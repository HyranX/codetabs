using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

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
