using System;
using System.IO;

static class PortableConfig
{
    public static string DirectoryPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"config"); } }
    public static string FilePath(string name) { return Path.Combine(DirectoryPath,name); }
    static readonly string[] Names={"codex-bindings.json","codex-viewed.json","codex-hidden.json","codex-directories.json","codex-projects.json","codex-pinned.json","codex-titles.json","shortcuts.txt","overview-settings.json","notification-settings.json"};
    public static void Initialize() {
        Migrate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodeTabs"),DirectoryPath);
    }
    static void Migrate(string legacy,string target) {
        Directory.CreateDirectory(target);
        string marker=Path.Combine(target,"migration-complete.txt");
        if(File.Exists(marker)) return;
        foreach(string name in Names) {
            string source=Path.Combine(legacy,name),destination=Path.Combine(target,name);
            if(File.Exists(source)&&!File.Exists(destination)) File.Copy(source,destination);
        }
        File.WriteAllText(marker,"Portable settings live in this folder. Existing settings were not overwritten.");
    }
    public static void Verify(string root) {
        root=Path.Combine(root,"portable-test-"+Guid.NewGuid().ToString("N"));
        string legacy=Path.Combine(root,"legacy-config"),portable=Path.Combine(root,"portable-config");
        Directory.CreateDirectory(legacy); File.WriteAllText(Path.Combine(legacy,"shortcuts.txt"),"original");
        Migrate(legacy,portable);
        if(File.ReadAllText(Path.Combine(portable,"shortcuts.txt"))!="original") throw new Exception("Portable migration failed");
        File.WriteAllText(Path.Combine(portable,"shortcuts.txt"),"portable");
        File.WriteAllText(Path.Combine(legacy,"shortcuts.txt"),"later");
        Migrate(legacy,portable);
        if(File.ReadAllText(Path.Combine(portable,"shortcuts.txt"))!="portable"||File.ReadAllText(Path.Combine(legacy,"shortcuts.txt"))!="later") throw new Exception("Portable migration overwrote preferences");
        string moved=Path.Combine(root,"moved-config"); Directory.CreateDirectory(moved);
        foreach(string file in Directory.GetFiles(portable)) File.Copy(file,Path.Combine(moved,Path.GetFileName(file)),true);
        if(File.ReadAllText(Path.Combine(moved,"shortcuts.txt"))!="portable") throw new Exception("Portable folder move failed");
    }
}
