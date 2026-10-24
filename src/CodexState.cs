using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

sealed class CodexSession
{
    public string Path,Cwd,Id; public DateTime Updated;
    public override string ToString() { return Updated.ToString("MM-dd HH:mm")+"  |  "+Cwd+"  |  "+Id; }
}

// Read-only adapter for local Codex rollout lifecycle events. No prompts are retained.
sealed class CodexState : IDisposable
{
    public static string Root { get { string home=Environment.GetEnvironmentVariable("CODEX_HOME"); return Path.Combine(String.IsNullOrEmpty(home)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"):home,"sessions"); } }
    sealed class Cursor
    {
        public DateTime ObserveAfter;
        public bool Live;
        public bool IsSubagent;
        public string ActiveTurn;
        public DateTime CandidateSince;
        public readonly List<string> Completions=new List<string>();
        public long Position; public string Partial="",State="unknown",Detail="尚无可识别状态",Completion="";
        public Decoder Decoder=Encoding.UTF8.GetDecoder();
    }
    sealed class Result { public string Path,State,Detail,Completion; public string[] Completions=new string[0]; }
    readonly DateTime observeAfter=DateTime.UtcNow;
    public Action<Hosted,string> TaskCompleted;
    readonly Dictionary<string,Cursor> cursors=new Dictionary<string,Cursor>(StringComparer.OrdinalIgnoreCase);
    Task<List<Result>> pending; DateTime next; bool disposed;
    public static List<CodexSession> Discover(bool allSources=false,string testHome=null) {
        var result=new List<CodexSession>();
        string home=testHome??Path.GetDirectoryName(Root);
        var roots=allSources?new[]{Path.Combine(home,"sessions"),Path.Combine(home,"archived_sessions")}:new[]{Path.Combine(home,"sessions")};
        var files=new List<FileInfo>();
        foreach(string root in roots) if(Directory.Exists(root)) files.AddRange(new DirectoryInfo(root).EnumerateFiles("*.jsonl",SearchOption.AllDirectories));
        foreach(var file in files.OrderByDescending(f=>f.LastWriteTimeUtc)) {
            try {
                using(var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                    var meta=Parse(reader.ReadLine()); var payload=Child(meta,"payload");
                    if(Text(meta,"type")!="session_meta" || IsSubagentSource(payload) || (!allSources && Text(payload,"source")!="vscode")) continue;
                    result.Add(new CodexSession { Path=file.FullName,Cwd=Text(payload,"cwd"),Id=Text(payload,"id"),Updated=file.LastWriteTime });
                }
            } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(ArgumentException) {}
        }
        return result;
    }
    static Dictionary<string,object> Parse(string line) { return new JavaScriptSerializer { MaxJsonLength=32*1024*1024 }.Deserialize<Dictionary<string,object>>(line??"{}"); }
    public static string ProjectDirectory(string path) {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) using(var reader=new StreamReader(stream,Encoding.UTF8)) return Text(Child(Parse(reader.ReadLine()),"payload"),"cwd");
    }
    static Dictionary<string,object> Child(Dictionary<string,object> obj,string key) { object value; return obj!=null&&obj.TryGetValue(key,out value)?value as Dictionary<string,object>:null; }
    static string Text(Dictionary<string,object> obj,string key) { object value; return obj!=null&&obj.TryGetValue(key,out value)?value as string:null; }
    static bool IsSubagentSource(Dictionary<string,object> payload) {
        var source=Child(payload,"source"); string name=Text(payload,"source");
        return (source!=null&&source.ContainsKey("subagent"))||(!String.IsNullOrEmpty(name)&&name.StartsWith("subagent",StringComparison.OrdinalIgnoreCase));
    }
    static void Apply(Cursor c,string line) {
        if(line.IndexOf("\"session_meta\"",StringComparison.Ordinal)<0 && line.IndexOf("\"event_msg\"",StringComparison.Ordinal)<0) return;
        try {
            var root=Parse(line); string type=Text(root,"type");
            if(type=="session_meta") { c.IsSubagent=IsSubagentSource(Child(root,"payload")); if(c.IsSubagent) c.Completions.Clear(); if(c.State=="unknown") { c.State="idle"; c.Detail="会话已创建，尚无任务开始事件"; } return; }
            if(type!="event_msg") return; string ev=Text(Child(root,"payload"),"type");
            string turn=Text(Child(root,"payload"),"turn_id");
            if((ev=="task_complete"||ev=="turn_aborted")&&!String.IsNullOrEmpty(turn)&&!String.IsNullOrEmpty(c.ActiveTurn)&&turn!=c.ActiveTurn) return;
            switch(ev) {
                case "task_started": c.State="running"; c.Completion=""; c.ActiveTurn=turn; c.Completions.Clear(); break;
                case "task_complete":
                    c.State="completed"; string timestamp=Text(root,"timestamp");
                    c.Completion=(Text(Child(root,"payload"),"turn_id")??"")+"|"+(timestamp??c.Position.ToString());
                    DateTimeOffset completedAt;
                    bool dated=DateTimeOffset.TryParse(timestamp,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out completedAt);
                    if(!c.IsSubagent&&(dated?completedAt.UtcDateTime>=c.ObserveAfter:c.Live)) {
                        if(!c.Completions.Contains(c.Completion)) { c.Completions.Clear(); c.Completions.Add(c.Completion); c.CandidateSince=DateTime.UtcNow; }
                    }
                    break;
                case "turn_aborted": c.State="interrupted"; c.Completions.Clear(); break;
                default: return;
            }
            c.Detail="本机会话事件："+ev+" · "+Text(root,"timestamp");
        } catch(ArgumentException) { c.State="unknown"; c.Detail="会话事件格式无法识别"; } catch(InvalidOperationException) { c.State="unknown"; c.Detail="会话事件格式无法识别"; }
    }
    Result Read(string path,DateTime? now=null) {
        try {
            Cursor c; if(!cursors.TryGetValue(path,out c)) { c=new Cursor { ObserveAfter=observeAfter }; cursors[path]=c; }
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                if(stream.Length<c.Position) { c=new Cursor { ObserveAfter=observeAfter }; cursors[path]=c; }
                // Idle/completed sessions normally have no new bytes. Avoid allocating
                // two 64K buffers per project per second just to discover EOF.
                if(stream.Length==c.Position) return Snapshot(path,c,now);
                stream.Position=c.Position; var bytes=new byte[65536]; var chars=new char[65536]; int remaining=8*1024*1024;
                while(remaining>0) {
                    int n=stream.Read(bytes,0,Math.Min(bytes.Length,remaining)); if(n==0) break;
                    c.Position+=n; remaining-=n;
                    int count=c.Decoder.GetChars(bytes,0,n,chars,0,false); string text=c.Partial+new string(chars,0,count); int start=0,index;
                    while((index=text.IndexOf('\n',start))>=0) { Apply(c,text.Substring(start,index-start)); start=index+1; }
                    c.Partial=text.Substring(start);
                    if(c.Partial.Length>32*1024*1024) throw new IOException("会话单行过大");
                }
                if(stream.Position<stream.Length) return new Result { Path=path,State="unknown",Detail="正在读取会话历史" };
            }
            return Snapshot(path,c,now);
        } catch(Exception ex) { return new Result { Path=path,State="unknown",Detail="无法读取状态："+ex.Message }; }
    }
    static Result Snapshot(string path,Cursor c,DateTime? now=null) {
        c.Live=true;
        bool ready=!c.IsSubagent&&c.State=="completed"&&c.Partial.Length==0&&(now??DateTime.UtcNow)>=c.CandidateSince.AddSeconds(5);
        var result=new Result { Path=path,State=c.State,Detail=c.Detail+"\n"+path,Completion=c.Completion,Completions=ready?c.Completions.ToArray():new string[0] };
        if(ready||c.IsSubagent||c.State!="completed") c.Completions.Clear(); return result;
    }
    public void Refresh(List<Hosted> windows) {
        if(disposed) return;
        if(pending!=null) {
            if(!pending.IsCompleted) return;
            if(pending.Status==TaskStatus.RanToCompletion) {
                var byPath=windows.Where(h=>h.CodexPath!=null).ToLookup(h=>h.CodexPath,StringComparer.OrdinalIgnoreCase);
                foreach(var r in pending.Result) {
                    foreach(var h in byPath[r.Path]) { h.CodexStatus=r.State; h.CodexDetail=r.Detail; h.CompletionToken=r.Completion; }
                    var source=byPath[r.Path].FirstOrDefault();
                    if(source!=null && TaskCompleted!=null) foreach(string token in r.Completions) TaskCompleted(source,token);
                }
            }
            pending=null;
        }
        if(DateTime.UtcNow<next) return; next=DateTime.UtcNow.AddSeconds(1);
        var paths=windows.Where(h=>!String.IsNullOrEmpty(h.CodexPath)).Select(h=>h.CodexPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(paths.Length>0) pending=Task.Run(()=>paths.Select(path=>Read(path)).ToList());
    }
    public static string Description(Hosted h) {
        string name=h.CodexStatus=="running"?"执行中":h.CodexStatus=="completed"?"已完成":h.CodexStatus=="idle"?"空闲":h.CodexStatus=="interrupted"?"已中断":"未知";
        return name+"\n"+h.CodexDetail;
    }
    public static void Draw(Graphics g,Hosted h,Rectangle r) {
        if(!h.IsVSCode) { using(var pen=new Pen(Color.LightSteelBlue,1.5f)) { g.DrawRectangle(pen,r.X+2,r.Y+3,13,11); g.DrawLine(pen,r.X+2,r.Y+6,r.X+15,r.Y+6); } return; }
        var old=g.SmoothingMode; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if(h.CodexStatus=="running") { using(var pen=new Pen(Color.FromArgb(115,220,255),2.5f)) g.DrawArc(pen,r,(Environment.TickCount&Int32.MaxValue)/4%360,265); }
        else if(h.CodexStatus=="completed") { using(var pen=new Pen(Color.FromArgb(100,245,157),2.5f)) g.DrawLines(pen,new[]{new Point(r.X+2,r.Y+8),new Point(r.X+6,r.Y+12),new Point(r.X+15,r.Y+3)}); }
        else if(h.CodexStatus=="idle") { using(var pen=new Pen(Color.Silver,2)) g.DrawEllipse(pen,r.X+4,r.Y+4,9,9); }
        else { using(var font=new Font("Segoe UI",10,FontStyle.Bold)) System.Windows.Forms.TextRenderer.DrawText(g,h.CodexStatus=="interrupted"?"!":"?",font,r,h.CodexStatus=="interrupted"?Color.Orange:Color.Silver,System.Windows.Forms.TextFormatFlags.HorizontalCenter|System.Windows.Forms.TextFormatFlags.VerticalCenter|System.Windows.Forms.TextFormatFlags.NoPadding|System.Windows.Forms.TextFormatFlags.SingleLine); }
        g.SmoothingMode=old;
    }
    public static void Verify() {
        var c=new Cursor();
        Apply(c,"{\"type\":\"session_meta\",\"payload\":{}}"); if(c.State!="idle") throw new Exception("Idle state failed");
        Apply(c,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}"); if(c.State!="running") throw new Exception("Running state failed");
        Apply(c,"{\"type\":\"response_item\",\"payload\":{\"type\":\"task_complete\"}}"); if(c.State!="running") throw new Exception("False completion");
        Apply(c,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}"); if(c.State!="completed") throw new Exception("Completed state failed");
        Apply(c,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}");
        Apply(c,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\"}}"); if(c.State!="interrupted") throw new Exception("Interrupted state failed");
        var start=new DateTime(2026,10,23,10,0,0,DateTimeKind.Utc);
        c=new Cursor { ObserveAfter=start };
        Apply(c,CompletionLine(start.AddDays(-1),"history"));
        if(Snapshot("test",c).Completions.Length!=0) throw new Exception("Historical completion notification");
        Apply(c,CompletionLine(start.AddSeconds(1),"first"));
        Apply(c,CompletionLine(start.AddSeconds(2),"second"));
        Apply(c,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}");
        var result=Snapshot("test",c);
        if(result.State!="running"||result.Completions.Length!=0||Snapshot("test",c,DateTime.UtcNow.AddSeconds(6)).Completions.Length!=0) throw new Exception("Resumed task emitted completion");
        c=new Cursor { ObserveAfter=start };
        Apply(c,CompletionLine(start.AddSeconds(1),"newly-discovered"));
        if(Snapshot("test",c).Completions.Length!=0||Snapshot("test",c,DateTime.UtcNow.AddSeconds(6)).Completions.Length!=1||Snapshot("test",c,DateTime.UtcNow.AddSeconds(7)).Completions.Length!=0) throw new Exception("Stable completion delay / deduplication failed");
        c=new Cursor { ObserveAfter=start };
        Apply(c,"{\"type\":\"session_meta\",\"payload\":{\"source\":{\"subagent\":{\"other\":\"guardian\"}}}}");
        Apply(c,CompletionLine(start.AddSeconds(1),"guardian"));
        if(!c.IsSubagent||Snapshot("test",c,DateTime.UtcNow.AddSeconds(6)).Completions.Length!=0) throw new Exception("Guardian completion notified");
        c=new Cursor { ObserveAfter=start };
        Apply(c,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"active\"}}");
        Apply(c,CompletionLine(start.AddSeconds(1),"older"));
        if(c.State!="running"||Snapshot("test",c,DateTime.UtcNow.AddSeconds(6)).Completions.Length!=0) throw new Exception("Other turn completed active task");
        Apply(c,CompletionLine(start.AddSeconds(2),"active"));
        Apply(c,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\",\"turn_id\":\"active\"}}");
        if(Snapshot("test",c,DateTime.UtcNow.AddSeconds(6)).Completions.Length!=0) throw new Exception("Interrupted task emitted completion");
    }
    static string CompletionLine(DateTime at,string id) { return "{\"timestamp\":\""+at.ToString("o")+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\""+id+"\"}}"; }
    public static void VerifyNotifications(string file) {
        string fixture=Path.Combine(Path.GetDirectoryName(file),"notification-discovery"),sessions=Path.Combine(fixture,"sessions");
        Directory.CreateDirectory(sessions);
        File.WriteAllText(Path.Combine(sessions,"human.jsonl"),"{\"type\":\"session_meta\",\"payload\":{\"source\":\"vscode\",\"id\":\"human\"}}\n",new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(sessions,"guardian.jsonl"),"{\"type\":\"session_meta\",\"payload\":{\"source\":{\"subagent\":{\"other\":\"guardian\"}},\"id\":\"guardian\"}}\n",new UTF8Encoding(false));
        var found=Discover(true,fixture); if(found.Count!=1||found[0].Id!="human") throw new Exception("Background subagent leaked into user catalogue");
        File.WriteAllText(file,CompletionLine(DateTime.UtcNow.AddDays(-1),"old")+"\n",new UTF8Encoding(false));
        using(var state=new CodexState()) {
            if(state.Read(file).Completions.Length!=0) throw new Exception("Initial read notified history");
            string line=CompletionLine(DateTime.UtcNow,"新任务");
            byte[] bytes=Encoding.UTF8.GetBytes(line+"\n"); int split=Array.IndexOf(bytes,(byte)0xe6)+1;
            if(split<=0) throw new Exception("UTF8 fixture missing");
            using(var stream=new FileStream(file,FileMode.Append)) stream.Write(bytes,0,split);
            if(state.Read(file).Completions.Length!=0) throw new Exception("Partial event notified");
            using(var stream=new FileStream(file,FileMode.Append)) stream.Write(bytes,split,bytes.Length-split);
            if(state.Read(file).Completions.Length!=0||state.Read(file,DateTime.UtcNow.AddSeconds(6)).Completions.Length!=1||state.Read(file,DateTime.UtcNow.AddSeconds(7)).Completions.Length!=0) throw new Exception("Incremental UTF8 stable completion failed");
            File.AppendAllText(file,CompletionLine(DateTime.UtcNow,"fast-a")+"\n"+CompletionLine(DateTime.UtcNow,"fast-b")+"\n",new UTF8Encoding(false));
            var windows=new List<Hosted> { new Hosted { CodexPath=file },new Hosted { CodexPath=file } }; int count=0;
            state.TaskCompleted=delegate { count++; };
            DateTime deadline=DateTime.UtcNow.AddSeconds(10);
            while(count<1&&DateTime.UtcNow<deadline) { state.Refresh(windows); System.Threading.Thread.Sleep(20); }
            if(count!=1||windows.Any(h=>h.CodexStatus!="completed")) throw new Exception("Stable latest completion dispatch duplicated or lost");
        }
        CompletionBatch.Verify(); Verify(); Console.Error.WriteLine("Completion notification regression passed (no system balloons emitted).");
    }
    public void Dispose() { disposed=true; }
}
