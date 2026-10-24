using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// Geometry is captured and restored in physical desktop pixels, including
// negative coordinates. WINDOWPLACEMENT alone may be rescaled on a DPI change.
sealed class WindowSnapshot
{
    public Native.Placement Placement;
    public Native.RectangleEdges Bounds,NormalBounds,Monitor,Work;
    public uint Process,Thread,Dpi;
    public static WindowSnapshot Capture(IntPtr window) {
        IntPtr old=WindowRestore.SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
            var s=new WindowSnapshot();
            s.Placement=new Native.Placement { length=Marshal.SizeOf(typeof(Native.Placement)) };
            if(!Native.GetWindowPlacement(window,ref s.Placement)||!HostApi.GetWindowRect(window,out s.Bounds)) throw new InvalidOperationException("无法记录窗口位置。");
            var monitor=WindowRestore.GetMonitor(WindowRestore.MonitorFromWindow(window,2));
            s.Monitor=monitor.monitor; s.Work=monitor.work;
            s.Thread=Native.GetWindowThreadProcessId(window,out s.Process);
            s.Dpi=WindowRestore.GetDpiForWindow(window);
            s.NormalBounds=s.Placement.normal;
            if((Native.GetStyle(window,-20).ToInt64()&0x80)==0) s.NormalBounds=WindowRestore.Offset(s.NormalBounds,s.Work.left-s.Monitor.left,s.Work.top-s.Monitor.top);
            if(s.Placement.show==1) s.NormalBounds=s.Bounds;
            return s;
        } finally { if(old!=IntPtr.Zero) WindowRestore.SetThreadDpiAwarenessContext(old); }
    }
}

static class WindowRestore
{
    [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref Native.RectangleEdges rect,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int size; public Native.RectangleEdges monitor,work; public uint flags; }
    internal static MonitorInfo GetMonitor(IntPtr handle) {
        var info=new MonitorInfo { size=Marshal.SizeOf(typeof(MonitorInfo)) };
        if(!GetMonitorInfo(handle,ref info)) throw new InvalidOperationException("无法读取显示器工作区。");
        return info;
    }
    internal static Native.RectangleEdges Offset(Native.RectangleEdges r,int x,int y) { r.left+=x; r.right+=x; r.top+=y; r.bottom+=y; return r; }
    internal static bool Equal(Native.RectangleEdges a,Native.RectangleEdges b) { return a.left==b.left&&a.top==b.top&&a.right==b.right&&a.bottom==b.bottom; }
    static bool SameWindow(IntPtr window,WindowSnapshot s) { uint pid; return Native.IsWindow(window)&&Native.GetWindowThreadProcessId(window,out pid)==s.Thread&&pid==s.Process; }
    internal static Native.RectangleEdges FitToWork(Native.RectangleEdges rect,Native.RectangleEdges work) {
        int width=Math.Min(rect.right-rect.left,work.right-work.left),height=Math.Min(rect.bottom-rect.top,work.bottom-work.top);
        int x=Math.Max(work.left,Math.Min(rect.left,work.right-width)),y=Math.Max(work.top,Math.Min(rect.top,work.bottom-height));
        return new Native.RectangleEdges { left=x,top=y,right=x+width,bottom=y+height };
    }
    public static Task<string> RestoreAsync(IntPtr window,WindowSnapshot snapshot) {
        return Task.Run(delegate {
            IntPtr old=SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { return Restore(window,snapshot); }
            catch(Exception ex) { return ex.Message; }
            finally { if(old!=IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
        });
    }
    static string Restore(IntPtr window,WindowSnapshot s) {
        if(!SameWindow(window,s)) return null;
        var originalMonitor=s.Monitor;
        IntPtr targetMonitor=MonitorFromRect(ref originalMonitor,2);
        var target=GetMonitor(targetMonitor);
        bool unchanged=Equal(target.monitor,s.Monitor)&&Equal(target.work,s.Work);
        var normal=unchanged?s.NormalBounds:FitToWork(s.NormalBounds,target.work);
        var placement=s.Placement; placement.flags|=4;
        // First establish the destination monitor/DPI in the normal state.
        placement.show=1;
        placement.normal=normal;
        if((Native.GetStyle(window,-20).ToInt64()&0x80)==0) placement.normal=Offset(normal,target.monitor.left-target.work.left,target.monitor.top-target.work.top);
        if(!Native.SetWindowPlacement(window,ref placement)) return "无法发送窗口还原请求。";
        var watch=Stopwatch.StartNew(); long lastMove=-200; int phase=0,stable=0;
        Native.RectangleEdges actual=new Native.RectangleEdges();
        while(watch.ElapsedMilliseconds<3000) {
            if(!SameWindow(window,s)) return null;
            var state=new Native.Placement { length=Marshal.SizeOf(typeof(Native.Placement)) };
            if(!Native.GetWindowPlacement(window,ref state)||!HostApi.GetWindowRect(window,out actual)) return "无法确认窗口还原结果。";
            bool matches=state.show==1&&Equal(actual,normal);
            if(phase==0) {
                if(matches) stable++; else stable=0;
                if(stable>=3) {
                    phase=1; stable=0;
                    placement.show=s.Placement.show;
                    if(!Native.SetWindowPlacement(window,ref placement)) return "无法恢复窗口显示状态。";
                    lastMove=watch.ElapsedMilliseconds;
                } else if(!matches&&watch.ElapsedMilliseconds-lastMove>=150) {
                    // The application may resize again while handling WM_DPICHANGED.
                    // Correct physical bounds after that message, without attaching queues.
                    if(state.show!=1) Native.ShowWindowAsync(window,1);
                    Native.SetWindowPos(window,IntPtr.Zero,normal.left,normal.top,normal.right-normal.left,normal.bottom-normal.top,0x4014);
                    lastMove=watch.ElapsedMilliseconds;
                }
            } else {
                bool final=state.show==s.Placement.show;
                if(s.Placement.show==1) final=final&&Equal(actual,normal);
                else if(s.Placement.show==3) final=final&&MonitorFromWindow(window,2)==targetMonitor;
                if(final) { if(++stable>=6) return null; } else {
                    stable=0;
                    if(watch.ElapsedMilliseconds-lastMove>=200) {
                        if(s.Placement.show==1) Native.SetWindowPos(window,IntPtr.Zero,normal.left,normal.top,normal.right-normal.left,normal.bottom-normal.top,0x4014);
                        else Native.SetWindowPlacement(window,ref placement);
                        lastMove=watch.ElapsedMilliseconds;
                    }
                }
            }
            Thread.Sleep(50);
        }
        return "窗口未在限定时间内恢复原尺寸（原 DPI "+s.Dpi+"，当前 DPI "+GetDpiForWindow(window)+"；当前位置 "+actual.left+","+actual.top+"，尺寸 "+(actual.right-actual.left)+"×"+(actual.bottom-actual.top)+"）。";
    }
}
