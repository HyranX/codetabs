using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class Native
{
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent,EnumProc callback,IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetParent(IntPtr h, IntPtr parent);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetStyle(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW", SetLastError=true)] public static extern IntPtr SetStyle(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h,int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr h, ref Placement p);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr h, ref Placement p);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent,IntPtr child);
    [DllImport("kernel32.dll")] public static extern void SetLastError(uint error);
    [StructLayout(LayoutKind.Sequential)] public struct Placement { public int length, flags, show; public Point min, max; public RectangleEdges normal; }
    [StructLayout(LayoutKind.Sequential)] public struct RectangleEdges { public int left, top, right, bottom; }
    public static string Title(IntPtr h) { var s = new StringBuilder(1024); GetWindowText(h,s,s.Capacity); return s.ToString(); }
    public static void Parent(IntPtr h, IntPtr parent) { SetLastError(0); var old=SetParent(h,parent); int error=Marshal.GetLastWin32Error(); if(old==IntPtr.Zero && error!=0) throw new Win32Exception(error); }
    public static void Style(IntPtr h, int index, IntPtr value) { SetLastError(0); var old=SetStyle(h,index,value); int error=Marshal.GetLastWin32Error(); if(old==IntPtr.Zero && error!=0) throw new Win32Exception(error); }
    public static void FocusWindow(IntPtr h) {
        if(!IsWindow(h)) return;
        uint pid; uint target=GetWindowThreadProcessId(h,out pid);
        var info=HostApi.GuiInfo();
        if(target==0 || !HostApi.GetGUIThreadInfo(target,ref info) || info.focus==h) return;
        // SetParent already connects embedded windows' input queues. Never force
        // another attachment/retry: it resets key state during IME transitions.
        SetFocus(h);
    }
}

sealed class Hosted
{
    public IntPtr Handle, Parent, Style, ExStyle;
    public IntPtr LastFocus;
    public Native.Placement Placement;
    public WindowSnapshot Snapshot;
    public string CodexPath;
    public string ApplicationName;
    public bool IsVSCode;
    public bool Compatible;
    public Rectangle CompatBounds;
    public bool CompatVisible;
    public string BindingKey;
    public string ProjectPath,CompletionToken;
    public string OverviewKey,OriginalProjectPath;
    public string OverviewTitle,ProjectSearchText;
    public string RemoteHostId,RemoteKind,RemoteAlias,ConversationId;
    public string CodexStatus="unknown";
    public string CodexDetail="尚未关联会话；右键标签 → 关联 Codex 会话";
    public override string ToString() { return String.IsNullOrEmpty(ApplicationName)?Native.Title(Handle):ApplicationName+"  —  "+Native.Title(Handle); }
}

sealed class InputIsolationFixture : Form
{
    public const int BlockMessage=0x8001;
    public const int DelayedResizeMessage=0x8002;
    protected override void WndProc(ref Message message) {
        if(message.Msg==DelayedResizeMessage) {
            var resize=new Timer { Interval=180 };
            resize.Tick+=delegate {
                resize.Stop(); resize.Dispose();
                Native.RectangleEdges bounds; HostApi.GetWindowRect(Handle,out bounds);
                Native.SetWindowPos(Handle,IntPtr.Zero,0,0,bounds.right-bounds.left+61,bounds.bottom-bounds.top+37,0x0016);
            };
            resize.Start(); return;
        }
        if(message.Msg==BlockMessage) {
            string suffix=Process.GetCurrentProcess().Id.ToString();
            using(var started=System.Threading.EventWaitHandle.OpenExisting("CodeTabs-test-started-"+suffix))
            using(var release=System.Threading.EventWaitHandle.OpenExisting("CodeTabs-test-release-"+suffix)) {
                started.Set(); release.WaitOne(5000);
            }
            return;
        }
        base.WndProc(ref message);
    }
}

static class Program
{
    [STAThread] static void Main(string[] args)
    {
        bool smoke=args.Length>0 && args[0]=="--smoke-test";
        if(smoke) Console.Error.WriteLine("check: initializing");
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>0 && args[0]=="--conversation-navigation-test") { try { ConversationNavigation.Verify(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(1); } return; }
        if(args.Length>0 && args[0]=="--migrate-config") { try { PortableConfig.Initialize(); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(1); } return; }
        if(args.Length==2 && args[0]=="--overview-test") { Overview.Verify(args[1]); return; }
        if(args.Length==2 && args[0]=="--binding-test") { BindingStore.VerifyDisk(args[1]); return; }
        if(args.Length==2 && args[0]=="--window-layout-test") { try { SavedWindowLayout.Verify(args[1]); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(1); } return; }
        if(args.Length==2 && args[0]=="--window-layout-ui-test") {
            Exception failure=null;
            using(var form=new MainForm(false)) {
                form.Shown+=delegate { form.BeginInvoke((Action)delegate { try { form.VerifyWindowLayoutUi(args[1]); } catch(Exception ex) { failure=ex; } finally { form.Close(); } }); };
                Application.Run(form);
            }
            if(failure!=null) { Console.Error.WriteLine(failure); Environment.Exit(1); } return;
        }
        if(args.Length==2 && args[0]=="--notification-test") { try { CodexState.VerifyNotifications(args[1]); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(1); } return; }
        if(args.Length>0 && args[0]=="--startup-test") {
            using(var form=new MainForm(false)) using(var timer=new Timer { Interval=1200 }) {
                timer.Tick+=delegate { timer.Stop(); form.Close(); }; form.Shown+=delegate { timer.Start(); }; Application.Run(form);
            } return;
        }
        if(args.Length>0 && args[0]=="--test-window") { var fixture=new InputIsolationFixture { Text="Code Tabs test fixture", Size=new Size(640,480) }; fixture.Controls.Add(new TextBox { Multiline=true,Dock=DockStyle.Fill,Text="Focus test input" }); Application.Run(fixture); return; }
        if(args.Length>0 && args[0]=="--smoke-test") {
            try {
                CodexState.Verify(); CompletionBatch.Verify(); BindingStore.Verify(); Console.Error.WriteLine("check: overview"); Overview.Verify(); Console.Error.WriteLine("check: host");
                Exception failure=null;
                using(var form=new MainForm(false)) {
                    form.Shown+=delegate { form.BeginInvoke((Action)delegate {
                        try { Console.Error.WriteLine("check: layout"); form.VerifyLayout(); Console.Error.WriteLine("check: external"); form.VerifyExternalWindow(); }
                        catch(Exception ex) { failure=ex; }
                        finally { Console.Error.WriteLine("check: close"); form.Close(); }
                    }); };
                    Application.Run(form);
                }
                if(failure!=null) throw failure;
                Console.Error.WriteLine("check: passed");
            }
            catch(Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(1); } return;
        }
        try { PortableConfig.Initialize(); }
        catch(Exception ex) { MessageBox.Show("无法保存便携配置，请将程序放在可写入的目录后启动。\n"+ex.Message,"CodeTabs"); return; }
        Application.Run(new MainForm());
    }
}
