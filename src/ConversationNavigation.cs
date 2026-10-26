using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Uses the installed Codex URI handler. Never sends a prompt or starts a turn.
static class ConversationNavigation
{
    public static string ThreadId(Hosted project) {
        string value=project.ConversationId;
        if(String.IsNullOrWhiteSpace(value) && !String.IsNullOrEmpty(project.CodexPath)) value=CodexState.ConversationId(project.CodexPath);
        if(String.IsNullOrWhiteSpace(value)) return null;
        Guid id; if(!Guid.TryParse(value,out id)) throw new InvalidOperationException("卡片没有有效的 Codex 对话标识。");
        return id.ToString("D");
    }
    public static int WindowId(string status,string title) {
        var matches=Regex.Matches(status,@"window \[(\d+)\] \((.*)\)\s*$",RegexOptions.Multiline)
            .Cast<Match>().Where(m=>m.Groups[2].Value==title).ToList();
        int id;
        if(matches.Count!=1 || !Int32.TryParse(matches[0].Groups[1].Value,out id) || id<=0)
            throw new InvalidOperationException("无法唯一定位对应的 VS Code 窗口，请等待窗口加载完成后重试。");
        return id;
    }
    public static void VerifyInstance(string status,uint processId) {
        var first=Regex.Match(status,@"^\s*\d+\s+\d+\s+(\d+)\s+\S",RegexOptions.Multiline);
        uint actual;
        if(!first.Success||!UInt32.TryParse(first.Groups[1].Value,out actual)||actual!=processId)
            throw new InvalidOperationException("当前项目使用独立的 VS Code 实例或用户数据目录，无法安全定位对话跳转目标。");
    }
    public static string UriFor(string threadId,int windowId,bool insiders) {
        Guid id; if(!Guid.TryParse(threadId,out id)||windowId<=0) throw new ArgumentException("Invalid conversation route");
        return (insiders?"vscode-insiders":"vscode")+"://openai.chatgpt/local/"+id.ToString("D")+"?windowId="+windowId;
    }
    static string Quote(string value) {
        var text=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) {
            if(c=='\\') { slashes++; continue; }
            text.Append('\\',c=='"'?slashes*2+1:slashes); text.Append(c); slashes=0;
        }
        text.Append('\\',slashes*2); return text.Append('"').ToString();
    }
    static string CliPath(string executable) {
        string root=Path.GetDirectoryName(executable);
        // Recent VS Code installs keep resources in a commit-named subdirectory.
        foreach(string name in new[]{"code.cmd","code-insiders.cmd"}) {
            string launcher=Path.Combine(root,"bin",name);
            if(!File.Exists(launcher)) continue;
            var match=Regex.Match(File.ReadAllText(launcher),@"%~dp0\.\.\\([^""\r\n]*resources\\app\\out\\cli\.js)",RegexOptions.IgnoreCase);
            if(match.Success) {
                string candidate=Path.GetFullPath(Path.Combine(root,match.Groups[1].Value));
                if(candidate.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)&&File.Exists(candidate)) return candidate;
            }
        }
        string classic=Path.Combine(root,"resources","app","out","cli.js");
        if(File.Exists(classic)) return classic;
        throw new FileNotFoundException("未找到当前 VS Code 的命令行入口，请修复或更新 VS Code 安装。");
    }
    public static string RunCli(string executable,params string[] arguments) {
        var info=new ProcessStartInfo(executable,Quote(CliPath(executable))+" "+String.Join(" ",arguments.Select(Quote))) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true
        };
        info.EnvironmentVariables["ELECTRON_RUN_AS_NODE"]="1"; info.EnvironmentVariables.Remove("VSCODE_DEV");
        using(var process=Process.Start(info)) {
            var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(15000)) { try { process.Kill(); } catch(InvalidOperationException) {} throw new TimeoutException("VS Code 对话跳转请求超时。"); }
            if(process.ExitCode!=0) throw new InvalidOperationException("VS Code 未接受对话跳转请求（退出码 "+process.ExitCode+"）。");
            error.GetAwaiter().GetResult(); return output.GetAwaiter().GetResult();
        }
    }
    public static void Verify() {
        const string id="12345678-1234-1234-1234-123456789abc";
        string status="0 281 31256 window [3] (other - Visual Studio Code)\r\n0 303 50764 window [14] (项目 (测试) - Visual Studio Code)\r\n";
        if(WindowId(status,"项目 (测试) - Visual Studio Code")!=14) throw new Exception("Conversation target window parsing failed");
        bool rejected=false; try { WindowId(status+status,"项目 (测试) - Visual Studio Code"); } catch(InvalidOperationException) { rejected=true; }
        if(!rejected) throw new Exception("Ambiguous conversation target accepted");
        rejected=false; try { WindowId(status,"missing"); } catch(InvalidOperationException) { rejected=true; }
        if(!rejected) throw new Exception("Missing conversation target accepted");
        if(UriFor(id,14,false)!="vscode://openai.chatgpt/local/"+id+"?windowId=14" || !UriFor(id,3,true).StartsWith("vscode-insiders://")) throw new Exception("Conversation URI routing failed");
        if(ThreadId(new Hosted { ConversationId=id })!=id || ThreadId(new Hosted())!=null) throw new Exception("Conversation identity failed");
        rejected=false; try { UriFor("../new?prompt=run",1,false); } catch(ArgumentException) { rejected=true; }
        if(!rejected) throw new Exception("Invalid conversation ID accepted");
        VerifyInstance("0 273 56992 code\r\n"+status,56992);
        rejected=false; try { VerifyInstance("0 273 56992 code\r\n"+status,1); } catch(InvalidOperationException) { rejected=true; }
        if(!rejected) throw new Exception("Wrong VS Code instance accepted");
        Console.Error.WriteLine("Conversation routing: IDs, exact window targeting, ambiguity and URI validation passed");
    }
}
