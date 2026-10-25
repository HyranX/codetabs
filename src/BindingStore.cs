using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Linq;

sealed class BindingStore
{
    readonly string path;
    public BindingStore(string file=null) { path=file??PortableConfig.FilePath("codex-bindings.json"); }
    public static string Key(string app,string title) {
        string name=title??"";
        foreach(string suffix in new[]{" - Visual Studio Code - Insiders"," - Visual Studio Code"}) if(name.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)) { name=name.Substring(0,name.Length-suffix.Length); break; }
        int separator=name.LastIndexOf(" - ",StringComparison.Ordinal);
        if(separator>=0) name=name.Substring(separator+3);
        return (app??"Code").ToLowerInvariant()+"|"+name.Trim().TrimStart('●','•','*',' ').ToLowerInvariant();
    }
    public Dictionary<string,string> Read() {
        if(!File.Exists(path)) return new Dictionary<string,string>();
        using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) using(var reader=new StreamReader(file,Encoding.UTF8)) return Decode(reader.ReadToEnd());
    }
    static Dictionary<string,string> Decode(string json) { return String.IsNullOrWhiteSpace(json)?new Dictionary<string,string>():new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(json); }
    public void Save(string key,string session) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        // Lock, merge, then write so a later association supersedes the previous one.
        using(var file=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            string json; using(var reader=new StreamReader(file,Encoding.UTF8,true,4096,true)) json=reader.ReadToEnd();
            var map=Decode(json); if(String.IsNullOrEmpty(session)) map.Remove(key); else map[key]=session;
            byte[] data=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(map)); file.Position=0; file.Write(data,0,data.Length); file.SetLength(data.Length); file.Flush(true);
        }
    }
    public static void Verify() {
        string first=Key("Code","old.cs - 项目A - Visual Studio Code"),second=Key("Code","new.cs - 项目A - Visual Studio Code");
        if(first!=second||first==Key("Code","项目B - Visual Studio Code")) throw new Exception("Binding key failed");
        var map=new Dictionary<string,string>(); map[first]="old.jsonl"; map[second]="latest.jsonl";
        var restored=Decode(new JavaScriptSerializer().Serialize(map)); if(restored[first]!="latest.jsonl"||restored.Count!=1) throw new Exception("Binding latest-wins failed");
        restored.Remove(second); if(Decode(new JavaScriptSerializer().Serialize(restored)).Count!=0) throw new Exception("Binding removal failed");
    }
    public static void VerifyDisk(string file) {
        var store=new BindingStore(file); string key=Key("Code","test.cs - test-project - Visual Studio Code");
        store.Save(key,"first.jsonl"); store.Save(key,"latest.jsonl");
        if(new BindingStore(file).Read()[key]!="latest.jsonl") throw new Exception("Persisted latest association failed");
        store.Save(key,null); if(new BindingStore(file).Read().ContainsKey(key)) throw new Exception("Persisted removal failed");
    }
}

sealed class SavedWindow
{
    public string App { get; set; }
    public string Title { get; set; }
    public string ClassName { get; set; }
    public bool IsCode { get; set; }
    public long Handle { get; set; }
    public int ProcessId { get; set; }
    public long ProcessStarted { get; set; }
    public static SavedWindow Capture(Hosted window) {
        uint pid; Native.GetWindowThreadProcessId(window.Handle,out pid);
        var name=new StringBuilder(256); HostApi.GetClassName(window.Handle,name,256);
        var saved=new SavedWindow { App=window.ApplicationName??"",Title=Native.Title(window.Handle),ClassName=name.ToString(),IsCode=window.IsVSCode,Handle=window.Handle.ToInt64(),ProcessId=(int)pid };
        try { using(var process=Process.GetProcessById((int)pid)) saved.ProcessStarted=process.StartTime.ToUniversalTime().Ticks; } catch(System.ComponentModel.Win32Exception) {} catch(ArgumentException) {} catch(InvalidOperationException) {}
        return saved;
    }
}

sealed class SavedWindowLayout
{
    public int Version { get; set; }
    public int Selected { get; set; }
    public List<SavedWindow> Windows { get; set; }
    public SavedWindowLayout() { Version=1; Selected=-1; Windows=new List<SavedWindow>(); }
    public void Save(string path) {
        WriteJson(path,this);
    }
    internal static void WriteJson(string path,object value) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        // Keep the last complete configuration if writing is interrupted.
        string pending=path+".pending";
        try {
            using(var stream=new FileStream(pending,FileMode.Create,FileAccess.Write,FileShare.None)) {
                byte[] bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value));
                stream.Write(bytes,0,bytes.Length); stream.Flush(true);
            }
            if(File.Exists(path)) File.Replace(pending,path,null); else File.Move(pending,path);
        } finally { if(File.Exists(pending)) File.Delete(pending); }
    }
    public static SavedWindowLayout Read(string path) {
        if(!File.Exists(path)) throw new InvalidOperationException("尚未保存窗口组合，请先点击保存图标。");
        var layout=new JavaScriptSerializer().Deserialize<SavedWindowLayout>(File.ReadAllText(path,Encoding.UTF8));
        Validate(layout);
        return layout;
    }
    internal static void Validate(SavedWindowLayout layout) {
        if(layout==null || layout.Version!=1 || layout.Windows==null || layout.Windows.Count==0 || layout.Windows.Any(x=>x==null || String.IsNullOrWhiteSpace(x.App) || x.Title==null) || layout.Selected < -1 || layout.Selected>=layout.Windows.Count) throw new InvalidDataException("窗口组合配置无效，请重新保存。");
    }
    static bool SameApp(SavedWindow a,SavedWindow b) { return String.Equals(a.App,b.App,StringComparison.OrdinalIgnoreCase) && a.ClassName==b.ClassName; }
    static int Unique(List<int> matches) { return matches.Count==1?matches[0]:-1; }
    public static int Match(SavedWindow saved,List<SavedWindow> candidates,HashSet<int> used,List<SavedWindow> layout) {
        var eligible=Enumerable.Range(0,candidates.Count).Where(i=>!used.Contains(i)&&SameApp(saved,candidates[i])).ToList();
        var exact=eligible.Where(i=>saved.ProcessStarted!=0 && candidates[i].ProcessStarted==saved.ProcessStarted && candidates[i].ProcessId==saved.ProcessId && candidates[i].Handle==saved.Handle).ToList();
        if(exact.Count>0) return Unique(exact);
        exact=eligible.Where(i=>candidates[i].Title==saved.Title).ToList();
        if(exact.Count>0) return Unique(exact);
        if(saved.IsCode) {
            exact=eligible.Where(i=>BindingStore.Key(candidates[i].App,candidates[i].Title)==BindingStore.Key(saved.App,saved.Title)).ToList();
            return Unique(exact);
        }
        // A changed title is safe only for a single-window application in both lists.
        if(layout.Count(x=>SameApp(saved,x))==1 && candidates.Count(x=>SameApp(saved,x))==1) return Unique(eligible);
        return -1;
    }
    public static void Verify(string path) {
        var a=new SavedWindow { App="Code",ClassName="editor",Title="old.cs - Project A - Visual Studio Code",IsCode=true,Handle=1,ProcessId=10,ProcessStarted=100 };
        var b=new SavedWindow { App="Mail",ClassName="mail",Title="Inbox",Handle=2,ProcessId=20,ProcessStarted=200 };
        var layout=new SavedWindowLayout { Windows=new List<SavedWindow>{b,a},Selected=1 }; layout.Save(path);
        var read=Read(path); if(read.Windows[0].App!="Mail" || read.Windows[1].App!="Code" || read.Selected!=1) throw new Exception("Window order did not persist");
        var candidates=new List<SavedWindow>{a,b}; var used=new HashSet<int>();
        if(Match(read.Windows[0],candidates,used,read.Windows)!=1) throw new Exception("Mixed app matching failed");
        used.Add(1); if(Match(b,candidates,used,read.Windows)!=-1) throw new Exception("Window reused");
        a.Title="new.cs - Project A - Visual Studio Code"; a.ProcessStarted=101; a.Handle=3;
        if(Match(read.Windows[1],candidates,used,read.Windows)!=0) throw new Exception("VS Code project matching failed");
        candidates.Add(new SavedWindow { App="Code",ClassName="editor",Title=a.Title });
        if(Match(read.Windows[1],candidates,used,read.Windows)!=-1) throw new Exception("Ambiguous window matched");
        candidates.Clear(); if(Match(b,candidates,new HashSet<int>(),read.Windows)!=-1) throw new Exception("Missing window matched");
        layout.Windows.Reverse(); layout.Selected=0; layout.Save(path);
        if(Read(path).Windows[0].App!="Code") throw new Exception("Saved layout overwrite failed");
        WindowLayoutLibrary.Verify(path,layout);
        Console.Error.WriteLine("Window layout: persistence, ordering, mixed apps, reuse, changed titles, ambiguous and missing windows passed");
    }
}

sealed class NamedWindowLayout
{
    public string Id { get; set; }
    public string Name { get; set; }
    public SavedWindowLayout Layout { get; set; }
    public override string ToString() { return Name+"  ·  "+Layout.Windows.Count+" 个窗口"; }
}

sealed class WindowLayoutLibrary
{
    public int Version { get; set; }
    public List<NamedWindowLayout> Items { get; set; }
    public WindowLayoutLibrary() { Version=1; Items=new List<NamedWindowLayout>(); }
    public static WindowLayoutLibrary Read(string path,string legacy) {
        if(!File.Exists(path)) {
            var library=new WindowLayoutLibrary();
            if(File.Exists(legacy)) { library.Items.Add(new NamedWindowLayout { Id=Guid.NewGuid().ToString("N"),Name="之前保存的组合",Layout=SavedWindowLayout.Read(legacy) }); library.Save(path); }
            return library;
        }
        var result=new JavaScriptSerializer().Deserialize<WindowLayoutLibrary>(File.ReadAllText(path,Encoding.UTF8));
        if(result==null || result.Version!=1 || result.Items==null || result.Items.Any(x=>x==null || String.IsNullOrWhiteSpace(x.Id) || String.IsNullOrWhiteSpace(x.Name)) || result.Items.Select(x=>x.Id).Distinct().Count()!=result.Items.Count) throw new InvalidDataException("组合列表配置无效，原文件未改动。");
        foreach(var entry in result.Items) SavedWindowLayout.Validate(entry.Layout);
        return result;
    }
    public void Save(string path) { SavedWindowLayout.WriteJson(path,this); }
    public NamedWindowLayout Add(string name,SavedWindowLayout layout) {
        name=(name??"").Trim();
        if(name.Length==0 || name.Length>80) throw new InvalidOperationException("请输入 1–80 个字符的组合名称。");
        if(Items.Any(x=>String.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("已有同名组合，请使用不同名称。");
        SavedWindowLayout.Validate(layout);
        var entry=new NamedWindowLayout { Id=Guid.NewGuid().ToString("N"),Name=name,Layout=layout }; Items.Add(entry); return entry;
    }
    public static void Verify(string legacy,SavedWindowLayout layout) {
        string path=legacy+".library-"+Guid.NewGuid().ToString("N")+".json";
        var library=Read(path,legacy);
        if(library.Items.Count!=1 || library.Items[0].Layout.Windows[0].App!=layout.Windows[0].App) throw new Exception("Legacy layout migration failed");
        var second=library.Add("工作组合",layout); library.Save(path);
        library=Read(path,legacy); if(library.Items.Count!=2 || library.Items[1].Id!=second.Id) throw new Exception("Named layout persistence failed");
        bool rejected=false; try { library.Add("工作组合",layout); } catch(InvalidOperationException) { rejected=true; }
        if(!rejected) throw new Exception("Duplicate layout name accepted");
        library.Items.RemoveAll(x=>x.Id==second.Id); library.Save(path);
        if(Read(path,legacy).Items.Count!=1) throw new Exception("Layout deletion failed");
        library.Items.Clear(); library.Save(path);
        if(Read(path,legacy).Items.Count!=0) throw new Exception("Deleted legacy layout reimported");
        Console.Error.WriteLine("Layout library: migration, multiple named layouts, duplicate names and deletion passed");
    }
}
