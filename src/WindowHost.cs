using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class HostApi
{
    public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    public delegate void WinEventProc(IntPtr hook,uint ev,IntPtr window,int objectId,int childId,uint thread,uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc proc,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [StructLayout(LayoutKind.Sequential)] public struct GuiThreadInfo { public int size,flags; public IntPtr active,focus,capture,menuOwner,moveSize,caret; public Native.RectangleEdges caretRect; }
    public static GuiThreadInfo GuiInfo() { return new GuiThreadInfo { size=Marshal.SizeOf(typeof(GuiThreadInfo)) }; }
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h,uint command);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Native.RectangleEdges rect);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,System.Text.StringBuilder name,int count);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out int value,int size);
    [StructLayout(LayoutKind.Sequential)] public struct MouseData { public Point point; public uint data,flags,time; public UIntPtr extra; }
}

sealed class ShortcutBox : TextBox
{
    public Keys Shortcut;
    public ShortcutBox(Keys keys) { ReadOnly=true; Set(keys); }
    public void Set(Keys keys) { Shortcut=keys; Text=new KeysConverter().ConvertToString(keys); }
    protected override bool ProcessCmdKey(ref Message msg, Keys data) {
        var key=data & Keys.KeyCode;
        if(key==Keys.Tab) return base.ProcessCmdKey(ref msg,data);
        if(key!=Keys.ControlKey && key!=Keys.ShiftKey && key!=Keys.Menu && key!=Keys.LWin && key!=Keys.RWin) Set(data);
        return true;
    }
}

sealed class TabStrip : Control
{
    public readonly List<Hosted> Items;
    public Hosted Selected;
    public Action<Hosted> SelectTab;
    public Action<Hosted> SelectForMenu;
    public Action<Point> ShowMenu;
    public Action AddAll, NewWindow, RemoveCode, SaveLayout, RestoreLayout;
    public Action DismissMenu;
    public Action<Hosted> CloseTab, DetachTab;
    public Action<Point> PickWindowAt;
    public Action Refocus;
    public Action PickingChanged;
    public Action TogglePin;
    public Action ShowOverview;
    public bool OverviewSelected=true;
    public bool Pinned;
    public bool ExternalDragOver;
    bool picking;
    readonly ToolTip tip=new ToolTip();
    int offset, dragIndex=-1; Point dragStart; bool dragging; int dropIndex=-1;
    Point hoverPoint=new Point(-1,-1);
    int[] tabWidths=new int[0];
    int dpi=96;
    Font dpiFont;
    int TabWidth { get { return (int)Math.Round(250*dpi/96.0); } }
    const int HomeWidth=40;
    int TabLeft(int i) { int x=0; for(int j=0;j<i && j<tabWidths.Length;j++) x+=tabWidths[j]; return x; }
    string TabTitle(int i) { return Native.Title(Items[i].Handle).Replace(" - Visual Studio Code",""); }
    Rectangle TabRect(int i) { return new Rectangle(HomeWidth+TabLeft(i)-offset+3,4,(i<tabWidths.Length?tabWidths[i]:TabWidth)-6,Height-8); }
    internal static int[] AllocateWidths(int[] desired,int available,int maximum,int minimum) {
        var widths=Enumerable.Repeat(maximum,desired.Length).ToArray();
        int excess=widths.Sum()-available;
        foreach(int i in Enumerable.Range(0,desired.Length).OrderBy(i=>desired[i])) {
            if(excess<=0) break;
            int take=Math.Min(excess,Math.Max(0,widths[i]-Math.Max(minimum,Math.Min(maximum,desired[i]))));
            widths[i]-=take; excess-=take;
        }
        while(excess>0) {
            int widest=widths.Length==0?minimum:widths.Max();
            if(widest<=minimum) break;
            var shrink=Enumerable.Range(0,widths.Length).Where(i=>widths[i]==widest).ToArray();
            int next=widths.Where(w=>w<widest).DefaultIfEmpty(minimum).Max();
            int step=Math.Min(widest-next,Math.Max(1,(excess+shrink.Length-1)/shrink.Length));
            foreach(int i in shrink) { int take=Math.Min(excess,Math.Min(step,widths[i]-minimum)); widths[i]-=take; excess-=take; }
        }
        return widths;
    }
    void LayoutTabs() {
        if(Items==null) return;
        int minimum=104+(int)Math.Round(24*dpi/96.0);
        tabWidths=AllocateWidths(Enumerable.Range(0,Items.Count).Select(i=>TextRenderer.MeasureText(TabTitle(i),Font).Width+104).ToArray(),ViewWidth,TabWidth,minimum);
    }
    public void RefreshDpi() {
        if(!IsHandleCreated) return;
        int next=(int)WindowRestore.GetDpiForWindow(Handle); if(next<=0) next=96;
        if(next==dpi && dpiFont!=null) return;
        dpi=next;
        var old=dpiFont; dpiFont=new Font("Microsoft YaHei UI",13*dpi/96f,FontStyle.Regular,GraphicsUnit.Pixel); Font=dpiFont;
        Height=Math.Max(36,Font.Height+16); if(old!=null) old.Dispose(); Reveal();
    }
    Rectangle ActionRect(int i,bool close) { var r=TabRect(i); return new Rectangle(r.Right-(close?30:60),r.Y+3,27,r.Height-6); }
    public TabStrip(List<Hosted> items) {
        Items=items; Height=36; Dock=DockStyle.Top; Cursor=Cursors.Hand;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.StandardDoubleClick,true);
    }
    int MenuWidth { get { return 36; } }
    int AllWidth { get { return 36; } }
    int PickWidth { get { return 36; } }
    int PinWidth { get { return 36; } }
    public int MinimumStripWidth { get { return HomeWidth+MenuWidth+PinWidth+AllWidth+PickWidth+144+150; } }
    int Space { get { return Math.Max(0,Width-MenuWidth-PinWidth-AllWidth-PickWidth-144); } }
    Rectangle MenuRect { get { return new Rectangle(Width-MenuWidth,0,MenuWidth,Height); } }
    Rectangle PinRect { get { return new Rectangle(MenuRect.X-PinWidth,0,PinWidth,Height); } }
    Rectangle RestoreRect { get { return new Rectangle(PinRect.X-36,0,36,Height); } }
    Rectangle SaveRect { get { return new Rectangle(RestoreRect.X-36,0,36,Height); } }
    Rectangle RemoveRect { get { return new Rectangle(SaveRect.X-36,0,36,Height); } }
    Rectangle AllRect { get { return new Rectangle(RemoveRect.X-AllWidth,0,AllWidth,Height); } }
    Rectangle PickRect { get { return new Rectangle(AllRect.X-PickWidth,0,PickWidth,Height); } }
    Rectangle NewRect { get { return new Rectangle(PickRect.X-36,0,36,Height); } }
    public bool IsPicking { get { return picking; } }
    public void OpenMenuForTest(Hosted h) { var r=TabRect(Items.IndexOf(h)); OnMouseDown(new MouseEventArgs(MouseButtons.Right,1,r.X+40,r.Y+10,0)); }
    public void StartPick() { picking=true; Capture=true; Cursor=Cursors.Cross; Invalidate(); if(PickingChanged!=null) PickingChanged(); }
    public void CancelPick() { picking=false; Capture=false; Cursor=Cursors.Hand; Invalidate(); if(PickingChanged!=null) PickingChanged(); }
    int ViewWidth { get { return Math.Max(1,Space-HomeWidth); } }
    int Hit(Point p) { if(p.X<HomeWidth || p.X>=Space) return -1; int x=p.X-HomeWidth+offset; for(int i=0;i<tabWidths.Length;i++) { if(x<tabWidths[i]) return i; x-=tabWidths[i]; } return -1; }
    public void Reveal() { LayoutTabs(); int i=Items.IndexOf(Selected); if(i>=0) { int left=TabLeft(i); if(left<offset) offset=left; if(left+tabWidths[i]>offset+ViewWidth) offset=left+tabWidths[i]-ViewWidth; } Clamp(); Invalidate(); }
    void Clamp() { offset=Math.Max(0,Math.Min(offset,Math.Max(0,tabWidths.Sum()-ViewWidth))); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Reveal(); }
    protected override void OnPaint(PaintEventArgs e) {
        LayoutTabs(); Clamp();
        e.Graphics.Clear(Color.FromArgb(25,29,37));
        using(var brush=new SolidBrush(OverviewSelected?Color.FromArgb(34,112,204):Color.FromArgb(49,55,66))) e.Graphics.FillRectangle(brush,new Rectangle(3,4,HomeWidth-6,Height-8));
        DrawToolbarIcon(e.Graphics,new Rectangle(0,0,HomeWidth,Height),"home",Color.White);
        e.Graphics.SetClip(new Rectangle(HomeWidth,0,Math.Max(0,Space-HomeWidth),Height));
        for(int i=0;i<Items.Count;i++) {
            var r=TabRect(i); bool active=Items[i]==Selected;
            using(var brush=new SolidBrush(active?Color.FromArgb(34,112,204):Color.FromArgb(49,55,66))) e.Graphics.FillRectangle(brush,r);
            if(active) using(var brush=new SolidBrush(Color.FromArgb(108,203,255))) e.Graphics.FillRectangle(brush,r.X,r.Bottom-3,r.Width,3);
            string title=TabTitle(i);
            CodexState.Draw(e.Graphics,Items[i],new Rectangle(r.X+9,r.Y+(r.Height-17)/2,17,17));
            TextRenderer.DrawText(e.Graphics,title,Font,new Rectangle(r.X+33,r.Y,r.Width-97,r.Height-2),active?Color.White:Color.FromArgb(198,205,216),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            DrawAction(e.Graphics,ActionRect(i,false),false);
            DrawAction(e.Graphics,ActionRect(i,true),true);
        }
        if(Items.Count==0) TextRenderer.DrawText(e.Graphics,"双击空白处新建 VS Code",Font,new Rectangle(HomeWidth+8,0,Math.Max(0,ViewWidth-8),Height),Color.LightGray,TextFormatFlags.VerticalCenter);
        if(dragging && dropIndex>=0) using(var pen=new Pen(Color.Cyan,3)) e.Graphics.DrawLine(pen,HomeWidth+TabLeft(dropIndex)-offset,3,HomeWidth+TabLeft(dropIndex)-offset,Height-3);
        e.Graphics.ResetClip();
        DrawToolbarIcon(e.Graphics,NewRect,"new",Color.White);
        if(picking) using(var brush=new SolidBrush(Color.FromArgb(21,112,175))) e.Graphics.FillRectangle(brush,Rectangle.Inflate(PickRect,-2,-4));
        DrawToolbarIcon(e.Graphics,PickRect,"pick",Color.FromArgb(115,220,255));
        DrawToolbarIcon(e.Graphics,AllRect,"all",Color.White);
        DrawToolbarIcon(e.Graphics,RemoveRect,"remove",Color.White);
        DrawToolbarIcon(e.Graphics,SaveRect,"save",Color.White);
        DrawToolbarIcon(e.Graphics,RestoreRect,"restore",Color.White);
        if(Pinned) using(var brush=new SolidBrush(Color.FromArgb(21,112,175))) e.Graphics.FillRectangle(brush,Rectangle.Inflate(PinRect,-2,-4));
        DrawToolbarIcon(e.Graphics,PinRect,"pin",Pinned?Color.FromArgb(130,230,255):Color.White);
        using(var brush=new SolidBrush(Color.FromArgb(58,65,80))) e.Graphics.FillRectangle(brush,Rectangle.Inflate(MenuRect,-4,-4));
        DrawToolbarIcon(e.Graphics,MenuRect,"menu",Color.White);
        if(ExternalDragOver) { using(var brush=new SolidBrush(Color.FromArgb(15,105,160))) e.Graphics.FillRectangle(brush,new Rectangle(0,0,Space,Height)); TextRenderer.DrawText(e.Graphics,"松开鼠标，接入此窗口",Font,new Rectangle(8,0,Space-8,Height),Color.White,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine); }
    }
    void DrawToolbarIcon(Graphics g,Rectangle r,string icon,Color color) {
        if(r.Contains(hoverPoint)) using(var brush=new SolidBrush(Color.FromArgb(65,78,96))) g.FillRectangle(brush,Rectangle.Inflate(r,-3,-4));
        int x=r.X+r.Width/2-8,y=r.Y+r.Height/2-8;
        using(var pen=new Pen(color,1.5f)) {
            if(icon=="home") { for(int row=0;row<2;row++) for(int col=0;col<2;col++) g.DrawRectangle(pen,x+col*9,y+row*9,6,6); }
            else if(icon=="new") { g.DrawLine(pen,x+8,y+2,x+8,y+14); g.DrawLine(pen,x+2,y+8,x+14,y+8); }
            else if(icon=="menu") { for(int row=0;row<3;row++) g.DrawLine(pen,x+1,y+3+row*5,x+15,y+3+row*5); }
            else if(icon=="remove") { g.DrawRectangle(pen,x+1,y+1,11,9); g.DrawRectangle(pen,x+5,y+5,11,9); g.DrawLine(pen,x+8,y+10,x+13,y+10); }
            else if(icon=="save") { g.DrawRectangle(pen,x+1,y+1,14,14); g.DrawRectangle(pen,x+4,y+1,7,5); g.DrawRectangle(pen,x+4,y+10,8,5); }
            else if(icon=="restore") { g.DrawArc(pen,x+2,y+2,12,12,210,290); g.DrawLines(pen,new[]{new Point(x+1,y+1),new Point(x+1,y+7),new Point(x+7,y+7)}); }
            else if(icon=="pick") { g.DrawEllipse(pen,x+3,y+3,10,10); g.DrawLine(pen,x+8,y,x+8,y+5); g.DrawLine(pen,x+8,y+11,x+8,y+16); g.DrawLine(pen,x,y+8,x+5,y+8); g.DrawLine(pen,x+11,y+8,x+16,y+8); }
            else if(icon=="all") { g.DrawRectangle(pen,x+1,y+1,11,9); g.DrawRectangle(pen,x+5,y+5,11,9); g.DrawLine(pen,x+8,y+10,x+13,y+10); g.DrawLine(pen,x+11,y+8,x+13,y+10); g.DrawLine(pen,x+11,y+12,x+13,y+10); }
            else if(icon=="pin") { g.DrawLines(pen,new[]{new Point(x+4,y+1),new Point(x+12,y+1),new Point(x+11,y+7),new Point(x+14,y+10),new Point(x+2,y+10),new Point(x+5,y+7),new Point(x+4,y+1)}); g.DrawLine(pen,x+8,y+10,x+8,y+16); }
        }
    }
    string ToolbarHint(Point point) {
        if(point.X<HomeWidth) return "Codex 总览";
        if(NewRect.Contains(point)) return "新建 VS Code 窗口";
        if(PickRect.Contains(point)) return "拖拽接入：按住并拖向目标窗口，松开接入；Esc 取消";
        if(AllRect.Contains(point)) return "全部接入 VS Code";
        if(RemoveRect.Contains(point)) return "移出全部 VS Code，恢复独立窗口（不关闭应用）";
        if(SaveRect.Contains(point)) return "将当前窗口及顺序存入组合列表";
        if(RestoreRect.Contains(point)) return "窗口组合列表：选择恢复或删除";
        if(PinRect.Contains(point)) return Pinned?"取消置顶":"窗口置顶";
        if(MenuRect.Contains(point)) return "菜单：更多操作与设置";
        return "";
    }
    void DrawAction(Graphics g,Rectangle r,bool close) {
        if(r.Contains(hoverPoint)) using(var brush=new SolidBrush(close?Color.FromArgb(195,57,67):Color.FromArgb(42,146,203))) g.FillRectangle(brush,r);
        int x=r.X+7,y=r.Y+(r.Height-13)/2;
        using(var pen=new Pen(Color.FromArgb(230,240,250),1.7f)) {
            if(close) { g.DrawLine(pen,x+2,y+2,x+12,y+12); g.DrawLine(pen,x+12,y+2,x+2,y+12); }
            else { g.DrawLines(pen,new[]{new Point(x+6,y+4),new Point(x,y+4),new Point(x,y+14),new Point(x+10,y+14),new Point(x+10,y+9)}); g.DrawLine(pen,x+5,y+9,x+13,y+1); g.DrawLine(pen,x+7,y+1,x+13,y+1); g.DrawLine(pen,x+13,y+1,x+13,y+7); }
        }
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if(e.Button==MouseButtons.Right) { int target=Hit(e.Location); if(target>=0) SelectForMenu(Items[target]); ShowMenu(e.Location); return; }
        if(e.Button!=MouseButtons.Left) return;
        if(e.X<HomeWidth) { if(DismissMenu!=null) DismissMenu(); ShowOverview(); return; }
        if(MenuRect.Contains(e.Location)) { ShowMenu(new Point(Width-240,Height)); return; }
        if(DismissMenu!=null) DismissMenu();
        if(PinRect.Contains(e.Location)) { TogglePin(); return; }
        if(RemoveRect.Contains(e.Location)) { RemoveCode(); return; }
        if(SaveRect.Contains(e.Location)) { SaveLayout(); return; }
        if(RestoreRect.Contains(e.Location)) { RestoreLayout(); return; }
        if(AllRect.Contains(e.Location)) { AddAll(); return; }
        if(PickRect.Contains(e.Location)) { StartPick(); return; }
        if(NewRect.Contains(e.Location)) { NewWindow(); return; }
        dragIndex=Hit(e.Location); dragStart=e.Location;
        if(dragIndex>=0) {
            var item=Items[dragIndex]; bool close=ActionRect(dragIndex,true).Contains(e.Location),detach=ActionRect(dragIndex,false).Contains(e.Location);
            if(close||detach) { dragIndex=-1; if(close) CloseTab(item); else DetachTab(item); return; }
            SelectTab(item); Capture=true;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e); hoverPoint=e.Location; Invalidate();
        int hover=Hit(e.Location); string hint=hover<0?ToolbarHint(e.Location):ActionRect(hover,true).Contains(e.Location)?"关闭此应用窗口（保留应用自身确认）":ActionRect(hover,false).Contains(e.Location)?"移出此标签，恢复为独立窗口（不关闭）":Native.Title(Items[hover].Handle)+(Items[hover].IsVSCode?"\nCodex："+CodexState.Description(Items[hover]):"\n"+Items[hover].ApplicationName); if(tip.GetToolTip(this)!=hint) tip.SetToolTip(this,hint);
        if(dragIndex<0 || e.Button!=MouseButtons.Left) return;
        if(Math.Abs(e.X-dragStart.X)>SystemInformation.DragSize.Width) dragging=true;
        if(!dragging) return;
        if(e.X<HomeWidth+24) offset-=12; else if(e.X>Space-24) offset+=12;
        Clamp(); int x=e.X-HomeWidth+offset; dropIndex=0;
        while(dropIndex<tabWidths.Length && x>TabLeft(dropIndex)+tabWidths[dropIndex]/2) dropIndex++;
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        if(picking) { CancelPick(); if(e.Button==MouseButtons.Left && PickWindowAt!=null) PickWindowAt(PointToScreen(e.Location)); return; }
        if(dragging && dropIndex>=0 && dragIndex>=0) { var item=Items[dragIndex]; Items.RemoveAt(dragIndex); int dest=dropIndex>dragIndex?dropIndex-1:dropIndex; Items.Insert(Math.Min(dest,Items.Count),item); }
        dragIndex=-1; dragging=false; dropIndex=-1; Capture=false; Reveal(); base.OnMouseUp(e);
        if(Refocus!=null) Refocus();
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if(picking&&!Capture) CancelPick(); }
    protected override bool ProcessCmdKey(ref Message msg,Keys key) { if(key==Keys.Escape && picking) { CancelPick(); return true; } return base.ProcessCmdKey(ref msg,key); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { if(e.Button==MouseButtons.Left && e.X>=HomeWidth && e.X<Space && Hit(e.Location)<0) NewWindow(); base.OnMouseDoubleClick(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { offset-=Math.Sign(e.Delta)*TabWidth; Clamp(); Invalidate(); base.OnMouseWheel(e); }
    protected override void OnMouseLeave(EventArgs e) { hoverPoint=new Point(-1,-1); Invalidate(); base.OnMouseLeave(e); }
    protected override void Dispose(bool disposing) { if(disposing) { tip.Dispose(); if(dpiFont!=null) dpiFont.Dispose(); } base.Dispose(disposing); }
    public void VerifyDrag() {
        var first=Items[0]; offset=0; LayoutTabs(); int end=HomeWidth+TabLeft(2)-10;
        OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,HomeWidth+50,20,0));
        OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,end,20,0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,end,20,0));
        if(Items[1]!=first || Selected!=first) throw new Exception("Drag ordering failed");
    }
    public void VerifyToolbar() {
        var shortFirst=AllocateWidths(new[]{130,240,220},600,250,128);
        if(shortFirst.Sum()!=600 || shortFirst[0]!=130 || shortFirst[1]<shortFirst[2]) throw new Exception("Short titles did not compress first");
        var preserveShort=AllocateWidths(new[]{145,375,145,375},650,375,140);
        if(preserveShort[0]!=145 || preserveShort[2]!=145 || preserveShort.Sum()!=650) throw new Exception("Short titles truncated while longer tabs could shrink");
        var overflow=AllocateWidths(new[]{130,240,220},200,250,128);
        if(overflow.Any(w=>w<128) || overflow.Sum()!=384) throw new Exception("Overflow tab targets became too small");
        LayoutTabs();
        for(int i=0;i<Items.Count;i++) { var tab=TabRect(i); if(tab.Left>=HomeWidth && tab.Right<=Space && Hit(new Point(tab.Left+5,Height/2))!=i) throw new Exception("Variable tab hit testing failed"); }
        var buttons=new[]{NewRect,PickRect,AllRect,RemoveRect,SaveRect,RestoreRect,PinRect,MenuRect};
        for(int i=0;i<buttons.Length;i++) {
            var r=buttons[i];
            if(r.Width<32 || r.Height<28 || r.Left<HomeWidth || r.Right>Width || String.IsNullOrEmpty(ToolbarHint(new Point(r.X+r.Width/2,r.Y+r.Height/2)))) throw new Exception("Toolbar icon clipped or missing hint");
            if(i>0 && buttons[i-1].IntersectsWith(r)) throw new Exception("Toolbar buttons overlap");
        }
    }
    public void ClickActionForTest(Hosted item,bool close) {
        Selected=item; Reveal(); var r=ActionRect(Items.IndexOf(item),close);
        OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,r.X+r.Width/2,r.Y+r.Height/2,0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,r.X+r.Width/2,r.Y+r.Height/2,0));
    }
}

sealed class MainForm : Form
{
    readonly List<Hosted> hosted=new List<Hosted>();
    readonly Panel surface=new Panel { Dock=DockStyle.Fill, BackColor=Color.FromArgb(30,30,30) };
    readonly TabStrip strip;
    readonly Overview overview=new Overview();
    Hosted pendingOverview;
    readonly Timer timer=new Timer { Interval=150 };
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    readonly Label empty=new Label { Dock=DockStyle.Fill, Text="菜单 → 选择应用窗口接入\n\n右上角可一键接入全部 VS Code 窗口\n\n双击标签栏空白处可新建 VS Code 窗口", ForeColor=Color.Silver, TextAlign=ContentAlignment.MiddleCenter };
    Keys left=Keys.Control|Keys.Alt|Keys.Left, right=Keys.Control|Keys.Alt|Keys.Right;
    bool keyboardEnabled;
    bool vscodeCompatible=true;
    readonly string windowModeSettings=PortableConfig.FilePath("window-mode.txt");
    readonly string settings=PortableConfig.FilePath("shortcuts.txt");
    HostApi.HookProc hookProc,mouseProc; IntPtr hook,mouseHook; bool dialogOpen, layoutPending;
    HostApi.WinEventProc moveProc; IntPtr moveHook,movingWindow;
    HostApi.WinEventProc foregroundProc; IntPtr foregroundHook;
    Native.RectangleEdges moveStart;
    WindowSnapshot moveSnapshot;
    bool focusQueued;
    Hosted focusRequest;
    DateTime focusDeadline, nextActivationFocus;
    bool restoringFocus;
    bool eatLeftUp,eatRightUp; int wheelRemainder;
    readonly CodexState codex=new CodexState();
    readonly CompletionNotifications notifications;
    readonly BindingStore bindings=new BindingStore();
    DateTime nextBindingRefresh;
    DateTime nextOpenWindowRefresh;
    bool closing;
    bool restoredForExit;
    bool testing;
    readonly Dictionary<Hosted,System.Threading.Tasks.Task<string>> restoringWindows=new Dictionary<Hosted,System.Threading.Tasks.Task<string>>();
    readonly List<Hosted> failedRestores=new List<Hosted>();
    readonly HashSet<int> swallowed=new HashSet<int>();
    HashSet<IntPtr> beforeLaunch; DateTime launchDeadline; string codePath;
    public MainForm(bool systemNotifications=true) {
        Text="Code Tabs"; Size=new Size(1280,850); MinimumSize=new Size(640,400); Font=new Font("Microsoft YaHei UI",9); StartPosition=FormStartPosition.CenterScreen;
        using(var iconStream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("CodeTabs.AppIcon")) Icon=new Icon(iconStream,new Size(32,32));
        if(systemNotifications) notifications=new CompletionNotifications(Icon,delegate {
            if(closing||IsDisposed) return;
            if(WindowState==FormWindowState.Minimized) WindowState=FormWindowState.Normal;
            Show(); Activate(); Select(null);
        });
        codex.TaskCompleted=overview.TaskCompleted=delegate(Hosted project,string token) {
            if(!closing&&notifications!=null) notifications.Completed(project,token,overview.DisplayName(project));
        };
        strip=new TabStrip(hosted) { Font=new Font("Microsoft YaHei UI",13,FontStyle.Regular,GraphicsUnit.Pixel) };
        MinimumSize=new Size(Math.Max(640,strip.MinimumStripWidth+20),400);
        strip.SelectTab=Select; strip.ShowMenu=delegate(Point point) { menu.Close(); menu.Show(strip,point); }; strip.DismissMenu=delegate { menu.Close(); }; strip.AddAll=AttachAll; strip.NewWindow=NewWindow; strip.CloseTab=CloseTab; strip.DetachTab=Detach;
        strip.PickWindowAt=PickAt; strip.Refocus=QueueFocus;
        strip.RemoveCode=RemoveAllCode; strip.SaveLayout=SaveWindowLayout; strip.RestoreLayout=RestoreWindowLayout;
        strip.PickingChanged=UpdateKeyboardHook;
        strip.SelectForMenu=delegate(Hosted h) { if(strip.Selected!=h) Select(h,false); };
        strip.ShowOverview=delegate { Select(null); };
        overview.OpenProject=OpenOverviewProject;
        strip.TogglePin=delegate { TopMost=!TopMost; strip.Pinned=TopMost; UpdateCompatible(true); strip.Invalidate(); };
        menu.AutoClose=true; menu.Font=new Font("Microsoft YaHei UI",9); menu.Padding=new Padding(4);
        menu.Items.Add("新建 VS Code 窗口",null,delegate { NewWindow(); });
        menu.Items.Add("接入全部 VS Code 窗口",null,delegate { AttachAll(); });
        menu.Items.Add("选择应用窗口接入…",null,delegate { Pick(); });
        menu.Items.Add("移出全部 VS Code（不关闭）",null,delegate { RemoveAllCode(); });
        menu.Items.Add("存入当前窗口组合…",null,delegate { SaveWindowLayout(); });
        menu.Items.Add("窗口组合列表（恢复 / 删除）…",null,delegate { RestoreWindowLayout(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("移出当前标签",null,delegate { if(strip.Selected!=null) Detach(strip.Selected); });
        menu.Items.Add("关闭当前应用窗口",null,delegate { if(strip.Selected!=null) CloseTab(strip.Selected); });
        menu.Items.Add("恢复全部独立窗口",null,delegate { RestoreAll(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("切换快捷键设置…",null,delegate { Settings(); });
        var imeModeItem=new ToolStripMenuItem("VS Code 输入法兼容模式（重新接入生效）");
        imeModeItem.ToolTipText="保留独立窗口及边框，避开嵌入窗口的输入队列关联。取消后恢复原嵌入方式。";
        imeModeItem.Click+=delegate {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(windowModeSettings));
                File.WriteAllText(windowModeSettings,vscodeCompatible?"embedded":"compatible");
                vscodeCompatible=!vscodeCompatible; imeModeItem.Checked=vscodeCompatible;
            } catch(Exception ex) { Error("无法保存窗口模式："+ex.Message); }
        };
        menu.Items.Add(imeModeItem);
        var completionItem=new ToolStripMenuItem("Codex 完成时系统提醒") { Checked=notifications!=null&&notifications.Enabled,Enabled=notifications!=null };
        completionItem.Click+=delegate {
            try { notifications.SetEnabled(!notifications.Enabled); completionItem.Checked=notifications.Enabled; }
            catch(Exception ex) { Error("无法保存通知设置："+ex.Message); }
        };
        menu.Items.Add(completionItem);
        var codexItem=menu.Items.Add("关联 Codex 会话…",null,delegate { BindCodex(); });
        menu.Opening+=delegate { codexItem.Enabled=strip.Selected!=null && strip.Selected.IsVSCode; };
        menu.Closed+=delegate { QueueFocus(); };
        Activated+=delegate { QueueActivationFocus(); BeginInvoke((Action)delegate { if(!closing) UpdateCompatible(true); }); };
        surface.Controls.Add(empty); surface.Controls.Add(overview); overview.BringToFront(); Controls.Add(surface); Controls.Add(strip);
        surface.SizeChanged+=delegate { Fit(); QueueFit(); };
        Resize+=delegate { QueueFit(); };
        LocationChanged+=delegate { if(IsHandleCreated&&!closing) UpdateCompatible(ManagedForeground(HostApi.GetForegroundWindow())); };
        Load+=delegate { LoadSettings(); vscodeCompatible=ReadCompatibleMode(windowModeSettings); imeModeItem.Checked=vscodeCompatible; UpdateKeyboardHook(); InstallMouseHook(); InstallMoveHook(); InstallForegroundHook(); };
        timer.Tick+=delegate { Tick(); }; timer.Start();
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {
            if(restoredForExit) return;
            e.Cancel=true;
            if(closing) return;
            closing=true; Enabled=false; RestoreAll(); FinishRestores();
        };
        FormClosed+=delegate { timer.Dispose(); codex.Dispose(); if(notifications!=null) notifications.Dispose(); if(moveHook!=IntPtr.Zero) HostApi.UnhookWinEvent(moveHook); if(foregroundHook!=IntPtr.Zero) HostApi.UnhookWinEvent(foregroundHook); if(hook!=IntPtr.Zero) HostApi.UnhookWindowsHookEx(hook); if(mouseHook!=IntPtr.Zero) HostApi.UnhookWindowsHookEx(mouseHook); menu.Dispose(); };
    }
    void Error(string message) { MessageBox.Show(this,message,"Code Tabs",MessageBoxButtons.OK,MessageBoxIcon.Information); }
    static bool IsCodeProcess(string name) { return name.Equals("Code",StringComparison.OrdinalIgnoreCase)||name.Equals("Code - Insiders",StringComparison.OrdinalIgnoreCase); }
    List<Hosted> Discover(bool vscodeOnly=true) {
        var result=new List<Hosted>();
        int ownPid=Process.GetCurrentProcess().Id;
        Native.EnumWindows(delegate(IntPtr h,IntPtr p) {
            if(!Native.IsWindowVisible(h) || hosted.Exists(x=>x.Handle==h)) return true;
            string title=Native.Title(h);
            if(String.IsNullOrWhiteSpace(title)||Native.GetParent(h)!=IntPtr.Zero) return true;
            if((Native.GetStyle(h,-20).ToInt64()&0x80L)!=0) return true;
            var className=new System.Text.StringBuilder(256); HostApi.GetClassName(h,className,256);
            if(className.ToString()=="Progman"||className.ToString()=="WorkerW"||className.ToString().StartsWith("Shell_")) return true;
            int cloaked; if(HostApi.DwmGetWindowAttribute(h,14,out cloaked,4)==0 && cloaked!=0) return true;
            uint pid; Native.GetWindowThreadProcessId(h,out pid);
            if(pid==ownPid) return true;
            try { using(var process=Process.GetProcessById((int)pid)) {
                string app=process.ProcessName;
                if(app.StartsWith("CodeTabs",StringComparison.OrdinalIgnoreCase)&&title!="Code Tabs test fixture") return true;
                bool isCode=IsCodeProcess(app);
                if(vscodeOnly && (!isCode || !title.Contains("Visual Studio Code"))) return true;
                result.Add(new Hosted { Handle=h,ApplicationName=app,IsVSCode=isCode,Compatible=app.Equals("WeChat",StringComparison.OrdinalIgnoreCase)||app.Equals("Weixin",StringComparison.OrdinalIgnoreCase) });
                if(isCode) try { codePath=process.MainModule.FileName; } catch(Win32Exception) {}
            } } catch(ArgumentException) {} catch(Win32Exception) {}
            return true;
        },IntPtr.Zero); return result;
    }
    void AttachAll() { var windows=Discover(); if(windows.Count==0) { Error("没有找到新的 VS Code 窗口。"); return; } foreach(var h in windows) Attach(h); }
    void RemoveAllCode() {
        foreach(var h in hosted.Where(x=>x.IsVSCode).ToArray()) Detach(h);
    }
    void SaveWindowLayout() { ManageWindowLayouts(true); }
    void RestoreWindowLayout() { ManageWindowLayouts(false); }
    string layoutTestDirectory;
    Action<Form> layoutDialogTest;
    void ManageWindowLayouts(bool saving) {
        NamedWindowLayout restore=null;
        dialogOpen=true;
        try {
            string path=PortableConfig.FilePath("window-layouts.json"),legacy=PortableConfig.FilePath("window-layout.json");
            if(layoutTestDirectory!=null) { path=Path.Combine(layoutTestDirectory,"window-layouts.json"); legacy=Path.Combine(layoutTestDirectory,"window-layout.json"); }
            var library=WindowLayoutLibrary.Read(path,legacy);
            using(var d=new Form { Text="窗口组合",ClientSize=new Size(760,440),MinimumSize=new Size(680,400),StartPosition=FormStartPosition.CenterParent,Font=Font,AutoScaleMode=AutoScaleMode.Dpi }) {
                var list=new ListBox { Name="layouts",Dock=DockStyle.Fill,HorizontalScrollbar=true,IntegralHeight=false };
                var details=new ListBox { Name="windows",Dock=DockStyle.Fill,HorizontalScrollbar=true,IntegralHeight=false };
                var split=new SplitContainer { Dock=DockStyle.Fill,Width=760,SplitterDistance=260 };
                split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(details);
                var info=new Label { Dock=DockStyle.Top,Height=Font.Height*2+20,Text="选择已保存的组合，查看窗口顺序后恢复。\n恢复需要应用窗口已打开；其他已有标签将保留在末尾。",Padding=new Padding(8) };
                var name=new TextBox { Name="layoutName",Width=230,MaxLength=80,Text="组合 "+DateTime.Now.ToString("MM-dd HH:mm:ss") };
                var save=new Button { Text="存入当前组合",AutoSize=true };
                var use=new Button { Text="恢复所选组合",AutoSize=true,Enabled=false };
                var remove=new Button { Text="删除所选",AutoSize=true,Enabled=false };
                var close=new Button { Text="关闭",AutoSize=true,DialogResult=DialogResult.Cancel };
                var status=new Label { Dock=DockStyle.Bottom,Height=Font.Height+10,Padding=new Padding(8,3,0,0) };
                var saveBar=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=Font.Height+24,Padding=new Padding(6),WrapContents=false };
                saveBar.Controls.Add(new Label { Text="组合名称",AutoSize=true,Margin=new Padding(3,6,3,3) }); saveBar.Controls.Add(name); saveBar.Controls.Add(save);
                var buttons=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=Font.Height+24,Padding=new Padding(6),WrapContents=false };
                buttons.Controls.Add(use); buttons.Controls.Add(remove); buttons.Controls.Add(close);
                Action<string> reload=delegate(string id) {
                    list.Items.Clear(); foreach(var entry in library.Items) list.Items.Add(entry);
                    list.SelectedIndex=library.Items.FindIndex(x=>x.Id==id);
                    if(list.SelectedIndex<0 && list.Items.Count>0) list.SelectedIndex=0;
                    status.Text=library.Items.Count==0?"尚无组合。输入名称后存入当前窗口。":"已保存 "+library.Items.Count+" 个组合";
                };
                list.SelectedIndexChanged+=delegate {
                    details.Items.Clear(); var entry=list.SelectedItem as NamedWindowLayout; use.Enabled=remove.Enabled=entry!=null;
                    if(entry!=null) for(int i=0;i<entry.Layout.Windows.Count;i++) details.Items.Add((i+1)+". "+entry.Layout.Windows[i].Title+"  ["+entry.Layout.Windows[i].App+"]");
                };
                save.Click+=delegate {
                    try {
                        var windows=hosted.Where(x=>Native.IsWindow(x.Handle)).ToList();
                        if(windows.Count==0) { status.Text="当前没有已组合的窗口。"; return; }
                        var updated=WindowLayoutLibrary.Read(path,legacy);
                        var entry=updated.Add(name.Text,new SavedWindowLayout { Windows=windows.Select(SavedWindow.Capture).ToList(),Selected=windows.IndexOf(strip.Selected) });
                        updated.Save(path); library=updated; reload(entry.Id); status.Text="已保存："+entry.Name;
                    } catch(Exception ex) { MessageBox.Show(d,ex.Message,"保存失败",MessageBoxButtons.OK,MessageBoxIcon.Information); }
                };
                remove.Click+=delegate {
                    var entry=list.SelectedItem as NamedWindowLayout; if(entry==null) return;
                    try {
                        var updated=WindowLayoutLibrary.Read(path,legacy); updated.Items.RemoveAll(x=>x.Id==entry.Id); updated.Save(path);
                        library=updated; reload(null); status.Text="已删除组合："+entry.Name+"（应用窗口未关闭）";
                    } catch(Exception ex) { MessageBox.Show(d,ex.Message,"删除失败",MessageBoxButtons.OK,MessageBoxIcon.Information); }
                };
                use.Click+=delegate { restore=list.SelectedItem as NamedWindowLayout; if(restore!=null) d.Close(); };
                list.DoubleClick+=delegate { if(use.Enabled) use.PerformClick(); };
                d.Controls.Add(split); d.Controls.Add(info); d.Controls.Add(status); d.Controls.Add(saveBar); d.Controls.Add(buttons); d.CancelButton=close;
                reload(null); d.Shown+=delegate { if(saving) { name.Focus(); name.SelectAll(); } else list.Focus(); if(layoutDialogTest!=null) d.BeginInvoke((Action)delegate { layoutDialogTest(d); }); };
                d.ShowDialog(this);
            }
        } catch(Exception ex) { Error("无法打开窗口组合："+ex.Message); }
        finally { dialogOpen=false; }
        if(restore!=null) RestoreWindowLayout(restore.Layout);
    }
    static IEnumerable<Control> AllControls(Control root) {
        foreach(Control child in root.Controls) { yield return child; foreach(var nested in AllControls(child)) yield return nested; }
    }
    public void VerifyWindowLayoutUi(string root) {
        layoutTestDirectory=Path.Combine(root,"library-ui-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(layoutTestDirectory);
        testing=true; Exception failure=null;
        using(var a=new Form { Text="微信" }) using(var b=new Form { Text="A long project title - Visual Studio Code" }) {
            a.Show(); b.Show(); var ha=new Hosted { Handle=a.Handle,ApplicationName="Chat" }; var hb=new Hosted { Handle=b.Handle,ApplicationName="Code",IsVSCode=true };
            Attach(ha); Attach(hb);
            layoutDialogTest=delegate(Form d) {
                try {
                    var controls=AllControls(d).ToList(); var name=(TextBox)controls.First(x=>x.Name=="layoutName");
                    var list=(ListBox)controls.First(x=>x.Name=="layouts"); var details=(ListBox)controls.First(x=>x.Name=="windows");
                    name.Text="日常工作"; controls.OfType<Button>().First(x=>x.Text=="存入当前组合").PerformClick();
                    name.Text="开发与沟通"; controls.OfType<Button>().First(x=>x.Text=="存入当前组合").PerformClick();
                    if(list.Items.Count!=2 || details.Items.Count!=2 || !details.Items[0].ToString().Contains("微信")) throw new Exception("Library dialog save or preview failed");
                    using(var bitmap=new Bitmap(d.Width,d.Height)) { d.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"window-layout-library.png")); }
                    hosted.Reverse(); Select(ha);
                    controls.OfType<Button>().First(x=>x.Text=="恢复所选组合").PerformClick();
                } catch(Exception ex) { failure=ex; d.Close(); }
            };
            ManageWindowLayouts(true); if(failure!=null) throw failure;
            if(hosted.Count!=2 || hosted[0]!=ha || hosted[1]!=hb || strip.Selected!=hb) throw new Exception("Library restore did not preserve order and selection");
            layoutDialogTest=delegate(Form d) {
                try {
                    var controls=AllControls(d).ToList(); var list=(ListBox)controls.First(x=>x.Name=="layouts");
                    controls.OfType<Button>().First(x=>x.Text=="删除所选").PerformClick();
                    if(list.Items.Count!=1 || hosted.Count!=2) throw new Exception("Library delete affected live windows");
                } catch(Exception ex) { failure=ex; } finally { d.Close(); }
            };
            ManageWindowLayouts(false); if(failure!=null) throw failure;
            foreach(var screen in Screen.AllScreens) {
                Location=new Point(screen.WorkingArea.X+40,screen.WorkingArea.Y+40); Application.DoEvents(); strip.RefreshDpi();
                int expected=(int)WindowRestore.GetDpiForWindow(strip.Handle);
                if(Math.Abs(strip.Font.Size-13*expected/96f)>0.1) throw new Exception("Toolbar font did not follow monitor DPI");
                strip.VerifyToolbar();
                using(var bitmap=new Bitmap(strip.Width,strip.Height)) { strip.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"toolbar-"+expected+"dpi.png")); }
            }
            using(var crowded=new TabStrip(new List<Hosted>{ha,hb,ha,hb,ha,hb})) {
                crowded.Width=1300; crowded.CreateControl(); crowded.RefreshDpi(); crowded.VerifyToolbar();
                using(var bitmap=new Bitmap(crowded.Width,crowded.Height)) { crowded.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"toolbar-crowded.png")); }
            }
            DetachForTest(ha); DetachForTest(hb);
        }
        layoutDialogTest=null; layoutTestDirectory=null;
        Console.Error.WriteLine("Library UI: named save, preview, selected restore, order, deletion and per-monitor DPI passed");
    }
    void RestoreWindowLayout(SavedWindowLayout layout) {
        try {
            FinishRestores();
            if(restoringWindows.Count>0) { Error("窗口正在移出，请稍后再恢复组合。"); return; }
            var candidates=hosted.Concat(Discover(false)).ToList();
            var records=candidates.Select(SavedWindow.Capture).ToList();
            var used=new HashSet<int>(); var ordered=new List<Hosted>(); var missing=new List<string>(); Hosted selected=null;
            for(int i=0;i<layout.Windows.Count;i++) {
                int match=SavedWindowLayout.Match(layout.Windows[i],records,used,layout.Windows);
                if(match<0) { missing.Add(layout.Windows[i].Title+"（"+layout.Windows[i].App+"）"); continue; }
                used.Add(match); var h=candidates[match];
                if(!hosted.Contains(h)) Attach(h);
                if(!hosted.Contains(h)) { missing.Add(layout.Windows[i].Title+"（接入失败）"); continue; }
                ordered.Add(h); if(i==layout.Selected) selected=h;
            }
            var remaining=hosted.Where(x=>!ordered.Contains(x)).ToList();
            hosted.Clear(); hosted.AddRange(ordered); hosted.AddRange(remaining);
            Select(layout.Selected<0?null:selected??ordered.FirstOrDefault()??hosted.FirstOrDefault()); strip.Reveal();
            if(missing.Count>0) Error("已恢复 "+ordered.Count+" 个窗口。以下窗口未打开、标题已改变或存在多个匹配，请打开或手动接入后重试：\n\n"+String.Join("\n",missing));
        } catch(Exception ex) { Error("恢复窗口组合失败："+ex.Message); }
    }
    void Pick() {
        dialogOpen=true;
        try { using(var d=new Form { Text="选择应用窗口接入", Size=new Size(850,460), StartPosition=FormStartPosition.CenterParent,Font=Font }) {
            var list=new ListBox { Dock=DockStyle.Fill, SelectionMode=SelectionMode.MultiExtended, HorizontalScrollbar=true };
            var info=new Label { Dock=DockStyle.Top,Height=50,Text="选择要接入的应用窗口，可按 Ctrl / Shift 多选。\n右上角“全部接入 VS Code”仍只接入 VS Code。" };
            var bar=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=40 };
            var ok=new Button { Text="接入所选窗口",AutoSize=true,DialogResult=DialogResult.OK,Enabled=false };
            var refresh=new Button { Text="刷新窗口列表",AutoSize=true };
            Action reload=delegate { list.Items.Clear(); foreach(var h in Discover(false)) list.Items.Add(h); ok.Enabled=list.SelectedItems.Count>0; };
            list.SelectedIndexChanged+=delegate { ok.Enabled=list.SelectedItems.Count>0; }; refresh.Click+=delegate { reload(); }; reload();
            bar.Controls.Add(ok); bar.Controls.Add(refresh); d.Controls.Add(list); d.Controls.Add(info); d.Controls.Add(bar); d.AcceptButton=ok;
            if(d.ShowDialog(this)==DialogResult.OK) foreach(Hosted h in list.SelectedItems) Attach(h);
        } } finally { dialogOpen=false; }
    }
    void NewWindow() {
        if(beforeLaunch!=null) return;
        var windows=Discover();
        if(String.IsNullOrEmpty(codePath)) {
            string[] candidates={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Microsoft VS Code","Code.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Microsoft VS Code","Code.exe")};
            foreach(var p in candidates) if(File.Exists(p)) { codePath=p; break; }
        }
        if(String.IsNullOrEmpty(codePath)) { using(var picker=new OpenFileDialog { Title="选择 Code.exe", Filter="VS Code|Code.exe;Code - Insiders.exe" }) { if(picker.ShowDialog(this)!=DialogResult.OK) return; codePath=picker.FileName; } }
        beforeLaunch=new HashSet<IntPtr>(); foreach(var h in windows) beforeLaunch.Add(h.Handle);
        try { string argument=LaunchArguments(pendingOverview); Process.Start(new ProcessStartInfo(codePath,argument) { UseShellExecute=false }); launchDeadline=DateTime.UtcNow.AddSeconds(pendingOverview!=null&&Overview.IsRemote(pendingOverview)?60:25); }
        catch(Exception ex) { beforeLaunch=null; Error("新建窗口失败："+ex.Message); }
    }
    static string QuoteArgument(string value) {
        var text=new System.Text.StringBuilder("\""); int slashes=0;
        foreach(char c in value) {
            if(c=='\\') { slashes++; continue; }
            if(c=='\"') { text.Append('\\',slashes*2+1); text.Append(c); }
            else { text.Append('\\',slashes); text.Append(c); }
            slashes=0;
        }
        text.Append('\\',slashes*2); text.Append('"'); return text.ToString();
    }
    static string LaunchArguments(Hosted project) {
        if(project==null) return "--new-window";
        if(Overview.IsRemote(project)) {
            string alias=project.RemoteAlias;
            if(project.RemoteKind!="ssh"||String.IsNullOrWhiteSpace(alias)) throw new InvalidOperationException("未找到可用的 SSH 别名，请先在 Codex 配置此连接，或通过更改目录设置本地映射。");
            string path=project.ProjectPath.Replace('\\','/');
            string uri="vscode-remote://ssh-remote+"+Uri.EscapeDataString(alias)+"/"+String.Join("/",path.TrimStart('/').Split('/').Select(Uri.EscapeDataString));
            return "--new-window --folder-uri "+QuoteArgument(uri);
        }
        return "--new-window "+QuoteArgument(project.ProjectPath);
    }
    void Attach(Hosted h,WindowSnapshot beforeMove=null) {
        if(!Native.IsWindow(h.Handle) || hosted.Exists(x=>x.Handle==h.Handle)||restoringWindows.Keys.Any(x=>x.Handle==h.Handle)) return;
        // Independent top-level windows do not implicitly attach input queues
        // through SetParent/ownership. Keep VS Code on this path by default.
        if(h.IsVSCode) h.Compatible=vscodeCompatible;
        h.Parent=Native.GetParent(h.Handle); h.Style=Native.GetStyle(h.Handle,-16); h.ExStyle=Native.GetStyle(h.Handle,-20);
        // A title-bar drop has already moved the window. Restore the snapshot
        // from move-start, not the location where the user released it.
        try { h.Snapshot=beforeMove??WindowSnapshot.Capture(h.Handle); }
        catch(Exception ex) { Error("接入失败："+ex.Message); return; }
        h.Placement=h.Snapshot.Placement;
        failedRestores.RemoveAll(x=>x.Handle==h.Handle);
        hosted.Add(h);
        try {
            RestoreBinding(h);
            if(h.Compatible) {
                // Shell requires hiding BEFORE changing taskbar eligibility.
                // Only do this at attach/detach, never during layout or menus.
                // TOOLWINDOW also removes the duplicate Alt+Tab entry without
                // introducing an owner/parent and coupling input queues.
                Native.ShowWindow(h.Handle,0);
                Native.Style(h.Handle,-20,new IntPtr((h.ExStyle.ToInt64() & ~0x00040000L) | 0x00000080L));
                Native.SetWindowPos(h.Handle,IntPtr.Zero,0,0,0,0,0x4037);
                Native.ShowWindowAsync(h.Handle,9); Select(h); return;
            }
            Native.ShowWindow(h.Handle,9);
            Native.Style(h.Handle,-16,new IntPtr((h.Style.ToInt64() & ~0x80CF0000L) | 0x40000000L));
            Native.Style(h.Handle,-20,new IntPtr(h.ExStyle.ToInt64() & ~0x00040301L));
            Native.Parent(h.Handle,surface.Handle); Select(h); QueueFit();
        } catch(Exception ex) { Detach(h); Error("接入失败："+ex.Message); }
    }
    void Select(Hosted h) { Select(h,true); }
    void Select(Hosted h,bool activate) {
        RememberFocus(strip.Selected);
        strip.Selected=h; empty.Visible=hosted.Count==0;
        strip.OverviewSelected=h==null; overview.Visible=h==null; if(h==null) overview.BringToFront(); else overview.MarkViewed(h);
        foreach(var item in hosted) if(!item.Compatible) Native.ShowWindow(item.Handle,item==h?5:0);
        Fit(); UpdateCompatible(true); UpdateCaption(); strip.Reveal();
        if(activate) QueueFocus();
        if(activate && h!=null && h.Compatible && !closing) HostApi.SetForegroundWindow(h.Handle);
    }
    static bool InWindow(IntPtr parent,IntPtr child) { return child!=IntPtr.Zero && (parent==child||Native.IsChild(parent,child)); }
    bool ManagedForeground(IntPtr window) { return window==Handle||HostApi.GetAncestor(window,2)==Handle||(strip.Selected!=null&&strip.Selected.Compatible&&InWindow(strip.Selected.Handle,window)); }
    void RememberFocus(Hosted h) {
        if(h==null || h.Compatible || !Native.IsWindow(h.Handle)) return;
        uint pid; var info=HostApi.GuiInfo();
        if(HostApi.GetGUIThreadInfo(Native.GetWindowThreadProcessId(h.Handle,out pid),ref info)&&InWindow(h.Handle,info.focus) && (info.focus!=h.Handle||!Native.IsWindow(h.LastFocus))) h.LastFocus=info.focus;
    }
    void QueueFocus() {
        if(closing||restoringFocus||!IsHandleCreated||IsDisposed) return;
        focusRequest=strip.Selected; focusDeadline=DateTime.UtcNow.AddMilliseconds(750);
        if(focusQueued) return;
        focusQueued=true; BeginInvoke((Action)delegate { focusQueued=false; RecoverFocus(); });
    }
    void QueueActivationFocus() {
        if(DateTime.UtcNow<nextActivationFocus) return;
        nextActivationFocus=DateTime.UtcNow.AddSeconds(1);
        QueueFocus();
    }
    void RecoverFocus() {
        // Poll only a short-lived request from a tab/menu/activation action.
        // An IME temporarily losing focus must not start a recovery loop.
        if(focusRequest==null) return;
        var info=HostApi.GuiInfo();
        if(!HostApi.GetGUIThreadInfo(0,ref info)) { focusRequest=null; return; }
        bool gesture=false;
        foreach(int key in new[]{1,2,0x10,0x11,0x12,0x5B,0x5C}) if(HostApi.GetAsyncKeyState(key)<0) { gesture=true; break; }
        RecoverFocus(HostApi.GetForegroundWindow(),info,gesture);
    }
    void RecoverFocus(IntPtr foreground,HostApi.GuiThreadInfo info,bool gesture) {
        var h=strip.Selected;
        if(focusRequest==null) return;
        if(closing||h!=focusRequest||DateTime.UtcNow>=focusDeadline||!Enabled||WindowState==FormWindowState.Minimized||!Native.IsWindow(h.Handle)) { focusRequest=null; return; }
        if(foreground!=Handle&&HostApi.GetAncestor(foreground,2)!=Handle) { focusRequest=null; return; }
        // The application's root is also valid focus (notably in Electron).
        // Preserve its remembered child without forcing that child back.
        if(InWindow(h.Handle,info.focus)) { RememberFocus(h); focusRequest=null; return; }
        // Only reclaim our own chrome. Null/transient focus, IME toolbars,
        // candidate windows and other applications are never ours to reclaim.
        if(info.focus!=Handle&&info.focus!=surface.Handle&&info.focus!=strip.Handle&&info.focus!=empty.Handle) { focusRequest=null; return; }
        if(dialogOpen||menu.Visible||strip.IsPicking) return;
        if(info.menuOwner!=IntPtr.Zero||info.moveSize!=IntPtr.Zero||info.capture!=IntPtr.Zero||(info.flags&0x1C)!=0) return;
        if(gesture) return;
        // Consume before SetFocus, which can synchronously re-enter activation.
        focusRequest=null; nextActivationFocus=DateTime.UtcNow.AddSeconds(1);
        restoringFocus=true;
        try {
            if(h.Compatible) { UpdateCompatible(true); HostApi.SetForegroundWindow(h.Handle); }
            else RestoreEditorFocus(h);
        } finally { restoringFocus=false; }
    }
    void RestoreEditorFocus(Hosted h) {
        IntPtr target=Native.IsWindow(h.LastFocus)&&InWindow(h.Handle,h.LastFocus)?h.LastFocus:h.Handle;
        Native.FocusWindow(target);
    }
    void Step(int direction) { if(hosted.Count<2) return; int i=hosted.IndexOf(strip.Selected); Select(hosted[(i+direction+hosted.Count)%hosted.Count]); }
    void CloseTab(Hosted h) { Select(h); Native.ShowWindowAsync(h.Handle,5); HostApi.PostMessage(h.Handle,0x0010,IntPtr.Zero,IntPtr.Zero); }
    void QueueFit() { if(layoutPending || !IsHandleCreated || IsDisposed) return; layoutPending=true; BeginInvoke((Action)delegate { layoutPending=false; Fit(); }); }
    void Fit() {
        if(closing) return;
        var h=strip.Selected; if(h==null || !Native.IsWindow(h.Handle) || WindowState==FormWindowState.Minimized) return;
        if(h.Compatible) { UpdateCompatible(false); return; }
        if(HostApi.IsZoomed(h.Handle)||HostApi.IsIconic(h.Handle)) Native.ShowWindow(h.Handle,9);
        // Electron can reapply its frame style. Remove independent sizing and window controls.
        long old=Native.GetStyle(h.Handle,-16).ToInt64(), desired=(old & ~0x80CF0000L)|0x40000000L;
        if(old!=desired) Native.Style(h.Handle,-16,new IntPtr(desired));
        Native.RectangleEdges r; HostApi.GetWindowRect(h.Handle,out r); Point origin=surface.PointToScreen(Point.Empty);
        if(old!=desired || r.left!=origin.X || r.top!=origin.Y || r.right-r.left!=surface.ClientSize.Width || r.bottom-r.top!=surface.ClientSize.Height)
            Native.SetWindowPos(h.Handle,IntPtr.Zero,0,0,surface.ClientSize.Width,surface.ClientSize.Height,0x0434);
    }
    void Tick() {
        strip.RefreshDpi();
        FinishRestores();
        if(closing) return;
        if(DateTime.UtcNow>=nextOpenWindowRefresh) { nextOpenWindowRefresh=DateTime.UtcNow.AddSeconds(2); overview.SetOpenWindows(hosted,Discover()); }
        foreach(var h in hosted.ToArray()) if(!Native.IsWindow(h.Handle)) { hosted.Remove(h); if(strip.Selected==h) Select(hosted.Count>0?hosted[0]:null); }
        Fit(); UpdateCompatible(false); UpdateCaption(); RefreshBindings(); RecoverFocus(); codex.Refresh(hosted); overview.TickOverview(); strip.Invalidate();
        if(movingWindow!=IntPtr.Zero) { Point pointer; HostApi.GetCursorPos(out pointer); strip.ExternalDragOver=DropZone(pointer); }
        if(beforeLaunch!=null) {
            foreach(var h in Discover()) if(!beforeLaunch.Contains(h.Handle) && (pendingOverview==null||MatchesProject(h,pendingOverview))) { beforeLaunch=null; Attach(h); if(pendingOverview!=null) { if(!String.IsNullOrEmpty(pendingOverview.CodexPath)&&SaveBinding(h,pendingOverview.CodexPath)) overview.MarkViewed(pendingOverview); OpenCodexConversation(pendingOverview,h); pendingOverview=null; } break; }
            if(beforeLaunch!=null && DateTime.UtcNow>launchDeadline) { beforeLaunch=null; pendingOverview=null; Error("新窗口尚未就绪，可稍后点击“全部接入 VS Code”。"); }
        }
    }
    void Detach(Hosted h) {
        if(h==null||restoringWindows.ContainsKey(h)) return;
        if(Native.IsWindow(h.Handle)) try {
            if(h.Compatible) {
                Native.ShowWindow(h.Handle,0);
                Native.Style(h.Handle,-20,h.ExStyle);
                Native.SetWindowPos(h.Handle,new IntPtr((h.ExStyle.ToInt64()&8)!=0?-1:-2),0,0,0,0,0x4033);
            } else {
            Native.Parent(h.Handle,h.Parent); Native.Style(h.Handle,-16,h.Style); Native.Style(h.Handle,-20,h.ExStyle);
            Native.SetWindowPos(h.Handle,IntPtr.Zero,0,0,0,0,0x0037);
            }
        } catch(Exception ex) { Error("恢复失败，请保持程序开启："+ex.Message); return; }
        // Stop all layout synchronization before posting restore operations.
        hosted.Remove(h); Select(strip.Selected==h?(hosted.Count>0?hosted[0]:null):strip.Selected);
        if(Native.IsWindow(h.Handle)) restoringWindows.Add(h,WindowRestore.RestoreAsync(h.Handle,h.Snapshot));
    }
    void RestoreAll() {
        foreach(var h in failedRestores.ToArray()) {
            failedRestores.Remove(h);
            if(Native.IsWindow(h.Handle)) restoringWindows[h]=WindowRestore.RestoreAsync(h.Handle,h.Snapshot);
        }
        foreach(var h in hosted.ToArray()) Detach(h);
    }
    void FinishRestores() {
        var errors=new List<string>();
        foreach(var pair in restoringWindows.ToArray()) if(pair.Value.IsCompleted) {
            restoringWindows.Remove(pair.Key);
            string error=pair.Value.IsFaulted?pair.Value.Exception.GetBaseException().Message:pair.Value.Result;
            if(error!=null) { failedRestores.Add(pair.Key); errors.Add(error); }
        }
        if(errors.Count>0) {
            closing=false; Enabled=true;
            if(testing) throw new Exception(String.Join("\n",errors));
            Error("窗口位置恢复尚未完成，可在菜单选择“恢复全部独立窗口”重试。\n"+String.Join("\n",errors));
        }
        if(closing&&restoringWindows.Count==0&&!restoredForExit) {
            if(hosted.Count>0||failedRestores.Count>0) { closing=false; Enabled=true; return; }
            restoredForExit=true; BeginInvoke((Action)Close);
        }
    }
    void UpdateCaption() { string title=strip.Selected==null?"Code Tabs":Native.Title(strip.Selected.Handle); if(String.IsNullOrWhiteSpace(title)) title="Code Tabs"; if(Text!=title) Text=title; }
    static bool MatchesProject(Hosted window,Hosted project) {
        if(!window.IsVSCode) return false;
        if(Overview.IsRemote(project)) {
            string title=Native.Title(window.Handle);
            return !String.IsNullOrEmpty(project.RemoteAlias)&&title.IndexOf("SSH: "+project.RemoteAlias,StringComparison.OrdinalIgnoreCase)>=0&&title.IndexOf(Path.GetFileName(project.ProjectPath.TrimEnd('/','\\')),StringComparison.OrdinalIgnoreCase)>=0;
        }
        bool moved=project.OriginalProjectPath!=null&&!String.Equals(project.OriginalProjectPath,project.ProjectPath,StringComparison.OrdinalIgnoreCase);
        string expected=moved?BindingStore.Key(project.ApplicationName,Path.GetFileName(project.ProjectPath.TrimEnd('\\','/'))):project.BindingKey??BindingStore.Key(project.ApplicationName,String.IsNullOrEmpty(project.ProjectPath)?Overview.ProjectName(project):Path.GetFileName(project.ProjectPath.TrimEnd('\\','/')));
        string actual=BindingStore.Key(window.ApplicationName,Native.Title(window.Handle));
        return project.BindingKey!=null&&!moved?actual==expected:actual.Substring(actual.IndexOf('|')+1)==expected.Substring(expected.IndexOf('|')+1);
    }
    void OpenOverviewProject(Hosted project) {
        bool moved=project.OriginalProjectPath!=null&&!String.Equals(project.OriginalProjectPath,project.ProjectPath,StringComparison.OrdinalIgnoreCase);
        var existing=hosted.Find(h=>h.IsVSCode&&!String.IsNullOrEmpty(project.CodexPath)&&(!moved||MatchesProject(h,project))&&String.Equals(h.CodexPath,project.CodexPath,StringComparison.OrdinalIgnoreCase))??hosted.Find(h=>MatchesProject(h,project));
        if(existing!=null) { if(!String.IsNullOrEmpty(project.CodexPath)&&existing.CodexPath!=project.CodexPath && !SaveBinding(existing,project.CodexPath)) return; Select(existing); overview.MarkViewed(project); OpenCodexConversation(project,existing); return; }
        var windows=Discover().FindAll(h=>MatchesProject(h,project));
        if(windows.Count==1) { Attach(windows[0]); if(!String.IsNullOrEmpty(project.CodexPath)&&SaveBinding(windows[0],project.CodexPath)) overview.MarkViewed(project); OpenCodexConversation(project,windows[0]); return; }
        if(windows.Count>1) { Error("有多个同名 VS Code 窗口，请从窗口列表选择接入后再打开项目。"); return; }
        if(beforeLaunch!=null) { Error("正在等待另一个 VS Code 窗口打开，请稍后重试。"); return; }
        if(String.IsNullOrWhiteSpace(project.ProjectPath)||(!Overview.IsRemote(project)&&!Directory.Exists(project.ProjectPath))) { Error("无法读取项目目录，请使用卡片菜单更改目录后打开。"); return; }
        pendingOverview=project; NewWindow(); if(beforeLaunch==null) pendingOverview=null;
    }
    int conversationRequest;
    async void OpenCodexConversation(Hosted project,Hosted window) {
        int request=++conversationRequest;
        if(!hosted.Contains(window)) return;
        try {
            string id=ConversationNavigation.ThreadId(project); if(id==null) return;
            string title=Native.Title(window.Handle),executable;
            uint pid; Native.GetWindowThreadProcessId(window.Handle,out pid);
            using(var process=Process.GetProcessById((int)pid)) executable=process.MainModule.FileName;
            string status=await System.Threading.Tasks.Task.Run(()=>ConversationNavigation.RunCli(executable,"--status"));
            if(IsDisposed||closing||request!=conversationRequest||strip.Selected!=window||!hosted.Contains(window)||!Native.IsWindow(window.Handle)) return;
            if(Native.Title(window.Handle)!=title) throw new InvalidOperationException("项目窗口标题正在变化，请再次点击卡片重试。");
            ConversationNavigation.VerifyInstance(status,pid);
            int target=ConversationNavigation.WindowId(status,title);
            string uri=ConversationNavigation.UriFor(id,target,Path.GetFileNameWithoutExtension(executable).IndexOf("insiders",StringComparison.OrdinalIgnoreCase)>=0);
            await System.Threading.Tasks.Task.Run(()=>ConversationNavigation.RunCli(executable,"--open-url",uri));
        } catch(Exception ex) {
            if(!IsDisposed&&!closing&&request==conversationRequest&&strip.Selected==window)
                Error("项目窗口已打开，但未能自动切换 Codex 对话：\n"+ex.Message+"\n请确认该窗口已安装并登录 Codex 扩展，也可从扩展历史记录手动打开。");
        }
    }
    void UpdateCompatible(bool raise) { UpdateCompatible(raise,HostApi.GetForegroundWindow()); }
    void UpdateCompatible(bool raise,IntPtr foreground) {
        FollowCompatibleForeground(foreground);
        foreach(var h in hosted) if(h.Compatible && Native.IsWindow(h.Handle)) {
            bool show=!closing && h==strip.Selected && Visible && Enabled && !dialogOpen && WindowState!=FormWindowState.Minimized;
            bool visible=Native.IsWindowVisible(h.Handle);
            if(show!=h.CompatVisible||show!=visible) { Native.ShowWindowAsync(h.Handle,show?4:0); h.CompatVisible=show; }
            if(!show) continue;
            Rectangle bounds=surface.RectangleToScreen(surface.ClientRectangle);
            Native.RectangleEdges actual; bool positioned=HostApi.GetWindowRect(h.Handle,out actual)&&actual.left==bounds.Left&&actual.top==bounds.Top&&actual.right==bounds.Right&&actual.bottom==bounds.Bottom;
            bool covered=!WindowAbove(h.Handle,Handle);
            bool lift=(raise||covered)&&!menu.Visible;
            if(bounds!=h.CompatBounds||!positioned||lift) {
                // HWND_NOTOPMOST does not raise an already non-topmost window.
                // Demote only when needed, then use HWND_TOP to stay above the container.
                if(lift&&!TopMost&&(Native.GetStyle(h.Handle,-20).ToInt64()&8)!=0) Native.SetWindowPos(h.Handle,new IntPtr(-2),0,0,0,0,0x4013);
                // Passive repairs insert directly above the container, below any
                // unrelated windows already above it. Never activate or attach queues.
                IntPtr after=lift?(raise?(TopMost?new IntPtr(-1):IntPtr.Zero):HostApi.GetWindow(Handle,3)):IntPtr.Zero;
                if(after==h.Handle) after=HostApi.GetWindow(h.Handle,3);
                Native.SetWindowPos(h.Handle,after,bounds.X,bounds.Y,bounds.Width,bounds.Height,(lift?0x4010u:0x4014u)|0x0040u); h.CompatBounds=bounds;
            }
        }
    }
    static bool WindowAbove(IntPtr window,IntPtr reference) {
        int limit=2048;
        for(IntPtr w=HostApi.GetWindow(reference,3);w!=IntPtr.Zero&&limit-->0;w=HostApi.GetWindow(w,3)) if(w==window) return true;
        return false;
    }
    void FollowCompatibleForeground(IntPtr foreground) {
        var h=strip.Selected;
        if(closing||IsDisposed||!Visible||!Enabled||dialogOpen||menu.Visible||WindowState==FormWindowState.Minimized||h==null||!h.Compatible||!Native.IsWindowVisible(h.Handle)||!InWindow(h.Handle,foreground)) return;
        if(HostApi.GetWindow(h.Handle,2)==Handle) return;
        // Keep the container immediately below the active app, without activating
        // it or creating an owner/parent relationship that couples input queues.
        Native.SetWindowPos(Handle,h.Handle,0,0,0,0,0x0213);
    }
    void InstallForegroundHook() {
        foregroundProc=delegate(IntPtr hookId,uint ev,IntPtr window,int objectId,int childId,uint thread,uint time) {
            if(closing||IsDisposed) return;
            // Ignore stale events when the user has already switched elsewhere.
            IntPtr current=HostApi.GetForegroundWindow();
            if(current==window) FollowCompatibleForeground(current);
        };
        foregroundHook=HostApi.SetWinEventHook(3,3,IntPtr.Zero,foregroundProc,0,0,0);
        // The existing timer also checks foreground state if the event hook fails.
    }
    void RestoreBinding(Hosted h) {
        if(!h.IsVSCode) return;
        try { ApplyBinding(h,bindings.Read()); } catch(Exception ex) { h.CodexDetail="关联记录读取失败："+ex.Message; }
    }
    void ApplyBinding(Hosted h,Dictionary<string,string> map) {
        string key=BindingStore.Key(h.ApplicationName,Native.Title(h.Handle)),path;
        map.TryGetValue(key,out path);
        h.BindingKey=key;
        if(h.CodexPath!=path) { h.CodexPath=path; h.CodexStatus="unknown"; h.CodexDetail=path==null?"尚未关联会话":"已恢复最近关联，正在读取状态"; }
    }
    void RefreshBindings() {
        if(DateTime.UtcNow<nextBindingRefresh) return; nextBindingRefresh=DateTime.UtcNow.AddSeconds(2);
        try { var map=bindings.Read(); foreach(var h in hosted) if(h.IsVSCode) ApplyBinding(h,map); } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(ArgumentException) {} catch(InvalidOperationException) {}
    }
    bool SaveBinding(Hosted h,string path) {
        try {
            string key=BindingStore.Key(h.ApplicationName,Native.Title(h.Handle)); bindings.Save(key,path);
            var map=bindings.Read(); foreach(var item in hosted) if(item.IsVSCode) ApplyBinding(item,map);
            return true;
        } catch(Exception ex) { Error("保存关联失败："+ex.Message); return false; }
    }
    bool DropZone(Point point) { return Visible && Enabled && !dialogOpen && WindowState!=FormWindowState.Minimized && strip.RectangleToScreen(strip.ClientRectangle).Contains(point); }
    void PickAt(Point point) {
        IntPtr target=HostApi.GetAncestor(HostApi.WindowFromPoint(point),2);
        var item=Discover(false).Find(h=>h.Handle==target);
        if(item!=null) { Attach(item); Activate(); QueueFocus(); }
    }
    void BeginWindowMove(IntPtr window) {
        movingWindow=IntPtr.Zero;
        if(!Visible||dialogOpen||window==Handle||hosted.Exists(h=>h.Handle==window)||Native.GetParent(window)!=IntPtr.Zero) return;
        try { moveSnapshot=WindowSnapshot.Capture(window); } catch(Exception) { return; }
        moveStart=moveSnapshot.Bounds;
        movingWindow=window;
    }
    bool FinishWindowMove(IntPtr window,Point pointer) {
        if(window!=movingWindow) return false;
        movingWindow=IntPtr.Zero; strip.ExternalDragOver=false; strip.Invalidate();
        Native.RectangleEdges end; if(!HostApi.GetWindowRect(window,out end)) return false;
        // Only a moved window, not a resize or a cancelled drag, qualifies.
        if((end.left==moveStart.left&&end.top==moveStart.top)||end.right-end.left!=moveStart.right-moveStart.left||end.bottom-end.top!=moveStart.bottom-moveStart.top||!DropZone(pointer)) return false;
        var item=Discover(false).Find(h=>h.Handle==window); if(item==null) return false;
        Attach(item,moveSnapshot); Activate(); QueueFocus(); return hosted.Exists(h=>h.Handle==window);
    }
    void InstallMoveHook() {
        moveProc=delegate(IntPtr hookId,uint ev,IntPtr window,int objectId,int childId,uint thread,uint time) {
            if(IsDisposed||window==IntPtr.Zero||objectId!=0||childId!=0) return;
            if(ev==0x000A) BeginWindowMove(window);
            else if(ev==0x000B) { Point point; HostApi.GetCursorPos(out point); FinishWindowMove(window,point); }
        };
        moveHook=HostApi.SetWinEventHook(0x000A,0x000B,IntPtr.Zero,moveProc,0,0,2);
        if(moveHook==IntPtr.Zero) Error("标题栏拖入监听不可用，可使用“拖拽接入”按钮或窗口列表。");
    }
    static bool Valid(Keys keys) { Keys key=keys&Keys.KeyCode; return (keys&(Keys.Control|Keys.Alt))!=0 && key!=Keys.None && key!=Keys.ControlKey && key!=Keys.Menu && key!=Keys.ShiftKey && key!=Keys.LWin && key!=Keys.RWin; }
    static bool ReadCompatibleMode(string path) {
        try { return !File.Exists(path)||File.ReadAllText(path).Trim()!="embedded"; }
        catch(IOException) { return true; } catch(UnauthorizedAccessException) { return true; }
    }
    void LoadSettings() { try { if(File.Exists(settings)) { var lines=File.ReadAllLines(settings); Keys a=(Keys)Int32.Parse(lines[0]), b=(Keys)Int32.Parse(lines[1]); if(Valid(a)&&Valid(b)&&a!=b) { left=a; right=b; } keyboardEnabled=lines.Length>2 && lines[2]=="keyboard-enabled"; } } catch(Exception) {} }
    void Settings() {
        dialogOpen=true;
        try { using(var d=new Form { Text="切换快捷键", ClientSize=new Size(490,285), FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, StartPosition=FormStartPosition.CenterParent, Font=Font }) {
            var a=new ShortcutBox(left) { Location=new Point(130,24),Width=300 }; var b=new ShortcutBox(right) { Location=new Point(130,67),Width=300 };
            d.Controls.Add(new Label { Text="向左切换",Location=new Point(20,27) }); d.Controls.Add(new Label { Text="向右切换",Location=new Point(20,70) }); d.Controls.Add(a); d.Controls.Add(b);
            var enable=new CheckBox { Text="额外启用上方键盘组合键", Checked=keyboardEnabled,Location=new Point(20,105),Width=420 }; d.Controls.Add(enable);
            d.Controls.Add(new Label { Text="默认：Win + Alt + 鼠标左键 / 右键 → 向左 / 向右\nWin + Alt + 滚轮向前 / 向后 → 向左 / 向右\n鼠标组合键始终启用，只在 Code Tabs 内生效。\n上方输入框可录入额外的 Ctrl 或 Alt 键盘组合键。",Location=new Point(20,140),Size=new Size(450,80) });
            var save=new Button { Text="保存",Location=new Point(370,242) }; d.Controls.Add(save);
            save.Click+=delegate {
                if(!Valid(a.Shortcut)||!Valid(b.Shortcut)||a.Shortcut==b.Shortcut) { MessageBox.Show(d,"请使用两个不同的组合键，并包含 Ctrl 或 Alt。"); return; }
                try { Directory.CreateDirectory(Path.GetDirectoryName(settings)); File.WriteAllLines(settings,new[]{((int)a.Shortcut).ToString(),((int)b.Shortcut).ToString(),enable.Checked?"keyboard-enabled":"keyboard-disabled"}); left=a.Shortcut; right=b.Shortcut; keyboardEnabled=enable.Checked; UpdateKeyboardHook(); d.Close(); }
                catch(Exception ex) { MessageBox.Show(d,"保存失败："+ex.Message); }
            }; d.ShowDialog(this);
        } } finally { dialogOpen=false; }
    }
    void UpdateKeyboardHook() {
        // With optional shortcuts off, ordinary typing/Win+Space must not wait
        // for this UI thread's low-level keyboard hook. Esc needs it only while picking.
        bool needed=!closing&&(keyboardEnabled||strip.IsPicking);
        if(!needed) {
            if(hook!=IntPtr.Zero&&swallowed.Count==0) { HostApi.UnhookWindowsHookEx(hook); hook=IntPtr.Zero; }
            return;
        }
        if(hook==IntPtr.Zero) InstallHook();
    }
    void InstallHook() {
        hookProc=delegate(int code,IntPtr w,IntPtr l) {
            if(code>=0) {
                int key=Marshal.ReadInt32(l); int message=w.ToInt32(); bool down=message==0x100||message==0x104;
                if(!down && swallowed.Remove(key)) { BeginInvoke((Action)UpdateKeyboardHook); return new IntPtr(1); }
                if(down && key==27 && strip.IsPicking) { swallowed.Add(key); BeginInvoke((Action)delegate { strip.CancelPick(); }); return new IntPtr(1); }
                IntPtr foreground=HostApi.GetForegroundWindow(); bool active=ManagedForeground(foreground);
                if(down && keyboardEnabled && active && !dialogOpen && !menu.Visible && hosted.Count>0) {
                    Keys keys=(Keys)key;
                    if(HostApi.GetAsyncKeyState(0x11)<0) keys|=Keys.Control;
                    if(HostApi.GetAsyncKeyState(0x12)<0) keys|=Keys.Alt;
                    if(HostApi.GetAsyncKeyState(0x10)<0) keys|=Keys.Shift;
                    if((keys==left||keys==right) && HostApi.GetAsyncKeyState(0x5B)>=0 && HostApi.GetAsyncKeyState(0x5C)>=0) {
                        if(swallowed.Add(key)) { int direction=keys==left?-1:1; BeginInvoke((Action)delegate { Step(direction); }); }
                        return new IntPtr(1);
                    }
                }
            }
            return HostApi.CallNextHookEx(hook,code,w,l);
        };
        hook=HostApi.SetWindowsHookEx(13,hookProc,HostApi.GetModuleHandle(null),0);
        if(hook==IntPtr.Zero) Error("快捷键初始化失败，仍可点击标签切换。");
    }
    static bool MouseChord() { return (HostApi.GetAsyncKeyState(0x5B)<0||HostApi.GetAsyncKeyState(0x5C)<0) && HostApi.GetAsyncKeyState(0x12)<0; }
    bool RouteGesture(int message,uint mouseData,bool chord,bool inside,bool active) {
        if(!chord) wheelRemainder=0;
        if(!inside || !active || dialogOpen || hosted.Count==0 || !chord || (message!=0x201&&message!=0x204&&message!=0x20A)) return false;
        int steps=0;
        if(message==0x201) { steps=-1; eatLeftUp=true; }
        if(message==0x204) { steps=1; eatRightUp=true; }
        if(message==0x20A) { wheelRemainder+=(short)(mouseData>>16); steps=-(wheelRemainder/120); wheelRemainder%=120; }
        int amount=steps; BeginInvoke((Action)delegate { menu.Close(); for(int i=0;i<Math.Abs(amount);i++) Step(Math.Sign(amount)); });
        return true;
    }
    void InstallMouseHook() {
        mouseProc=delegate(int code,IntPtr w,IntPtr l) {
            if(code>=0) {
                int message=w.ToInt32(); var data=(HostApi.MouseData)Marshal.PtrToStructure(l,typeof(HostApi.MouseData));
                if(message==0x202 && eatLeftUp) { eatLeftUp=false; return new IntPtr(1); }
                if(message==0x205 && eatRightUp) { eatRightUp=false; return new IntPtr(1); }
                bool down=message==0x201||message==0x204||message==0x207||message==0x20B;
                if(down) DismissMenuOnClick(data.point);
                IntPtr target=HostApi.WindowFromPoint(data.point); IntPtr foreground=HostApi.GetForegroundWindow();
                bool inside=ManagedForeground(target);
                bool active=ManagedForeground(foreground);
                if(RouteGesture(message,data.data,MouseChord(),inside,active)) {
                    // Mask bare Win/Alt release so the shell Start menu/editor menu is not opened.
                    if(HostApi.GetAsyncKeyState(0x11)>=0) { HostApi.keybd_event(0x11,0,0,UIntPtr.Zero); HostApi.keybd_event(0x11,0,2,UIntPtr.Zero); }
                    return new IntPtr(1);
                }
            }
            return HostApi.CallNextHookEx(mouseHook,code,w,l);
        };
        mouseHook=HostApi.SetWindowsHookEx(14,mouseProc,HostApi.GetModuleHandle(null),0);
        if(mouseHook==IntPtr.Zero) Error("鼠标快捷键初始化失败，仍可点击标签切换。");
    }
    void DismissMenuOnClick(Point point) {
        if(menu.Visible && !menu.Bounds.Contains(point) && !strip.RectangleToScreen(strip.ClientRectangle).Contains(point)) BeginInvoke((Action)delegate { menu.Close(ToolStripDropDownCloseReason.AppClicked); });
    }
    async void BindCodex() {
        var selected=strip.Selected; if(selected==null) return;
        dialogOpen=true;
        try {
            var sessions=await System.Threading.Tasks.Task.Run(()=>CodexState.Discover());
            if(IsDisposed || !hosted.Contains(selected)) return;
            using(var d=new Form { Text="关联此标签的 Codex 会话",Size=new Size(850,470),StartPosition=FormStartPosition.CenterParent,Font=Font }) {
                var info=new Label { Dock=DockStyle.Top,Height=55,Text="选择此项目对应的 Codex 会话。关联将保存，下次自动恢复。\n同名项目重新选择时，最新关联覆盖旧关联；取消关联也会保存。" };
                var list=new ListBox { Dock=DockStyle.Fill,HorizontalScrollbar=true }; foreach(var session in sessions) list.Items.Add(session);
                var buttons=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=40 };
                var bind=new Button { Text="关联所选",AutoSize=true }; var browse=new Button { Text="选择会话文件…",AutoSize=true }; var clear=new Button { Text="取消关联",AutoSize=true };
                bind.Click+=delegate { var item=list.SelectedItem as CodexSession; if(item!=null && SaveBinding(selected,item.Path)) d.Close(); };
                browse.Click+=delegate { using(var picker=new OpenFileDialog { Filter="Codex 会话|*.jsonl",InitialDirectory=CodexState.Root }) { if(picker.ShowDialog(d)==DialogResult.OK && SaveBinding(selected,picker.FileName)) d.Close(); } };
                clear.Click+=delegate { if(SaveBinding(selected,null)) d.Close(); };
                buttons.Controls.Add(bind); buttons.Controls.Add(browse); buttons.Controls.Add(clear); d.Controls.Add(list); d.Controls.Add(info); d.Controls.Add(buttons); d.ShowDialog(this);
            }
        } catch(Exception ex) { if(!IsDisposed) Error("读取会话列表失败："+ex.Message); }
        finally { dialogOpen=false; }
    }
    public void VerifyLayout() {
        testing=true;
        VerifyKeyboardHookLifetime();
        string modeTest=PortableConfig.FilePath("window-mode-test-"+Guid.NewGuid().ToString("N")+".txt");
        try {
            if(!ReadCompatibleMode(modeTest)) throw new Exception("Compatibility mode not default");
            Directory.CreateDirectory(Path.GetDirectoryName(modeTest));
            File.WriteAllText(modeTest,"embedded"); if(ReadCompatibleMode(modeTest)) throw new Exception("Embedded preference not loaded");
            File.WriteAllText(modeTest,"compatible"); if(!ReadCompatibleMode(modeTest)) throw new Exception("Compatible preference not loaded");
        } finally { if(File.Exists(modeTest)) File.Delete(modeTest); }
        var remoteProject=new Hosted { ProjectPath="/work/project.with.dot and space",RemoteHostId="remote-test",RemoteKind="ssh",RemoteAlias="test-server" };
        string remoteArgs=LaunchArguments(remoteProject);
        if(!remoteArgs.Contains("--folder-uri")||!remoteArgs.Contains("ssh-remote+test-server/work/project.with.dot%20and%20space")) throw new Exception("Remote folder launch URI failed");
        if(QuoteArgument("D:\\folder\\")!="\"D:\\folder\\\\\"") throw new Exception("Launch argument trailing slash escaping failed");
        using(var target=new Form { Text="new-location - Visual Studio Code" }) {
            var window=new Hosted { Handle=target.Handle,IsVSCode=true,ApplicationName="Code" };
            var project=new Hosted { ApplicationName="Code",BindingKey="code|old-location",OriginalProjectPath=@"D:\old-location",ProjectPath=@"D:\new-location" };
            if(!MatchesProject(window,project)) throw new Exception("Custom directory must match new project window");
            target.Text="old-location - Visual Studio Code";
            if(MatchesProject(window,project)) throw new Exception("Custom directory must not route to old window");
            target.Text="project.with.dot and space [SSH: test-server] - Visual Studio Code";
            if(!MatchesProject(window,remoteProject)) throw new Exception("Remote window routing failed");
            target.Text="project.with.dot and space - Visual Studio Code";
            if(MatchesProject(window,remoteProject)) throw new Exception("Remote project matched local window");
        }
        Select(null); if(!overview.Visible||!strip.OverviewSelected||strip.Selected!=null) throw new Exception("Overview tab failed");
        strip.VerifyToolbar(); var originalFont=strip.Font;
        using(var largeFont=new Font(originalFont.FontFamily,16,FontStyle.Bold)) { strip.Font=largeFont; strip.VerifyToolbar(); strip.Font=originalFont; }
        using(var a=new Form { Text="Test A" }) using(var b=new Form { Text="Test B" }) {
            a.Show(); b.Show(); var ha=new Hosted { Handle=a.Handle }; var hb=new Hosted { Handle=b.Handle };
            Attach(ha); Attach(hb); Application.DoEvents();
            if(Text!=b.Text) throw new Exception("Selected caption not synchronized");
            b.Text="Updated active title"; Tick(); if(Text!=b.Text) throw new Exception("Live caption not synchronized");
            strip.TogglePin(); if(!TopMost||!strip.Pinned) throw new Exception("Pin failed"); strip.TogglePin(); if(TopMost||strip.Pinned) throw new Exception("Unpin failed");
            Size=new Size(920,620); Application.DoEvents(); CheckBounds(hb);
            WindowState=FormWindowState.Maximized; Application.DoEvents(); Fit(); CheckBounds(hb);
            WindowState=FormWindowState.Normal; Application.DoEvents(); Fit(); CheckBounds(hb);
            Step(-1); if(strip.Selected!=ha) throw new Exception("Left switch failed");
            Step(-1); if(strip.Selected!=hb) throw new Exception("Wrap failed");
            strip.VerifyDrag(); Step(1); if(strip.Selected!=hb) throw new Exception("Reorder switch failed");
            VerifyGestures(ha,hb);
            bool preventClose=true;
            a.FormClosing+=delegate(object sender,FormClosingEventArgs e) { e.Cancel=preventClose; };
            strip.ClickActionForTest(ha,true); Application.DoEvents(); if(!Native.IsWindow(ha.Handle)) throw new Exception("Close bypassed cancel");
            preventClose=false; strip.ClickActionForTest(ha,true); Application.DoEvents(); Tick(); if(Native.IsWindow(ha.Handle)||hosted.Contains(ha)) throw new Exception("Close failed");
            strip.ClickActionForTest(hb,false); Application.DoEvents(); if(!Native.IsWindow(b.Handle)||hosted.Contains(hb)||Native.GetParent(b.Handle)!=IntPtr.Zero||Native.GetStyle(b.Handle,-16)!=hb.Style) throw new Exception("Detach button failed");
        }
        using(var code=new Form { Text="Remove VS Code fixture" }) using(var other=new Form { Text="Keep other application fixture" }) {
            code.Show(); other.Show();
            var hc=new Hosted { Handle=code.Handle,IsVSCode=true,ApplicationName="Code" };
            var ho=new Hosted { Handle=other.Handle,ApplicationName="Other" };
            Attach(hc); Attach(ho); RemoveAllCode(); WaitForRestoresForTest();
            if(hosted.Contains(hc)||!hosted.Contains(ho)||!Native.IsWindow(code.Handle)||Native.GetParent(code.Handle)!=IntPtr.Zero) throw new Exception("Remove all VS Code affected other applications or closed a window");
            DetachForTest(ho);
        }
    }
    void VerifyGestures(Hosted a,Hosted b) {
        Select(a);
        if(RouteGesture(0x201,0,false,true,true)||RouteGesture(0x201,0,true,false,true)||RouteGesture(0x201,0,true,true,false)) throw new Exception("Gesture leaked outside scope");
        RouteGesture(0x201,0,true,true,true); Application.DoEvents(); if(strip.Selected!=b) throw new Exception("Left mouse switch failed");
        RouteGesture(0x204,0,true,true,true); Application.DoEvents(); if(strip.Selected!=a) throw new Exception("Right mouse switch failed");
        // Submit the synthetic wheel pair atomically: real mouse moves without
        // Win+Alt may reset the accumulator while DoEvents pumps the global hook.
        wheelRemainder=0;
        RouteGesture(0x20A,60u<<16,true,true,true); if(wheelRemainder!=60||strip.Selected!=a) throw new Exception("High precision wheel threshold failed");
        RouteGesture(0x20A,60u<<16,true,true,true); Application.DoEvents(); if(strip.Selected!=b) throw new Exception("Forward wheel failed");
        RouteGesture(0x20A,unchecked((uint)(-120<<16)),true,true,true); Application.DoEvents(); if(strip.Selected!=a) throw new Exception("Backward wheel failed");
        eatLeftUp=false; eatRightUp=false;
        VerifyMenuDismiss();
    }
    void VerifyMenuDismiss() {
        // Exercise the hook's outside-menu click path over the hosted surface.
        menu.Show(strip,new Point(60,strip.Height)); Application.DoEvents();
        DismissMenuOnClick(surface.PointToScreen(new Point(surface.Width-30,surface.Height-30)));
        Application.DoEvents(); if(menu.Visible) throw new Exception("Menu failed to dismiss");
    }
    public void VerifyExternalWindow() {
        using(var process=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--test-window") { UseShellExecute=false })) {
            try {
                IntPtr handle=IntPtr.Zero;
                for(int i=0;i<100 && handle==IntPtr.Zero;i++) { System.Threading.Thread.Sleep(50); process.Refresh(); handle=process.MainWindowHandle; }
                if(handle==IntPtr.Zero) throw new Exception("External test window failed to start");
                var candidates=Discover(false); var h=candidates.Find(item=>item.Handle==handle);
                if(h==null) throw new Exception("Generic application discovery failed");
                if(Discover().Exists(item=>item.Handle==handle)) throw new Exception("VS Code-only discovery included another application");
                if(candidates.Exists(item=>item.Handle==Handle)) throw new Exception("Discovery included its own container");
                Attach(h); Application.DoEvents(); CheckBounds(h);
                VerifyFocusRecovery(h);
                VerifyMenuDismiss();
                WindowState=FormWindowState.Maximized; Application.DoEvents(); Fit(); CheckBounds(h);
                WindowState=FormWindowState.Normal; Size=new Size(1030,710); Application.DoEvents(); Fit(); CheckBounds(h);
                Native.SetWindowPos(handle,IntPtr.Zero,40,30,500,300,0x0014); Fit(); CheckBounds(h);
                DetachForTest(h); if(Native.GetParent(handle)!=IntPtr.Zero || Native.GetStyle(handle,-16)!=h.Style) throw new Exception("External restore failed");
                AssertRestoredPlacement(handle,h.Placement,"Embedded detach");
                // A release outside the strip must never attach a moved window.
                BeginWindowMove(handle); Native.SetWindowPos(handle,IntPtr.Zero,70,80,0,0,0x0015);
                if(FinishWindowMove(handle,new Point(-10000,-10000))) throw new Exception("Drag accepted outside target");
                var beforeDrag=ReadPlacementForTest(handle);
                BeginWindowMove(handle); Native.SetWindowPos(handle,IntPtr.Zero,90,100,0,0,0x0015);
                Point drop=strip.PointToScreen(new Point(20,20));
                if(!FinishWindowMove(handle,drop)) throw new Exception("Title drag attach failed");
                DetachForTest(hosted.Find(item=>item.Handle==handle));
                AssertRestoredPlacement(handle,beforeDrag,"Title drag detach");
                VerifyCompatible(handle);
                VerifyPlacementRestore(handle);
                VerifyDelayedResizeRestore(handle);
                VerifyCloseRestore(handle);
                // Previous restore cases can leave the fixture maximized on another screen.
                // Restore it, place it inside the work area, then verify the actual hit target.
                var pickArea=Screen.PrimaryScreen.WorkingArea;
                // Keep this short-lived fixture above unrelated always-on-top desktop apps.
                Native.ShowWindow(handle,9); Native.SetWindowPos(handle,new IntPtr(-1),pickArea.Left+40,pickArea.Top+40,640,480,0x0040);
                var pickPoint=new Point(pickArea.Left+140,pickArea.Top+140);
                var pickWait=Stopwatch.StartNew();
                while(HostApi.GetAncestor(HostApi.WindowFromPoint(pickPoint),2)!=handle && pickWait.ElapsedMilliseconds<2000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                if(HostApi.GetAncestor(HostApi.WindowFromPoint(pickPoint),2)!=handle) throw new Exception("Drag picker fixture is obscured at the test point");
                PickAt(pickPoint); Application.DoEvents();
                if(!hosted.Exists(item=>item.Handle==handle)) throw new Exception("Drag picker failed");
                DetachForTest(hosted.Find(item=>item.Handle==handle));
            } finally { process.CloseMainWindow(); process.WaitForExit(5000); }
        }
    }
    static Native.Placement ReadPlacementForTest(IntPtr window) {
        var placement=new Native.Placement { length=Marshal.SizeOf(typeof(Native.Placement)) };
        if(!Native.GetWindowPlacement(window,ref placement)) throw new Exception("Cannot read fixture placement");
        return placement;
    }
    void DetachForTest(Hosted h) {
        Detach(h);
        WaitForRestoresForTest();
    }
    void WaitForRestoresForTest() {
        var watch=Stopwatch.StartNew();
        while(restoringWindows.Count>0&&watch.ElapsedMilliseconds<4500) {
            Application.DoEvents(); FinishRestores(); System.Threading.Thread.Sleep(10);
        }
        if(restoringWindows.Count>0||failedRestores.Count>0) throw new Exception("Restore tasks did not finish");
    }
    static bool SamePlacement(Native.Placement a,Native.Placement b) {
        return a.show==b.show&&a.normal.left==b.normal.left&&a.normal.top==b.normal.top&&a.normal.right==b.normal.right&&a.normal.bottom==b.normal.bottom;
    }
    void AssertRestoredPlacement(IntPtr window,Native.Placement expected,string label,Native.RectangleEdges? screenBounds=null) {
        // Independent windows restore asynchronously; wait for the target's queue.
        var watch=Stopwatch.StartNew(); int stable=0;
        while(watch.ElapsedMilliseconds<2000) {
            Application.DoEvents(); FinishRestores(); Fit(); UpdateCompatible(false);
            Native.RectangleEdges actualBounds; HostApi.GetWindowRect(window,out actualBounds);
            if(SamePlacement(ReadPlacementForTest(window),expected)&&(!screenBounds.HasValue||SameBounds(actualBounds,screenBounds.Value))) { if(++stable>=10) return; }
            else stable=0;
            System.Threading.Thread.Sleep(10);
        }
        var actual=ReadPlacementForTest(window);
        Native.RectangleEdges actualScreen; HostApi.GetWindowRect(window,out actualScreen);
        throw new Exception(label+" did not restore show/normal bounds: "+actual.show+" "+actual.normal.left+","+actual.normal.top+","+actual.normal.right+","+actual.normal.bottom+" expected "+expected.show+" "+expected.normal.left+","+expected.normal.top+","+expected.normal.right+","+expected.normal.bottom+" screen="+actualScreen.left+","+actualScreen.top+","+actualScreen.right+","+actualScreen.bottom+(screenBounds.HasValue?" expected screen="+screenBounds.Value.left+","+screenBounds.Value.top+","+screenBounds.Value.right+","+screenBounds.Value.bottom:""));
    }
    static bool SameBounds(Native.RectangleEdges a,Native.RectangleEdges b) { return a.left==b.left&&a.top==b.top&&a.right==b.right&&a.bottom==b.bottom; }
    void VerifyPlacementRestore(IntPtr window) {
        bool savedMode=vscodeCompatible;
        Point savedLocation=Location;
        try {
            foreach(var display in Screen.AllScreens) foreach(bool compatible in new[]{true,false}) foreach(int show in new[]{1,3}) {
                vscodeCompatible=compatible;
                Native.ShowWindow(window,1);
                Rectangle area=display.WorkingArea;
                var hostDisplay=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName!=display.DeviceName)??display;
                Location=new Point(hostDisplay.WorkingArea.X+20,hostDisplay.WorkingArea.Y+20);
                Native.SetWindowPos(window,IntPtr.Zero,area.X+40,area.Y+50,Math.Min(640,area.Width-80),Math.Min(480,area.Height-100),0x0014);
                Native.ShowWindow(window,show);
                for(int i=0;i<10;i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                var expected=ReadPlacementForTest(window);
                Native.RectangleEdges expectedScreen; HostApi.GetWindowRect(window,out expectedScreen);
                Console.Error.WriteLine("Restore test: "+display.DeviceName+" monitor="+display.Bounds+" work="+area+" dpi="+WindowRestore.GetDpiForWindow(window)+" compatible="+compatible+" show="+show);
                var h=new Hosted { Handle=window,IsVSCode=true,ApplicationName="Code" };
                Attach(h); Application.DoEvents();
                Size=new Size(970,680); Fit(); Select(null); Select(h);
                if(show==3) { RestoreAll(); WaitForRestoresForTest(); } else DetachForTest(h);
                AssertRestoredPlacement(window,expected,(compatible?"Compatible":"Embedded")+" show="+show,expectedScreen);
                // A second attachment must snapshot the new independent placement.
                Native.ShowWindow(window,1); Native.SetWindowPos(window,IntPtr.Zero,area.X+90,area.Y+80,580,410,0x0014);
                expected=ReadPlacementForTest(window); Attach(h); DetachForTest(h);
                AssertRestoredPlacement(window,expected,"Repeated attach/detach");
            }
        } finally { vscodeCompatible=savedMode; Location=savedLocation; }
        Console.Error.WriteLine("Placement restore: normal/maximized, both modes, title drag and repeated attach passed");
    }
    void VerifyDelayedResizeRestore(IntPtr window) {
        Native.ShowWindow(window,1);
        var snapshot=WindowSnapshot.Capture(window);
        var h=new Hosted { Handle=window,Compatible=true };
        Attach(h); Application.DoEvents(); Detach(h);
        HostApi.PostMessage(window,InputIsolationFixture.DelayedResizeMessage,IntPtr.Zero,IntPtr.Zero);
        WaitForRestoresForTest();
        AssertRestoredPlacement(window,snapshot.Placement,"Delayed size-only change",snapshot.Bounds);
        Console.Error.WriteLine("Delayed application resize: corrected original physical width/height");
    }
    void VerifyCloseRestore(IntPtr window) {
        foreach(var display in Screen.AllScreens) {
            Native.ShowWindow(window,1);
            var area=display.WorkingArea;
            Native.SetWindowPos(window,IntPtr.Zero,area.X+40,area.Y+50,640,480,0x0014);
            for(int i=0;i<10;i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            var snapshot=WindowSnapshot.Capture(window);
            using(var container=new MainForm(false)) {
                container.testing=true; container.Show();
                var other=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName!=display.DeviceName)??display;
                container.Location=new Point(other.WorkingArea.X+20,other.WorkingArea.Y+20);
                IntPtr originalExStyle=Native.GetStyle(window,-20);
                container.Attach(new Hosted { Handle=window,Compatible=true }); Application.DoEvents();
                container.Close();
                if(container.IsDisposed) throw new Exception("Container exited before asynchronous restore completed");
                var watch=Stopwatch.StartNew();
                while(!container.IsDisposed&&watch.ElapsedMilliseconds<4500) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                if(!container.IsDisposed) throw new Exception("Container did not exit after restoring windows");
                if(Native.GetStyle(window,-20)!=originalExStyle) throw new Exception("Closing container did not restore shell window styles");
            }
            AssertRestoredPlacement(window,snapshot.Placement,"Close container "+display.DeviceName,snapshot.Bounds);
        }
        Console.Error.WriteLine("Actual FormClosing: waits for verified physical bounds on all monitors");
    }
    void VerifyCompatible(IntPtr window) {
        IntPtr parent=Native.GetParent(window),style=Native.GetStyle(window,-16),owner=Native.GetStyle(window,-8),exStyle=Native.GetStyle(window,-20);
        // Exercise the production VS Code default, not a manually forced flag.
        bool savedMode=vscodeCompatible; vscodeCompatible=true;
        var h=new Hosted { Handle=window,IsVSCode=true,ApplicationName="Code" };
        Attach(h); Application.DoEvents();
        if(!h.Compatible||Native.GetParent(window)!=parent||Native.GetStyle(window,-16)!=style||Native.GetStyle(window,-8)!=owner) throw new Exception("Compatibility mode modified native parent/style/owner");
        if((Native.GetStyle(window,-20).ToInt64() & 0x00040080L)!=0x80L) throw new Exception("Hosted window still eligible for taskbar/Alt+Tab");
        VerifyInputIsolation(h);
        VerifyCompatibleMoveAndMenu(h);
        Select(null); if(h.CompatVisible) throw new Exception("Overview did not hide independent window");
        Select(h); if(!h.CompatVisible) throw new Exception("Tab did not show independent window");
        menu.Show(strip,new Point(60,strip.Height));
        if(!h.CompatVisible) throw new Exception("Menu hid independent window");
        UpdateCompatible(true); if(!h.CompatVisible) throw new Exception("Menu update hid independent window");
        menu.Close(); Application.DoEvents(); UpdateCompatible(false);
        if(!h.CompatVisible) throw new Exception("Menu close did not show independent window");
        WindowState=FormWindowState.Minimized; UpdateCompatible(false); if(h.CompatVisible) throw new Exception("Compatibility minimize failed");
        WindowState=FormWindowState.Normal; UpdateCompatible(true); if(!h.CompatVisible) throw new Exception("Compatibility restore failed");
        DetachForTest(h); if(hosted.Contains(h)||!Native.IsWindow(window)) throw new Exception("Compatibility detach failed");
        if(Native.GetStyle(window,-20)!=exStyle) throw new Exception("Detach did not restore original shell window styles");
        vscodeCompatible=false; Attach(h); Application.DoEvents();
        if(h.Compatible||Native.GetParent(window)!=surface.Handle) throw new Exception("Embedded opt-out failed");
        DetachForTest(h); vscodeCompatible=savedMode;
    }
    void VerifyCompatibleMoveAndMenu(Hosted h) {
        bool running=timer.Enabled; timer.Stop(); Point original=Location;
        // This test supplies controlled foreground snapshots: unattended desktops
        // may deny foreground activation of a fixture. Test real z-order changes.
        if(foregroundHook!=IntPtr.Zero) { HostApi.UnhookWinEvent(foregroundHook); foregroundHook=IntPtr.Zero; }
        try {
            for(int i=0;i<5;i++) {
                Location=new Point(original.X+12*(i+1),original.Y+8*(i+1));
                if(h.CompatBounds!=surface.RectangleToScreen(surface.ClientRectangle)) throw new Exception("Movement waited for polling timer");
            }
            Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0013);
            UpdateCompatible(true);
            bool above=false; var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<1500) {
                Application.DoEvents();
                for(IntPtr w=HostApi.GetWindow(Handle,3);w!=IntPtr.Zero;w=HostApi.GetWindow(w,3)) if(w==h.Handle) { above=true; break; }
                if(above) break; System.Threading.Thread.Sleep(10);
            }
            if(!above) throw new Exception("Compatible window remained behind container");
            CheckBounds(h);
            using(var middle=new Form { Text="Interleaved window fixture",Size=new Size(200,120) }) {
                middle.Show();
                Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0013);
                Native.SetWindowPos(middle.Handle,IntPtr.Zero,0,0,0,0,0x0013);
                Native.SetWindowPos(h.Handle,IntPtr.Zero,0,0,0,0,0x0013);
                IntPtr foreground=HostApi.GetForegroundWindow();
                FollowCompatibleForeground(middle.Handle);
                if(!WindowAbove(middle.Handle,Handle)) throw new Exception("Unrelated foreground raised container");
                FollowCompatibleForeground(h.Handle);
                if(HostApi.GetWindow(h.Handle,2)!=Handle||!WindowAbove(Handle,middle.Handle)) throw new Exception("Active app did not bring container directly behind it");
                if(HostApi.GetForegroundWindow()!=foreground) throw new Exception("Container grouping changed foreground focus");
                bool autoClose=menu.AutoClose;
                try {
                    menu.AutoClose=false; menu.Show(strip,new Point(60,strip.Height));
                    if(!menu.Visible) throw new Exception("Grouping guard menu not opened");
                    Native.SetWindowPos(middle.Handle,h.Handle,0,0,0,0,0x0013);
                    IntPtr before=HostApi.GetWindow(Handle,3);
                    FollowCompatibleForeground(h.Handle);
                    if(HostApi.GetWindow(Handle,3)!=before) throw new Exception("Grouping interfered with open menu");
                } finally { menu.Close(); menu.AutoClose=autoClose; }
            }
            // Simulate a late activation after our asynchronous placement request.
            Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0013);
            using(var unrelated=new Form { Text="Unrelated foreground stand-in",Size=new Size(200,120) }) {
                unrelated.Show(); Native.SetWindowPos(unrelated.Handle,IntPtr.Zero,0,0,0,0,0x0013);
                watch.Restart();
                do { UpdateCompatible(false,unrelated.Handle); Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                while(!WindowAbove(h.Handle,Handle)&&watch.ElapsedMilliseconds<1500);
                if(!WindowAbove(h.Handle,Handle)) throw new Exception("Late container activation remained blank");
                if(!WindowAbove(unrelated.Handle,h.Handle)) throw new Exception("Passive repair raised over unrelated window");
            }
            Native.ShowWindow(h.Handle,0);
            watch.Restart();
            do { UpdateCompatible(false); Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            while(!Native.IsWindowVisible(h.Handle)&&watch.ElapsedMilliseconds<1500);
            if(!Native.IsWindowVisible(h.Handle)) throw new Exception("Cached visibility prevented redisplay");
            // A real right click activates the container before MouseDown. Drain
            // earlier fixture messages, then reproduce that order without a pump.
            Application.DoEvents(); Activate();
            strip.OpenMenuForTest(h);
            if(!menu.Visible||!h.CompatVisible) throw new Exception("Right-click menu not visible or target hidden");
            UpdateCompatible(true); Application.DoEvents();
            if(!menu.Visible||!Native.IsWindowVisible(h.Handle)) throw new Exception("Right-click menu lost focus or target flickered");
            menu.Close();
        } finally { menu.Close(); Location=original; InstallForegroundHook(); if(running) timer.Start(); }
        Console.Error.WriteLine("Compatible movement/menu: immediate layout, visible content and correct z-order passed");
    }
    void VerifyInputIsolation(Hosted h) {
        uint pid; Native.GetWindowThreadProcessId(h.Handle,out pid);
        using(var started=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset,"CodeTabs-test-started-"+pid))
        using(var release=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset,"CodeTabs-test-release-"+pid)) {
            try {
                HostApi.PostMessage(h.Handle,InputIsolationFixture.BlockMessage,IntPtr.Zero,IntPtr.Zero);
                if(!started.WaitOne(2000)) throw new Exception("Isolation fixture did not enter blocked state");
                var watch=Stopwatch.StartNew();
                for(int i=0;i<20;i++) UpdateCompatible(true);
                watch.Stop();
                Console.Error.WriteLine("Independent window: 20 updates while target blocked, "+watch.ElapsedMilliseconds+" ms");
                if(watch.ElapsedMilliseconds>=1000) throw new Exception("Blocked target stalled host window updates");
            } finally { release.Set(); }
        }
    }
    void VerifyKeyboardHookLifetime() {
        bool saved=keyboardEnabled; keyboardEnabled=false; UpdateKeyboardHook();
        if(hook!=IntPtr.Zero) throw new Exception("Keyboard hook remained active with shortcuts disabled");
        strip.StartPick(); if(hook==IntPtr.Zero) throw new Exception("Picker Escape hook missing");
        strip.CancelPick(); if(hook!=IntPtr.Zero) throw new Exception("Picker hook not released");
        keyboardEnabled=true; UpdateKeyboardHook(); if(hook==IntPtr.Zero) throw new Exception("Optional shortcuts hook missing");
        // Retain the hook until a swallowed key's release is paired.
        swallowed.Add(27); keyboardEnabled=false; UpdateKeyboardHook();
        if(hook==IntPtr.Zero) throw new Exception("Hook removed before consumed key release");
        swallowed.Clear(); UpdateKeyboardHook(); if(hook!=IntPtr.Zero) throw new Exception("Hook not removed after key release");
        keyboardEnabled=saved; UpdateKeyboardHook();
    }
    void VerifyFocusRecovery(Hosted h) {
        IntPtr editor=IntPtr.Zero;
        Native.EnumChildWindows(h.Handle,delegate(IntPtr child,IntPtr unused) { var name=new System.Text.StringBuilder(256); HostApi.GetClassName(child,name,256); if(name.ToString().IndexOf("EDIT",StringComparison.OrdinalIgnoreCase)>=0) { editor=child; return false; } return true; },IntPtr.Zero);
        if(editor==IntPtr.Zero) throw new Exception("Test editor not found");
        Activate(); Native.FocusWindow(editor); Application.DoEvents(); RememberFocus(h);
        if(h.LastFocus!=editor) throw new Exception("Editor focus not remembered");
        Native.FocusWindow(surface.Handle); RestoreEditorFocus(h);
        uint pid; var info=HostApi.GuiInfo(); HostApi.GetGUIThreadInfo(Native.GetWindowThreadProcessId(editor,out pid),ref info);
        if(info.focus!=editor) { var frontInfo=HostApi.GuiInfo(); HostApi.GetGUIThreadInfo(0,ref frontInfo); throw new Exception("Editor focus not recovered: focus="+info.focus+" expected="+editor+" saved="+h.LastFocus+" foreground="+HostApi.GetForegroundWindow()+" host="+Handle+" flags="+frontInfo.flags+" capture="+frontInfo.capture+" menu="+frontInfo.menuOwner); }
        menu.Show(strip,new Point(40,strip.Height)); Application.DoEvents(); menu.Close(); Application.DoEvents(); RestoreEditorFocus(h);
        info=HostApi.GuiInfo(); HostApi.GetGUIThreadInfo(Native.GetWindowThreadProcessId(editor,out pid),ref info);
        if(info.focus!=editor) throw new Exception("Menu close did not restore editor focus");
        VerifyImeFocusPolicy(h,editor);
    }
    void VerifyImeFocusPolicy(Hosted h,IntPtr editor) {
        // Use controlled snapshots: CI desktops may report no foreground window.
        // Actual SetFocus still runs against the separate fixture process.
        var info=HostApi.GuiInfo(); info.focus=surface.Handle;
        Native.FocusWindow(surface.Handle); focusRequest=null;
        for(int i=0;i<20;i++) RecoverFocus(Handle,info,false);
        AssertFixtureFocus(editor,surface.Handle,"Idle polling stole focus");
        QueueFocus(); RecoverFocus(Handle,info,true);
        if(focusRequest!=h) throw new Exception("Held gesture lost the bounded request");
        RecoverFocus(Handle,info,false);
        AssertFixtureFocus(editor,editor,"Explicit request failed to restore editor");
        if(focusRequest!=null) throw new Exception("Recovery was not consumed");
        QueueActivationFocus(); if(focusRequest!=null) throw new Exception("Activation rearmed recovery after SetFocus");
        Native.FocusWindow(surface.Handle); RecoverFocus(Handle,info,false);
        AssertFixtureFocus(editor,surface.Handle,"Consumed recovery retried");
        using(var ime=new Form { Text="IME focus stand-in" }) {
            foreach(IntPtr transient in new[]{IntPtr.Zero,ime.Handle,h.Handle,editor}) {
                QueueFocus(); info.focus=transient; RecoverFocus(Handle,info,false);
                if(focusRequest!=null) throw new Exception("Transient/application focus retained recovery request");
                AssertFixtureFocus(editor,surface.Handle,"Transient/application focus was overridden");
            }
            QueueFocus(); info.focus=surface.Handle; RecoverFocus(ime.Handle,info,false);
            if(focusRequest!=null) throw new Exception("External foreground retained recovery request");
            AssertFixtureFocus(editor,surface.Handle,"External foreground was overridden");
        }
        QueueFocus(); focusDeadline=DateTime.UtcNow.AddSeconds(-1); RecoverFocus(Handle,info,false);
        if(focusRequest!=null) throw new Exception("Expired request retained");
        AssertFixtureFocus(editor,surface.Handle,"Expired recovery stole focus");
        QueueFocus(); info.capture=strip.Handle; RecoverFocus(Handle,info,false);
        AssertFixtureFocus(editor,surface.Handle,"Mouse capture was interrupted");
        info.capture=IntPtr.Zero; RecoverFocus(Handle,info,false);
        AssertFixtureFocus(editor,editor,"Recovery after capture release failed");
        Application.DoEvents();
    }
    static void AssertFixtureFocus(IntPtr editor,IntPtr expected,string message) {
        uint pid; var info=HostApi.GuiInfo();
        if(!HostApi.GetGUIThreadInfo(Native.GetWindowThreadProcessId(editor,out pid),ref info)||info.focus!=expected) throw new Exception(message);
    }
    void CheckBounds(Hosted h) { var watch=Stopwatch.StartNew(); Native.RectangleEdges r; do { HostApi.GetWindowRect(h.Handle,out r); var p=surface.PointToScreen(Point.Empty); if(r.left==p.X&&r.top==p.Y&&r.right-r.left==surface.ClientSize.Width&&r.bottom-r.top==surface.ClientSize.Height) return; Application.DoEvents(); System.Threading.Thread.Sleep(10); } while(watch.ElapsedMilliseconds<1500); throw new Exception("Hosted window bounds mismatch: actual="+r.left+","+r.top+","+r.right+","+r.bottom+" expected="+surface.RectangleToScreen(surface.ClientRectangle)); }
}
