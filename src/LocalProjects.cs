using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

// Only project identifiers, names, directories and rollout locations are read.
// No prompts, credentials, or Codex records are modified.
sealed class LocalProject
{
    public string Id,Name,Path,SessionPath,HostId,HostKind,Alias;
    public bool Registered;
}
static class LocalProjects
{
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int ms);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int size,out IntPtr stmt,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt,int index);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr stmt,int index);
    static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text+"\0"); }
    static string Column(IntPtr stmt,int index) {
        IntPtr ptr=sqlite3_column_text(stmt,index); if(ptr==IntPtr.Zero) return null;
        int count=sqlite3_column_bytes(stmt,index); var bytes=new byte[count];
        Marshal.Copy(ptr,bytes,0,count); return Encoding.UTF8.GetString(bytes);
    }
    static List<string[]> Query(IntPtr db,string sql,bool optional=false) {
        IntPtr stmt; int rc=sqlite3_prepare_v2(db,Utf8(sql),-1,out stmt,IntPtr.Zero);
        if(rc!=0) { if(stmt!=IntPtr.Zero) sqlite3_finalize(stmt); if(optional) return new List<string[]>(); throw new IOException("索引结构无法读取（"+rc+"）"); }
        try {
            var rows=new List<string[]>(); int columns=sqlite3_column_count(stmt);
            while((rc=sqlite3_step(stmt))==100) { var row=new string[columns]; for(int i=0;i<columns;i++) row[i]=Column(stmt,i); rows.Add(row); }
            if(rc!=101) throw new IOException("索引读取暂不可用（"+rc+"）");
            return rows;
        } finally { sqlite3_finalize(stmt); }
    }
    static string LatestState(string folder) {
        if(!Directory.Exists(folder)) return null;
        return Directory.GetFiles(folder,"state_*.sqlite").OrderByDescending(p=> {
            int number; return Int32.TryParse(Path.GetFileNameWithoutExtension(p).Substring(6),out number)?number:0;
        }).FirstOrDefault();
    }
    static string Value(Dictionary<string,object> map,string key) { object value; return map!=null&&map.TryGetValue(key,out value)?value as string:null; }
    static IEnumerable<Dictionary<string,object>> Objects(Dictionary<string,object> map,string key) {
        object value; if(!map.TryGetValue(key,out value)) yield break;
        var array=value as System.Collections.IEnumerable; if(array==null||value is string) yield break;
        foreach(var item in array) { var obj=item as Dictionary<string,object>; if(obj!=null) yield return obj; }
    }
    public static List<LocalProject> Discover(string home,out string warning) {
        var result=new List<LocalProject>(); var warnings=new List<string>();
        foreach(string file in new[]{LatestState(home),LatestState(Path.Combine(home,"sqlite")),Path.Combine(home,"sqlite","codex-dev.db")}.Where(p=>p!=null&&File.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase)) {
            IntPtr db=IntPtr.Zero;
            try {
                // SQLITE_OPEN_READONLY; never CREATE or READWRITE.
                int rc=sqlite3_open_v2(Utf8(file),out db,1,IntPtr.Zero);
                if(rc!=0) throw new IOException("索引暂不可读（"+rc+"）");
                sqlite3_busy_timeout(db,300);
                if(Path.GetFileName(file)=="codex-dev.db") {
                    var columns=new HashSet<string>(Query(db,"PRAGMA table_info(local_thread_catalog)").Select(r=>r[1]));
                    string title=columns.Contains("display_title")?"c.display_title":"NULL";
                    foreach(var r in Query(db,"SELECT c.thread_id,c.cwd,"+title+",c.host_id,h.host_kind FROM local_thread_catalog c JOIN local_thread_catalog_hosts h ON h.host_id=c.host_id WHERE h.host_kind<>'chatgpt' ORDER BY c.source_updated_at DESC"))
                        result.Add(new LocalProject { Id=r[0],Path=r[1],Name=r[2],HostId=r[4]=="local"?null:r[3],HostKind=r[4] });
                } else {
                    var tables=new HashSet<string>(Query(db,"SELECT name FROM sqlite_master WHERE type='table'").Select(r=>r[0]));
                    if(tables.Contains("projects")&&tables.Contains("project_roots"))
                        foreach(var r in Query(db,"SELECT p.id,p.name,r.path FROM projects p LEFT JOIN project_roots r ON r.project_id=p.id ORDER BY p.position,r.position"))
                            result.Add(new LocalProject { Id=r[0],Name=r[1],Path=r[2],Registered=true });
                    if(tables.Contains("threads")) {
                        var columns=new HashSet<string>(Query(db,"PRAGMA table_info(threads)").Select(r=>r[1]));
                        string title=columns.Contains("title")?"title":"NULL";
                        foreach(var r in Query(db,"SELECT id,cwd,rollout_path,source,"+title+" FROM threads ORDER BY updated_at DESC"))
                            if((r[3]??"").IndexOf("subagent",StringComparison.OrdinalIgnoreCase)<0)
                                result.Add(new LocalProject { Id=r[0],Path=r[1],SessionPath=r[2],Name=r[4] });
                    }
                }
            } catch(Exception ex) { warnings.Add(Path.GetFileName(file)+"："+ex.Message); }
            finally { if(db!=IntPtr.Zero) sqlite3_close(db); }
        }
        try {
            string stateFile=Path.Combine(home,".codex-global-state.json");
            if(File.Exists(stateFile)) {
                var state=new JavaScriptSerializer { MaxJsonLength=32*1024*1024 }.Deserialize<Dictionary<string,object>>(File.ReadAllText(stateFile,Encoding.UTF8));
                var aliases=Objects(state,"codex-managed-remote-connections").Where(o=>Value(o,"hostId")!=null).GroupBy(o=>Value(o,"hostId")).ToDictionary(g=>g.Key,g=>Value(g.First(),"alias"));
                foreach(var item in Objects(state,"remote-projects")) result.Add(new LocalProject { Id=Value(item,"id"),Name=Value(item,"label"),Path=Value(item,"remotePath"),HostId=Value(item,"hostId"),HostKind="ssh",Registered=true });
                foreach(var item in result.Where(p=>p.HostId!=null)) { string alias; if(aliases.TryGetValue(item.HostId,out alias)) item.Alias=alias; }
            }
        } catch(Exception ex) { warnings.Add("项目登记文件："+ex.Message); }
        warning=String.Join("；",warnings); return result;
    }
}
