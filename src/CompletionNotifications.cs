using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// Shared by the tab and overview monitors: one notification per actual event.
sealed class CompletionBatch
{
    readonly Dictionary<string,HashSet<string>> delivered=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    sealed class Pending { public string Title; public Func<bool> IsCurrent; }
    readonly List<Pending> pending=new List<Pending>();
    public bool Enabled=true;
    public void Add(string path,string token,string title,Func<bool> isCurrent=null) {
        if(String.IsNullOrEmpty(path)||String.IsNullOrEmpty(token)) return;
        HashSet<string> tokens;
        if(!delivered.TryGetValue(path,out tokens)) { tokens=new HashSet<string>(StringComparer.Ordinal); delivered.Add(path,tokens); }
        if(!tokens.Add(token)||!Enabled) return;
        pending.Add(new Pending { Title=String.IsNullOrWhiteSpace(title)?"未命名项目":title.Replace('\r',' ').Replace('\n',' '),IsCurrent=isCurrent });
    }
    public string Take() {
        if(pending.Count==0) return null;
        var current=pending.Where(p=>p.IsCurrent==null||p.IsCurrent()).Select(p=>p.Title).ToList();
        pending.Clear();
        if(!Enabled||current.Count==0) return null;
        string message=current.Count==1?current[0]+" 的 Codex 任务已完成。":current.Count+" 个 Codex 任务已完成：\n"+String.Join("、",current.Distinct().Take(4));
        if(message.Length>220) message=message.Substring(0,220)+"…";
        return message+"\n点击查看 Codex 总览。";
    }
    public static void Verify() {
        var batch=new CompletionBatch();
        batch.Add("session","one","项目"); batch.Add("SESSION","one","重复");
        string first=batch.Take(); if(first==null||first.Contains("重复")||batch.Take()!=null) throw new Exception("Completion deduplication failed");
        batch.Add("session","one","历史"); if(batch.Take()!=null) throw new Exception("Completion replayed after refresh");
        batch.Add("session","two","项目"); batch.Add("other","one","其他");
        if(!batch.Take().StartsWith("2 个")) throw new Exception("Completion batching failed");
        batch.Enabled=false; batch.Add("session","off","关闭期间"); if(batch.Take()!=null) throw new Exception("Disabled notification emitted");
        batch.Enabled=true; batch.Add("session","off","旧事件"); if(batch.Take()!=null) throw new Exception("Disabled event replayed");
        batch.Add("session","three",new string('长',1000)); if(batch.Take().Length>255) throw new Exception("Balloon text too long");
        bool completed=true; batch.Add("session","resumed","继续执行",()=>completed); completed=false;
        if(batch.Take()!=null) throw new Exception("Resumed task emitted queued notification");
    }
}

sealed class CompletionNotifications : IDisposable
{
    readonly CompletionBatch batch=new CompletionBatch();
    readonly BindingStore settings=new BindingStore(PortableConfig.FilePath("notification-settings.json"));
    readonly NotifyIcon icon;
    readonly Timer timer=new Timer { Interval=1000 };
    public bool Enabled { get { return batch.Enabled; } }
    public CompletionNotifications(Icon appIcon,Action openOverview) {
        try { string value; if(settings.Read().TryGetValue("completion-enabled",out value)) batch.Enabled=value!="false"; } catch(Exception) {}
        icon=new NotifyIcon { Icon=appIcon,Text="CodeTabs · Codex 完成提醒",Visible=batch.Enabled };
        icon.BalloonTipClicked+=delegate { openOverview(); };
        icon.DoubleClick+=delegate { openOverview(); };
        timer.Tick+=delegate {
            timer.Stop(); string message=batch.Take();
            if(message!=null) icon.ShowBalloonTip(6000,"Codex 任务已完成",message,ToolTipIcon.Info);
        };
    }
    public void SetEnabled(bool enabled) {
        settings.Save("completion-enabled",enabled?"true":"false");
        batch.Enabled=enabled; icon.Visible=enabled;
        if(!enabled) { timer.Stop(); batch.Take(); }
    }
    public void Completed(Hosted project,string token,string title) {
        batch.Add(project.CodexPath,token,title,()=>project.CodexStatus=="completed"&&project.CompletionToken==token);
        if(batch.Enabled&&!timer.Enabled) timer.Start();
    }
    public void Dispose() { timer.Dispose(); icon.Visible=false; icon.Dispose(); }
}
