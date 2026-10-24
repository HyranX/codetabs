using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

// One scrollable drawing surface; no child windows per project.
sealed class Overview : UserControl
{
    sealed class Entry {
        public Hosted Project; public string Key,Title,Path,Status;
        public bool Hidden,Missing,Viewed,ChangedDirectory,Pinned,Opened,Managed;
    }
    sealed class SearchRecord { public string Key,Title,Text; }
    sealed class CardCanvas : ScrollableControl {
        public List<Entry> Entries=new List<Entry>();
        public string SelectedKey;
        public Action<Entry> OpenEntry,HideEntry,EditEntry,PinEntry,RenameEntry;
        public int LastPainted;
        public string EmptyMessage="暂无可显示的项目。可刷新、添加目录，或勾选显示已隐藏。";
        int columns=1,cardWidth=360,cardHeight=250,gap=16;
        float scale=1;
        public bool Light;
        Color Theme(Color dark,Color light) { return Light?light:dark; }
        Font titleFont;
        readonly ToolTip tip=new ToolTip { AutoPopDelay=15000,InitialDelay=500,ReshowDelay=100 };
        string tipKey;
        public CardCanvas() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.StandardDoubleClick|ControlStyles.Selectable,true);
            AutoScroll=true; TabStop=true; BackColor=Color.FromArgb(22,27,35);
        }
        int S(int n) { return (int)Math.Round(n*scale); }
        public void UpdateEntries(List<Entry> entries) {
            Entries=entries; Arrange(); Invalidate();
        }
        protected override void OnFontChanged(EventArgs e) {
            base.OnFontChanged(e);
            if(titleFont!=null) titleFont.Dispose();
            titleFont=new Font(Font.FontFamily,Font.Size+3f,FontStyle.Bold);
            scale=Math.Max(1,Font.Height/17f); Arrange();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
        void Arrange() {
            // Constant-time arithmetic, independent of the total number of cards.
            gap=S(12); cardHeight=S(208);
            int available=Math.Max(S(180),Width-SystemInformation.VerticalScrollBarWidth-2*gap);
            columns=Math.Max(1,(available+gap)/(S(300)+gap));
            cardWidth=Math.Max(S(180),(available-(columns-1)*gap)/columns);
            int rows=(Entries.Count+columns-1)/columns;
            var extent=new Size(0,rows==0?0:gap+rows*(cardHeight+gap));
            if(AutoScrollMinSize!=extent) AutoScrollMinSize=extent;
        }
        public Rectangle BoundsFor(int index) {
            return new Rectangle(gap+(index%columns)*(cardWidth+gap),gap+(index/columns)*(cardHeight+gap)+AutoScrollPosition.Y,cardWidth,cardHeight);
        }
        Rectangle ActionBounds(Rectangle r,int index) {
            int width=(r.Width-S(28)-4*S(6)-S(28))/4;
            return new Rectangle(r.Left+S(14)+index*(width+S(6)),r.Bottom-S(40),index==4?S(28):width,S(28));
        }
        Rectangle OpenBounds(Rectangle r) { return ActionBounds(r,0); }
        Rectangle RenameBounds(Rectangle r) { return ActionBounds(r,1); }
        Rectangle PinBounds(Rectangle r) { return ActionBounds(r,2); }
        Rectangle HideBounds(Rectangle r) { return ActionBounds(r,3); }
        Rectangle MoreBounds(Rectangle r) { return ActionBounds(r,4); }
        bool IsAction(Rectangle r,Point p) { for(int i=0;i<5;i++) if(ActionBounds(r,i).Contains(p)) return true; return false; }
        readonly ContextMenuStrip actions=new ContextMenuStrip();
        void ShowActions(Entry entry,Rectangle r) {
            actions.Items.Clear();
            actions.Items.Add("更改目录",null,delegate { if(EditEntry!=null) EditEntry(entry); });
            actions.Show(this,new Point(MoreBounds(r).Left,MoreBounds(r).Bottom));
        }
        public int Hit(Point point) {
            int y=point.Y-AutoScrollPosition.Y-gap,x=point.X-gap;
            if(x<0||y<0) return -1;
            int col=x/(cardWidth+gap),row=y/(cardHeight+gap),index=row*columns+col;
            return col<columns&&index<Entries.Count&&BoundsFor(index).Contains(point)?index:-1;
        }
        void SelectEntry(int index) {
            if(index<0||index>=Entries.Count) return;
            SelectedKey=Entries[index].Key; Invalidate(); Focus();
        }
        protected override void OnMouseClick(MouseEventArgs e) {
            base.OnMouseClick(e); if(e.Button!=MouseButtons.Left) return;
            int i=Hit(e.Location); if(i<0) return;
            SelectEntry(i); Rectangle r=BoundsFor(i); Entry entry=Entries[i];
            if(OpenBounds(r).Contains(e.Location)) { if(OpenEntry!=null) OpenEntry(entry); }
            else if(RenameBounds(r).Contains(e.Location)) { if(RenameEntry!=null) RenameEntry(entry); }
            else if(PinBounds(r).Contains(e.Location)) { if(PinEntry!=null) PinEntry(entry); }
            else if(HideBounds(r).Contains(e.Location)) { if(HideEntry!=null) HideEntry(entry); }
            else if(MoreBounds(r).Contains(e.Location)) ShowActions(entry,r);
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) {
            base.OnMouseDoubleClick(e); if(e.Button!=MouseButtons.Left) return;
            int i=Hit(e.Location); if(i<0) return; Rectangle r=BoundsFor(i);
            if(IsAction(r,e.Location)) return;
            SelectEntry(i); if(OpenEntry!=null) OpenEntry(Entries[i]);
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e); int i=Hit(e.Location);
            Entry entry=i<0?null:Entries[i];
            string key=entry==null?null:entry.Key;
            if(key!=tipKey) { tipKey=key; tip.SetToolTip(this,entry==null?"":entry.Title+"\n"+entry.Path+"\n"+entry.Project.CodexDetail); }
            Cursor=i>=0&&IsAction(BoundsFor(i),e.Location)?Cursors.Hand:Cursors.Default;
        }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); tip.Hide(this); Invalidate(); }
        protected override bool IsInputKey(Keys keyData) {
            Keys key=keyData&Keys.KeyCode;
            return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e); int i=Entries.FindIndex(x=>x.Key==SelectedKey);
            if(e.KeyCode==Keys.Enter&&i>=0) { if(OpenEntry!=null) OpenEntry(Entries[i]); e.Handled=true; return; }
            int delta=e.KeyCode==Keys.Left?-1:e.KeyCode==Keys.Right?1:e.KeyCode==Keys.Up?-columns:e.KeyCode==Keys.Down?columns:0;
            if(delta==0||Entries.Count==0) return;
            i=Math.Max(0,Math.Min(Entries.Count-1,i<0?0:i+delta)); SelectEntry(i);
            Rectangle r=BoundsFor(i); int y=-AutoScrollPosition.Y;
            if(r.Top<0) AutoScrollPosition=new Point(0,y+r.Top-gap);
            else if(r.Bottom>ClientSize.Height) AutoScrollPosition=new Point(0,y+r.Bottom-ClientSize.Height+gap);
            e.Handled=true;
        }
        void DrawText(Graphics g,string text,Font font,Rectangle r,Color color,TextFormatFlags flags) {
            TextRenderer.DrawText(g,text??"",font,r,color,flags|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
        }
        void DrawButton(Graphics g,string text,Rectangle r,bool active=false,bool primary=false) {
            Color background=primary?Color.FromArgb(38,118,183):active?Theme(Color.FromArgb(204,215,228),Color.FromArgb(62,76,95)):Theme(Color.FromArgb(46,57,72),Color.FromArgb(230,235,242));
            Color foreground=primary?Color.White:active?Theme(Color.FromArgb(25,36,50),Color.White):Theme(Color.FromArgb(211,224,238),Color.FromArgb(44,57,74));
            using(var brush=new SolidBrush(background)) g.FillRectangle(brush,r);
            TextRenderer.DrawText(g,text,Font,r,foreground,background,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
        }
        int DrawBadge(Graphics g,string text,int x,int y,Color background,Color foreground) {
            int width=TextRenderer.MeasureText(text,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+S(14);
            var bounds=new Rectangle(x,y,width,S(21));
            using(var brush=new SolidBrush(background)) g.FillRectangle(brush,bounds);
            using(var brush=new SolidBrush(foreground)) using(var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center }) g.DrawString(text,Font,brush,bounds,format);
            return x+width+S(6);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); LastPainted=0;
            if(Entries.Count==0) {
                DrawText(e.Graphics,EmptyMessage,Font,new Rectangle(gap,gap,Math.Max(10,ClientSize.Width-2*gap),S(60)),Theme(Color.Silver,Color.FromArgb(85,97,113)),TextFormatFlags.WordBreak); return;
            }
            int first=Math.Max(0,(-AutoScrollPosition.Y-gap)/(cardHeight+gap))*columns;
            int last=Math.Min(Entries.Count,first+((ClientSize.Height/(cardHeight+gap))+3)*columns);
            for(int i=first;i<last;i++) {
                Rectangle r=BoundsFor(i); if(!r.IntersectsWith(e.ClipRectangle)) continue;
                LastPainted++; DrawEntry(e.Graphics,Entries[i],r);
            }
        }
        void DrawEntry(Graphics g,Entry entry,Rectangle r) {
            bool selected=entry.Key==SelectedKey;
            // The surface is neutral. Only selection owns the outline; other states
            // have separate, named locations and never compete for edge colours.
            using(var brush=new SolidBrush(Theme(Color.FromArgb(30,39,52),Color.White))) g.FillRectangle(brush,r);
            using(var pen=new Pen(selected?Theme(Color.FromArgb(82,180,255),Color.FromArgb(0,104,181)):Theme(Color.FromArgb(57,70,87),Color.FromArgb(210,217,226)),selected?S(2):1)) g.DrawRectangle(pen,r.X+1,r.Y+1,r.Width-3,r.Height-3);
            int x=r.X+S(16),w=r.Width-S(32);
            int badgeX=DrawBadge(g,entry.Managed?"CodeTabs 管理中":entry.Opened?"独立窗口已打开":"未打开",x,r.Y+S(10),entry.Managed?Theme(Color.FromArgb(30,83,125),Color.FromArgb(219,238,255)):Theme(Color.FromArgb(44,55,70),Color.FromArgb(237,240,244)),entry.Managed?Theme(Color.FromArgb(161,216,255),Color.FromArgb(0,84,145)):Theme(Color.FromArgb(181,195,211),Color.FromArgb(87,101,119)));
            if(entry.Pinned) badgeX=DrawBadge(g,"↑ 已置顶",badgeX,r.Y+S(10),Theme(Color.FromArgb(204,215,228),Color.FromArgb(62,76,95)),Theme(Color.FromArgb(25,36,50),Color.White));
            if(entry.Hidden) DrawBadge(g,"已隐藏",badgeX,r.Y+S(10),Theme(Color.FromArgb(44,55,70),Color.FromArgb(237,240,244)),Theme(Color.FromArgb(181,195,211),Color.FromArgb(87,101,119)));
            DrawText(g,entry.Title,titleFont??Font,new Rectangle(x,r.Y+S(38),w,S(44)),Theme(Color.White,Color.FromArgb(25,36,50)),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
            Color state=entry.Status=="running"?Theme(Color.FromArgb(103,215,219),Color.FromArgb(0,112,117)):entry.Status=="completed"?Theme(Color.FromArgb(126,218,163),Color.FromArgb(31,117,65)):entry.Status=="interrupted"?Theme(Color.FromArgb(239,182,119),Color.FromArgb(151,76,24)):Theme(Color.FromArgb(171,184,201),Color.FromArgb(85,97,113));
            string label=entry.Status=="running"?"● 正在执行":entry.Status=="completed"?"✓ 已完成":entry.Status=="idle"?"○ 空闲":entry.Status=="interrupted"?"■ 已中断":"○ 状态未知";
            if(entry.Status=="completed") label+=entry.Viewed?" · 已查看":" · 待查看";
            DrawText(g,label,Font,new Rectangle(x,r.Y+S(86),w,S(20)),state,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            DrawText(g,entry.Path??"尚未设置目录",Font,new Rectangle(x,r.Y+S(110),w,S(34)),Theme(Color.FromArgb(157,177,198),Color.FromArgb(81,100,123)),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
            if(entry.Missing) DrawText(g,"! 目录不存在或不可访问",Font,new Rectangle(x,r.Y+S(146),w,S(18)),Theme(Color.FromArgb(239,169,112),Color.FromArgb(151,76,24)),TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            else if(entry.ChangedDirectory) DrawText(g,"自定义目录",Font,new Rectangle(x,r.Y+S(146),w,S(18)),Theme(Color.FromArgb(141,160,181),Color.FromArgb(100,112,128)),TextFormatFlags.SingleLine);
            DrawButton(g,entry.Opened?"切换":"打开",OpenBounds(r),false,true);
            DrawButton(g,"重命名",RenameBounds(r));
            DrawButton(g,entry.Pinned?"已置顶":"置顶",PinBounds(r),entry.Pinned);
            DrawButton(g,entry.Hidden?"显示":"隐藏",HideBounds(r));
            DrawButton(g,"⋯",MoreBounds(r));
        }
        public void TestClick(int index,bool twice) {
            var r=BoundsFor(index); var e=new MouseEventArgs(MouseButtons.Left,1,r.X+20,r.Y+30,0);
            OnMouseClick(e); if(twice) OnMouseDoubleClick(e);
        }
        public bool ButtonsFit() {
            Rectangle r=BoundsFor(0);
            int badges=new[]{"CodeTabs 管理中","↑ 已置顶","已隐藏"}.Sum(t=>TextRenderer.MeasureText(t,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+S(14))+2*S(6);
            return badges<=r.Width-S(32)&&RenameBounds(r).Left>=OpenBounds(r).Right&&PinBounds(r).Left>=RenameBounds(r).Right&&HideBounds(r).Left>=PinBounds(r).Right&&MoreBounds(r).Left>=HideBounds(r).Right&&TextRenderer.MeasureText("已置顶",Font).Width<PinBounds(r).Width&&TextRenderer.MeasureText("隐藏",Font).Width<HideBounds(r).Width;
        }
        public void TestPinClick(int index) {
            var r=PinBounds(BoundsFor(index)); var e=new MouseEventArgs(MouseButtons.Left,1,r.X+r.Width/2,r.Y+r.Height/2,0);
            OnMouseClick(e); OnMouseDoubleClick(e);
        }
        public void TestAction(int index,int action) {
            var r=ActionBounds(BoundsFor(index),action); var e=new MouseEventArgs(MouseButtons.Left,1,r.X+r.Width/2,r.Y+r.Height/2,0);
            OnMouseClick(e); OnMouseDoubleClick(e);
        }
        protected override void Dispose(bool disposing) { if(disposing) { tip.Dispose(); actions.Dispose(); if(titleFont!=null) titleFont.Dispose(); } base.Dispose(disposing); }
    }
    sealed class Snapshot {
        public int Revision;
        public string Warning;
        public List<Hosted> Projects=new List<Hosted>();
        public List<Hosted> Conversations=new List<Hosted>();
        public Dictionary<string,bool> Directories=new Dictionary<string,bool>();
    }
    readonly CardCanvas cards=new CardCanvas();
    readonly Label summary=new Label(),note=new Label(),searchInfo=new Label();
    readonly CheckBox showAll=new CheckBox { AutoSize=true,Text="显示已隐藏",Margin=new Padding(16,10,0,0) };
    readonly ComboBox viewMode=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=240,ForeColor=Color.Black,BackColor=Color.White,Margin=new Padding(12,7,0,0) };
    readonly TextBox search=new TextBox { Dock=DockStyle.Fill };
    readonly ComboBox themeMode=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=110,Margin=new Padding(12,7,0,0) };
    sealed class WindowCatalogue {
        public readonly HashSet<string> Sessions=new HashSet<string>(StringComparer.OrdinalIgnoreCase),Names=new HashSet<string>(StringComparer.OrdinalIgnoreCase),Directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> RemoteTitles=new List<string>();
        public void Clear() { Sessions.Clear(); Names.Clear(); Directories.Clear(); RemoteTitles.Clear(); }
        public bool Contains(Hosted h) {
            if(h.CodexPath!=null&&Sessions.Contains(h.CodexPath)&&(h.OriginalProjectPath==null||h.ProjectPath==h.OriginalProjectPath)) return true;
            string name=String.IsNullOrEmpty(h.ProjectPath)?"":Path.GetFileName(h.ProjectPath.TrimEnd('\\','/'));
            if(IsRemote(h)) return name.Length>0&&!String.IsNullOrEmpty(h.RemoteAlias)&&RemoteTitles.Any(t=>t.IndexOf("SSH: "+h.RemoteAlias,StringComparison.OrdinalIgnoreCase)>=0&&t.IndexOf(name,StringComparison.OrdinalIgnoreCase)>=0);
            return (!String.IsNullOrEmpty(h.ProjectPath)&&Directories.Contains(NormalizeLocalPath(h.ProjectPath).TrimEnd('\\','/')))||(name.Length>0&&Names.Contains(name));
        }
    }
    readonly WindowCatalogue openWindows=new WindowCatalogue(),managedWindows=new WindowCatalogue();
    readonly Dictionary<string,string> sessionDirectories=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    void UpdateWindowCatalogue(WindowCatalogue catalogue,IEnumerable<Hosted> windows) {
        catalogue.Clear();
        foreach(var window in windows.Where(h=>h.IsVSCode&&Native.IsWindow(h.Handle))) {
            if(!String.IsNullOrEmpty(window.CodexPath)) catalogue.Sessions.Add(window.CodexPath);
            string title=Native.Title(window.Handle);
            if(title.IndexOf("SSH:",StringComparison.OrdinalIgnoreCase)>=0) { catalogue.RemoteTitles.Add(title); continue; }
            if(!String.IsNullOrEmpty(window.CodexPath)) {
                string directory;
                if(!sessionDirectories.TryGetValue(window.CodexPath,out directory)) {
                    try { directory=NormalizeLocalPath(CodexState.ProjectDirectory(window.CodexPath)); sessionDirectories[window.CodexPath]=directory; } catch(Exception) { directory=null; }
                }
                if(!String.IsNullOrEmpty(directory)) { catalogue.Directories.Add(directory.TrimEnd('\\','/')); continue; }
            }
            string key=BindingStore.Key(window.ApplicationName,title); catalogue.Names.Add(key.Substring(key.IndexOf('|')+1));
        }
    }
    public void SetOpenWindows(IEnumerable<Hosted> managed,IEnumerable<Hosted> external) {
        var owned=managed.ToList();
        UpdateWindowCatalogue(managedWindows,owned);
        UpdateWindowCatalogue(openWindows,owned.Concat(external));
    }
    readonly Timer searchDelay=new Timer { Interval=150 };
    readonly Dictionary<Hosted,SearchRecord> searchIndex=new Dictionary<Hosted,SearchRecord>();
    string appliedQuery="";
    readonly CodexState monitor=new CodexState();
    readonly BindingStore associations=new BindingStore();
    readonly BindingStore seen=new BindingStore(PortableConfig.FilePath("codex-viewed.json"));
    readonly BindingStore hiddenStore,directoryStore,manualStore,pinStore,titleStore,viewStore;
    Dictionary<string,string> viewed=new Dictionary<string,string>(),hidden=new Dictionary<string,string>(),pinned=new Dictionary<string,string>(),titles=new Dictionary<string,string>();
    Dictionary<string,bool> directories=new Dictionary<string,bool>();
    List<Hosted> projects=new List<Hosted>(); Task<Snapshot> discovery;
    List<Hosted> directoryProjects=new List<Hosted>(),conversationProjects=new List<Hosted>();
    List<Hosted> monitoredProjects=new List<Hosted>();
    public Action<Hosted,string> TaskCompleted;
    public string DisplayName(Hosted project) { string title; return titles.TryGetValue(ProjectKey(project),out title)?title:ProjectName(project); }
    DateTime nextScan,nextPaint; string discoveryError=""; int directoryRevision;
    public Action<Hosted> OpenProject;
    static string DataFile(string name) { return PortableConfig.FilePath(name); }
    public Overview(string testDirectory=null) {
        monitor.TaskCompleted=delegate(Hosted project,string token) { if(TaskCompleted!=null) TaskCompleted(project,token); };
        hiddenStore=new BindingStore(testDirectory==null?DataFile("codex-hidden.json"):Path.Combine(testDirectory,"hidden.json"));
        directoryStore=new BindingStore(testDirectory==null?DataFile("codex-directories.json"):Path.Combine(testDirectory,"directories.json"));
        manualStore=new BindingStore(testDirectory==null?DataFile("codex-projects.json"):Path.Combine(testDirectory,"projects.json"));
        pinStore=new BindingStore(testDirectory==null?DataFile("codex-pinned.json"):Path.Combine(testDirectory,"pinned.json"));
        titleStore=new BindingStore(testDirectory==null?DataFile("codex-titles.json"):Path.Combine(testDirectory,"titles.json"));
        viewStore=new BindingStore(testDirectory==null?DataFile("overview-settings.json"):Path.Combine(testDirectory,"overview-settings.json"));
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);
        Dock=DockStyle.Fill; BackColor=Color.FromArgb(22,27,35); ForeColor=Color.White; Font=new Font("Microsoft YaHei UI",9);
        summary.Dock=DockStyle.Bottom; summary.Height=26; summary.Font=Font; summary.TextAlign=ContentAlignment.MiddleLeft; summary.Padding=new Padding(18,0,0,0); summary.Text="Codex 项目总览";
        note.Dock=DockStyle.Bottom; note.Height=24; note.Padding=new Padding(18,2,18,0); note.Visible=false;
        note.Text="Ctrl+F 检索 · Esc 清空 · 检索含已隐藏对话";
        var toolbar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=50,Padding=new Padding(14,0,0,0),WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink };
        var refresh=ToolbarButton("刷新项目"); refresh.Click+=delegate { nextScan=DateTime.MinValue; };
        var add=ToolbarButton("添加目录"); add.Click+=delegate { AddDirectory(); };
        showAll.CheckedChanged+=delegate { RenderCards(); };
        search.TextChanged+=delegate { searchDelay.Stop(); if(String.IsNullOrWhiteSpace(search.Text)) ApplySearch(); else searchDelay.Start(); };
        searchDelay.Tick+=delegate { ApplySearch(); };
        search.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { ApplySearch(); cards.Focus(); e.SuppressKeyPress=true; } };
        toolbar.Controls.Add(refresh); toolbar.Controls.Add(add); toolbar.Controls.Add(showAll);
        viewMode.Items.AddRange(new object[]{"按项目目录","独立对话（全量）"});
        viewMode.DrawMode=DrawMode.OwnerDrawFixed; viewMode.ItemHeight=Font.Height+4; viewMode.AccessibleName="项目展示方式";
        viewMode.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
            e.DrawBackground(); int index=e.Index<0?viewMode.SelectedIndex:e.Index;
            if(index>=0) TextRenderer.DrawText(e.Graphics,viewMode.Items[index].ToString(),viewMode.Font,e.Bounds,viewMode.ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
        };
        int mode=1; try { string saved; if(viewStore.Read().TryGetValue("view",out saved)&&saved=="directories") mode=0; } catch(Exception) {}
        viewMode.SelectedIndex=mode;
        viewMode.SelectedIndexChanged+=delegate {
            projects=viewMode.SelectedIndex==1?conversationProjects:directoryProjects; searchIndex.Clear(); RenderCards(); nextPaint=DateTime.MinValue; cards.AutoScrollPosition=Point.Empty;
            try { viewStore.Save("view",viewMode.SelectedIndex==1?"conversations":"directories"); } catch(Exception ex) { MessageBox.Show(this,"视图设置保存失败："+ex.Message); }
        };
        toolbar.Controls.Add(viewMode);
        themeMode.Items.AddRange(new object[]{"黑色主题","白色主题"});
        string savedTheme=null; try { viewStore.Read().TryGetValue("theme",out savedTheme); } catch(Exception) {}
        themeMode.SelectedIndex=savedTheme=="light"?1:0;
        themeMode.SelectedIndexChanged+=delegate { ApplyTheme(themeMode.SelectedIndex==1); try { viewStore.Save("theme",themeMode.SelectedIndex==1?"light":"dark"); } catch(Exception ex) { MessageBox.Show(this,"主题保存失败："+ex.Message); } };
        // Keep theme and search together even when the toolbar wraps on narrow windows.
        var searchBar=new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=Padding.Empty };
        var searchLabel=new Label { Text="检索卡片",AutoSize=true,Margin=new Padding(8,10,4,0),TextAlign=ContentAlignment.MiddleLeft };
        var clear=ToolbarButton("清空"); clear.Padding=new Padding(6,2,6,2); clear.Margin=new Padding(0,5,0,0);
        search.Dock=DockStyle.None; search.Width=160; search.Margin=new Padding(0,7,4,0); search.AccessibleName="检索全部对话";
        clear.Click+=delegate { search.Clear(); search.Focus(); };
        searchBar.Controls.Add(themeMode); searchBar.Controls.Add(searchLabel); searchBar.Controls.Add(search); searchBar.Controls.Add(clear);
        toolbar.Controls.Add(searchBar);
        searchInfo.Dock=DockStyle.Bottom; searchInfo.Height=24; searchInfo.Padding=new Padding(18,2,0,0); searchInfo.ForeColor=Color.LightSteelBlue;
        cards.Dock=DockStyle.Fill; cards.Font=Font;
        cards.OpenEntry=e=> { if(OpenProject!=null) OpenProject(e.Project); };
        cards.HideEntry=e=>SetHidden(e.Project,!e.Hidden);
        cards.EditEntry=e=>EditDirectory(e.Project);
        cards.PinEntry=e=>SetPinned(e.Project,!e.Pinned);
        cards.RenameEntry=e=>RenameProject(e.Project);
        Controls.Add(cards); Controls.Add(searchInfo); Controls.Add(toolbar); Controls.Add(summary); Controls.Add(note);
        try { viewed=seen.Read(); hidden=hiddenStore.Read(); pinned=pinStore.Read(); titles=titleStore.Read(); } catch(Exception) { discoveryError="本地记录读取失败"; }
        ApplyTheme(themeMode.SelectedIndex==1);
    }
    void ApplyTheme(bool light) {
        Color background=light?Color.FromArgb(243,245,248):Color.FromArgb(22,27,35),foreground=light?Color.FromArgb(38,49,64):Color.FromArgb(220,229,240);
        Action<Control> apply=null; apply=control=> {
            control.BackColor=background; control.ForeColor=foreground;
            if(control is Button||control is TextBox||control is ComboBox) control.BackColor=light?Color.White:Color.FromArgb(43,54,69);
            foreach(Control child in control.Controls) apply(child);
        };
        apply(this); cards.Light=light;
        summary.ForeColor=searchInfo.ForeColor=note.ForeColor=light?Color.FromArgb(100,112,128):Color.FromArgb(144,159,178);
        cards.Invalidate();
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
        if(keyData==(Keys.Control|Keys.F)) { search.Focus(); search.SelectAll(); return true; }
        if(keyData==Keys.Escape&&!String.IsNullOrEmpty(search.Text)) { search.Clear(); return true; }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    void ApplySearch() { searchDelay.Stop(); appliedQuery=search.Text.Trim(); RenderCards(); cards.AutoScrollPosition=Point.Empty; }
    Button ToolbarButton(string text) { return new Button { Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(12,5,12,5),Margin=new Padding(4,2,4,2),ForeColor=Color.White,BackColor=Color.FromArgb(43,59,78),FlatStyle=FlatStyle.Flat }; }
    static string DirectoryKey(string path) { return "dir|"+path.TrimEnd('\\','/').Replace('/','\\').ToLowerInvariant(); }
    static string CatalogueKey(string host,string path) { return String.IsNullOrEmpty(host)?DirectoryKey(path):"remote|"+host+"|"+path.TrimEnd('/'); }
    public static bool IsRemote(Hosted h) { return h.RemoteHostId!=null&&(h.OriginalProjectPath==null||h.OriginalProjectPath==h.ProjectPath); }
    static string ProjectKey(Hosted h) { return h.OverviewKey??(!String.IsNullOrWhiteSpace(h.ProjectPath)?CatalogueKey(h.RemoteHostId,h.ProjectPath):"ref|"+(h.BindingKey??h.CodexPath??"unknown").ToLowerInvariant()); }
    static void ApplyDirectory(Hosted h,Dictionary<string,string> map) {
        h.OverviewKey=ProjectKey(h); h.OriginalProjectPath=h.ProjectPath;
        string replacement;
        if(map.TryGetValue(h.OverviewKey,out replacement)||(!String.IsNullOrEmpty(h.ConversationId)&&!String.IsNullOrWhiteSpace(h.ProjectPath)&&map.TryGetValue(CatalogueKey(h.RemoteHostId,h.ProjectPath),out replacement))) h.ProjectPath=replacement;
        if(!IsRemote(h)) h.ProjectPath=NormalizeLocalPath(h.ProjectPath);
        if(String.IsNullOrEmpty(h.RemoteHostId)) h.OriginalProjectPath=NormalizeLocalPath(h.OriginalProjectPath);
    }
    public static string NormalizeLocalPath(string path) {
        if(String.IsNullOrEmpty(path)) return path;
        string value=path.Replace('/','\\');
        if(value.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)) return @"\\"+value.Substring(8);
        if(value.StartsWith(@"\\?\",StringComparison.Ordinal)&&value.Length>=7&&Char.IsLetter(value[4])&&value[5]==':'&&value[6]=='\\') return value.Substring(4);
        return path;
    }
    static List<Hosted> BuildConversations(List<CodexSession> sessions,List<LocalProject> indexed) {
        var byId=new Dictionary<string,Hosted>(StringComparer.Ordinal);
        foreach(var session in sessions) {
            string key="thread|local|"+(session.Id??session.Path);
            byId[key]=new Hosted { IsVSCode=true,ApplicationName="Code",OverviewKey=key,ConversationId=session.Id,ProjectPath=session.Cwd,CodexPath=session.Path };
        }
        foreach(var item in indexed.Where(p=>!p.Registered&&!String.IsNullOrWhiteSpace(p.Id))) {
            string key="thread|"+(item.HostId??"local")+"|"+item.Id; Hosted h;
            if(!byId.TryGetValue(key,out h)) {
                h=new Hosted { IsVSCode=true,ApplicationName="Code",OverviewKey=key,ConversationId=item.Id,ProjectPath=item.Path,RemoteHostId=item.HostId,RemoteKind=item.HostKind,RemoteAlias=item.Alias,CodexDetail="只有会话索引，无法确认实时任务状态" }; byId[key]=h;
            }
            if(String.IsNullOrWhiteSpace(h.OverviewTitle)&&!String.IsNullOrWhiteSpace(item.Name)) h.OverviewTitle=item.Name;
            if(String.IsNullOrEmpty(h.CodexPath)&&!String.IsNullOrWhiteSpace(item.SessionPath)&&File.Exists(item.SessionPath)) h.CodexPath=item.SessionPath;
        }
        return byId.Values.ToList();
    }
    Snapshot Scan(int revision) {
        var result=new Snapshot { Revision=revision }; var rows=result.Projects; var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var binding in associations.Read()) {
            string cwd=null; try { cwd=CodexState.ProjectDirectory(binding.Value); } catch(Exception) {}
            if(!String.IsNullOrEmpty(cwd)&&!paths.Add(DirectoryKey(cwd))) continue;
            rows.Add(new Hosted { IsVSCode=true,ApplicationName=binding.Key.Split('|')[0],BindingKey=binding.Key,ProjectPath=cwd,CodexPath=binding.Value });
        }
        var sessions=CodexState.Discover(true);
        foreach(var session in sessions)
            if(!String.IsNullOrWhiteSpace(session.Cwd)&&paths.Add(DirectoryKey(session.Cwd)))
                rows.Add(new Hosted { IsVSCode=true,ApplicationName="Code",ProjectPath=session.Cwd,CodexPath=session.Path });
        string warning; var indexed=LocalProjects.Discover(Path.GetDirectoryName(CodexState.Root),out warning); result.Warning=warning;
        MergeIndexedProjects(rows,indexed);
        paths=new HashSet<string>(rows.Where(h=>!String.IsNullOrWhiteSpace(h.ProjectPath)).Select(h=>DirectoryKey(h.ProjectPath)),StringComparer.OrdinalIgnoreCase);
        foreach(var item in manualStore.Read()) if(paths.Add(DirectoryKey(item.Value)))
            rows.Add(new Hosted { IsVSCode=true,ApplicationName="Code",ProjectPath=item.Value,CodexStatus="idle",CodexDetail="手动添加，尚无本机会话" });
        var replacements=directoryStore.Read();
        result.Conversations=BuildConversations(sessions,indexed);
        var checkedPaths=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        foreach(var h in rows.Concat(result.Conversations)) {
            ApplyDirectory(h,replacements);
            if(!IsRemote(h)&&!String.IsNullOrWhiteSpace(h.ProjectPath)) {
                bool exists; if(!checkedPaths.TryGetValue(h.ProjectPath,out exists)) { exists=Directory.Exists(h.ProjectPath); checkedPaths[h.ProjectPath]=exists; }
                result.Directories[ProjectKey(h)]=exists;
            }
        }
        return result;
    }
    static void MergeIndexedProjects(List<Hosted> rows,List<LocalProject> indexed) {
        var byDirectory=rows.Where(h=>!String.IsNullOrWhiteSpace(h.ProjectPath)).GroupBy(h=>CatalogueKey(h.RemoteHostId,h.ProjectPath)).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
        foreach(var item in indexed.Where(p=>!p.Registered&&!String.IsNullOrWhiteSpace(p.Path))) {
            string key=CatalogueKey(item.HostId,item.Path); Hosted h;
            if(!byDirectory.TryGetValue(key,out h)) {
                h=new Hosted { IsVSCode=true,ApplicationName="Code",ProjectPath=item.Path,RemoteHostId=item.HostId,RemoteKind=item.HostKind,RemoteAlias=item.Alias,CodexDetail="仅有本地索引记录，无法从日志确认任务状态" };
                byDirectory[key]=h; rows.Add(h);
            }
            if(String.IsNullOrEmpty(h.CodexPath)&&!String.IsNullOrWhiteSpace(item.SessionPath)&&File.Exists(item.SessionPath)) h.CodexPath=item.SessionPath;
        }
        var registered=indexed.Where(p=>p.Registered).GroupBy(p=>(p.HostId??"local")+"|"+p.Id).Select(g=>g.ToList()).ToList();
        var primaryCounts=registered.Where(g=>!String.IsNullOrWhiteSpace(g[0].Path)).GroupBy(g=>CatalogueKey(g[0].HostId,g[0].Path)).ToDictionary(g=>g.Key,g=>g.Count());
        foreach(var group in registered) {
            var item=group[0]; Hosted old=null;
            string key=String.IsNullOrWhiteSpace(item.Path)?null:CatalogueKey(item.HostId,item.Path);
            if(key!=null) byDirectory.TryGetValue(key,out old);
            var h=new Hosted {
                IsVSCode=true,ApplicationName="Code",OverviewTitle=item.Name,ProjectPath=item.Path,RemoteHostId=item.HostId,RemoteKind=item.HostKind,RemoteAlias=item.Alias,
                OverviewKey=key!=null&&primaryCounts[key]==1?key:"project|"+(item.HostId??"local")+"|"+item.Id,
                ProjectSearchText=String.Join("\n",group.SelectMany(p=>new[]{p.Name,p.Path})),
                BindingKey=old==null?null:old.BindingKey,CodexPath=old==null?null:old.CodexPath,
                CodexDetail=old==null?"已登记项目，尚无可读取的会话日志":old.CodexDetail
            };
            // One card per registered independent project, including rootless projects.
            // Multiple registered projects may intentionally share a directory.
            foreach(var root in group.Where(p=>!String.IsNullOrWhiteSpace(p.Path))) {
                Hosted directoryCard;
                if(byDirectory.TryGetValue(CatalogueKey(root.HostId,root.Path),out directoryCard)) rows.Remove(directoryCard);
            }
            rows.Add(h);
        }
    }
    public void TickOverview() {
        if(IsDisposed) return;
        if(discovery!=null && discovery.IsCompleted) {
            if(discovery.Status==TaskStatus.RanToCompletion && discovery.Result.Revision==directoryRevision) {
                var previous=monitoredProjects.Where(h=>h.CodexPath!=null).GroupBy(h=>h.CodexPath,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
                directoryProjects=discovery.Result.Projects; conversationProjects=discovery.Result.Conversations;
                monitoredProjects=directoryProjects.Concat(conversationProjects).ToList();
                projects=viewMode.SelectedIndex==1?conversationProjects:directoryProjects; directories=discovery.Result.Directories; searchIndex.Clear();
                foreach(var h in monitoredProjects) { Hosted old; if(h.CodexPath!=null&&previous.TryGetValue(h.CodexPath,out old)) { h.CodexStatus=old.CodexStatus; h.CodexDetail=old.CodexDetail; h.CompletionToken=old.CompletionToken; } }
                discoveryError=String.IsNullOrEmpty(discovery.Result.Warning)?"":"部分本地索引暂不可读，当前结果可能不完整";
            } else if(discovery.Status!=TaskStatus.RanToCompletion) { discoveryError="项目扫描失败，请点击刷新重试"; var ignored=discovery.Exception; }
            else nextScan=DateTime.MinValue;
            discovery=null;
        }
        if(discovery==null && DateTime.UtcNow>=nextScan) { nextScan=DateTime.UtcNow.AddSeconds(30); int revision=directoryRevision; discovery=Task.Run(()=>Scan(revision)); }
        monitor.Refresh(monitoredProjects);
        if(DateTime.UtcNow<nextPaint) return; nextPaint=DateTime.UtcNow.AddSeconds(1);
        int running=projects.Count(h=>h.CodexStatus=="running"),unread=projects.Count(h=>h.CodexStatus=="completed"&&!IsViewed(h));
        summary.Text=String.IsNullOrEmpty(discoveryError)?String.Format("Codex 总览  ·  {0} 张卡片  ·  {1} 执行中  ·  {2} 完成后未打开",projects.Count,running,unread):discoveryError;
        if(Visible) RenderCards();
    }
    static bool SameEntries(List<Entry> first,List<Entry> second) {
        if(first.Count!=second.Count) return false;
        for(int i=0;i<first.Count;i++) {
            var a=first[i]; var b=second[i];
            if(a.Key!=b.Key||a.Title!=b.Title||a.Path!=b.Path||a.Status!=b.Status||a.Hidden!=b.Hidden||a.Pinned!=b.Pinned||a.Opened!=b.Opened||a.Managed!=b.Managed||a.Missing!=b.Missing||a.Viewed!=b.Viewed||a.ChangedDirectory!=b.ChangedDirectory) return false;
        }
        return true;
    }
    void RenderCards() {
        string[] terms=appliedQuery.Replace('/','\\').Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
        var entries=new List<Entry>(); int hiddenTotal=0,hiddenMatches=0;
        foreach(var h in projects) {
            SearchRecord record;
            if(!searchIndex.TryGetValue(h,out record)) {
                string title; if(!titles.TryGetValue(ProjectKey(h),out title)) title=ProjectName(h);
                record=new SearchRecord { Key=ProjectKey(h),Title=title,Text=String.Join("\n",title,ProjectName(h),h.ProjectPath,h.OriginalProjectPath,h.ProjectSearchText,h.RemoteAlias,h.RemoteHostId).Replace('/','\\') };
                searchIndex[h]=record;
            }
            string key=record.Key; bool isHidden=hidden.ContainsKey(key),exists;
            if(isHidden) hiddenTotal++;
            // A non-empty search ALWAYS searches the full catalogue, including hidden cards.
            if(terms.Length==0&&isHidden&&!showAll.Checked) continue;
            bool match=true; foreach(string term in terms) if(record.Text.IndexOf(term,StringComparison.OrdinalIgnoreCase)<0) { match=false; break; }
            if(!match) continue;
            if(isHidden) hiddenMatches++;
            entries.Add(new Entry { Project=h,Key=key,Title=record.Title,Path=IsRemote(h)?(h.RemoteAlias??h.RemoteHostId)+" · "+h.ProjectPath:h.ProjectPath,Status=h.CodexStatus,Hidden=isHidden,Pinned=pinned.ContainsKey(key),Opened=openWindows.Contains(h),Managed=managedWindows.Contains(h),Missing=directories.TryGetValue(key,out exists)&&!exists,Viewed=IsViewed(h),ChangedDirectory=h.OriginalProjectPath!=null&&!String.Equals(h.OriginalProjectPath,h.ProjectPath,StringComparison.OrdinalIgnoreCase) });
        }
        // Only the typically small pinned set needs sorting; the rest stays in scan order.
        entries=entries.Where(e=>e.Pinned).OrderBy(e=>pinned[e.Key],StringComparer.Ordinal).Concat(entries.Where(e=>!e.Pinned)).ToList();
        showAll.Text=String.Format("显示已隐藏（{0}）",hiddenTotal);
        searchInfo.Text=terms.Length==0?String.Format("显示 {0} / {1} 个项目 · 输入关键词可检索全部，包括已隐藏项目",entries.Count,projects.Count):String.Format("匹配 {0} / {1} 个项目 · 包括 {2} 个已隐藏项目",entries.Count,projects.Count,hiddenMatches);
        cards.EmptyMessage=terms.Length>0?"没有匹配的项目。可换一个关键词，或清空筛选。":"暂无可显示的项目。可刷新、添加目录，或勾选显示已隐藏。";
        if(!SameEntries(entries,cards.Entries)) cards.UpdateEntries(entries);
        else { cards.Entries=entries; if(entries.Count==0) cards.Invalidate(); }
    }
    void SetHidden(Hosted h,bool value) {
        string key=ProjectKey(h);
        try { hiddenStore.Save(key,value?"hidden":null); if(value) hidden[key]="hidden"; else hidden.Remove(key); RenderCards(); }
        catch(Exception ex) { MessageBox.Show(this,"隐藏记录保存失败："+ex.Message,"CodeTabs"); }
    }
    void SetPinned(Hosted h,bool value) {
        string key=ProjectKey(h);
        string order=value?NextPinOrder():null;
        try {
            pinStore.Save(key,order);
            if(value) pinned[key]=order; else pinned.Remove(key);
            RenderCards(); if(value) cards.AutoScrollPosition=Point.Empty;
        } catch(Exception ex) { MessageBox.Show(this,"置顶记录保存失败："+ex.Message,"CodeTabs"); }
    }
    string NextPinOrder() {
        long order=DateTime.UtcNow.Ticks;
        foreach(string value in pinned.Values) { long saved; if(Int64.TryParse(value,out saved)&&saved>=order&&saved<Int64.MaxValue) order=saved+1; }
        return order.ToString("D19",System.Globalization.CultureInfo.InvariantCulture);
    }
    void AddDirectory() {
        using(var picker=new FolderBrowserDialog { Description="添加一个项目目录到 CodeTabs",ShowNewFolderButton=false }) {
            if(picker.ShowDialog(this)!=DialogResult.OK) return;
            try { manualStore.Save(DirectoryKey(picker.SelectedPath),picker.SelectedPath); nextScan=DateTime.MinValue; }
            catch(Exception ex) { MessageBox.Show(this,"添加失败："+ex.Message,"CodeTabs"); }
        }
    }
    void SaveTitle(Hosted h,string title) {
        string key=ProjectKey(h); title=String.IsNullOrWhiteSpace(title)?null:title.Trim();
        titleStore.Save(key,title);
        if(title==null) titles.Remove(key); else titles[key]=title;
        searchIndex.Remove(h); RenderCards();
    }
    void RenameProject(Hosted h) {
        string current; if(!titles.TryGetValue(ProjectKey(h),out current)) current=ProjectName(h);
        using(var dialog=new Form { Text="重命名卡片",Size=new Size(600,210),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,Font=Font,AutoScaleMode=AutoScaleMode.Font }) {
            var info=new Label { Dock=DockStyle.Top,Height=42,Padding=new Padding(12,10,0,0),Text="设置卡片显示名称，不更改项目目录或 Codex 对话标题。" };
            var input=new TextBox { Dock=DockStyle.Fill,Text=current,MaxLength=200 };
            var row=new Panel { Dock=DockStyle.Top,Height=40,Padding=new Padding(12,4,12,4) }; row.Controls.Add(input);
            var bar=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=46,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(6) };
            var cancel=new Button { Text="取消",AutoSize=true,DialogResult=DialogResult.Cancel }; var save=new Button { Text="保存",AutoSize=true }; var reset=new Button { Text="恢复原标题",AutoSize=true };
            save.Click+=delegate { if(String.IsNullOrWhiteSpace(input.Text)) { MessageBox.Show(dialog,"请输入标题，或选择恢复原标题。"); return; } try { SaveTitle(h,input.Text); dialog.DialogResult=DialogResult.OK; } catch(Exception ex) { MessageBox.Show(dialog,ex.Message); } };
            reset.Click+=delegate { try { SaveTitle(h,null); dialog.DialogResult=DialogResult.OK; } catch(Exception ex) { MessageBox.Show(dialog,ex.Message); } };
            bar.Controls.Add(cancel); bar.Controls.Add(save); bar.Controls.Add(reset);
            dialog.Controls.Add(row); dialog.Controls.Add(info); dialog.Controls.Add(bar); dialog.AcceptButton=save; dialog.CancelButton=cancel;
            dialog.Shown+=delegate { input.Focus(); input.SelectAll(); }; dialog.ShowDialog(this);
        }
    }
    void SaveDirectory(Hosted h,string path) {
        string key=ProjectKey(h);
        directoryStore.Save(key,path);
        directoryRevision++;
        h.OverviewKey=key;
        if(h.OriginalProjectPath==null) h.OriginalProjectPath=h.ProjectPath;
        h.ProjectPath=path??h.OriginalProjectPath;
        directories.Remove(key); searchIndex.Remove(h); nextScan=DateTime.MinValue; RenderCards();
    }
    void EditDirectory(Hosted h) {
        using(var dialog=new Form { Text="更改项目目录 · 仅用于 CodeTabs",Width=660,Height=245,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false,FormBorderStyle=FormBorderStyle.FixedDialog,AutoScaleMode=AutoScaleMode.Font,Font=Font }) {
            var explanation=new Label { Dock=DockStyle.Top,Height=50,Padding=new Padding(12),Text="只修改 CodeTabs 打开此项目的位置，不移动文件、不修改 Codex 会话或配置。" };
            var input=new TextBox { Dock=DockStyle.Fill,Text=h.ProjectPath??"" };
            var browse=new Button { Dock=DockStyle.Right,Width=100,Text="浏览…" };
            var row=new Panel { Dock=DockStyle.Top,Height=40,Padding=new Padding(12,4,12,4) }; row.Controls.Add(input); row.Controls.Add(browse);
            browse.Click+=delegate { using(var picker=new FolderBrowserDialog { Description="选择项目的新目录",ShowNewFolderButton=false }) { if(picker.ShowDialog(dialog)==DialogResult.OK) input.Text=picker.SelectedPath; } };
            var buttons=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(6),WrapContents=false };
            var save=new Button { Text="保存",AutoSize=true }; var cancel=new Button { Text="取消",AutoSize=true,DialogResult=DialogResult.Cancel }; var reset=new Button { Text="恢复原目录",AutoSize=true };
            save.Click+=delegate {
                string path=NormalizeLocalPath(input.Text.Trim().Trim('"'));
                if(!Path.IsPathRooted(path)||!Directory.Exists(path)) { MessageBox.Show(dialog,"请选择存在且可访问的完整目录路径。"); return; }
                try { SaveDirectory(h,Path.GetFullPath(path)); dialog.DialogResult=DialogResult.OK; } catch(Exception ex) { MessageBox.Show(dialog,ex.Message); }
            };
            reset.Click+=delegate { try { SaveDirectory(h,null); dialog.DialogResult=DialogResult.OK; } catch(Exception ex) { MessageBox.Show(dialog,ex.Message); } };
            buttons.Controls.Add(cancel); buttons.Controls.Add(save); buttons.Controls.Add(reset);
            dialog.Controls.Add(row); dialog.Controls.Add(explanation); dialog.Controls.Add(buttons); dialog.AcceptButton=save; dialog.CancelButton=cancel; dialog.ShowDialog(this);
        }
    }
    public static string ProjectName(Hosted h) { if(!String.IsNullOrWhiteSpace(h.OverviewTitle)) return h.OverviewTitle; if(!String.IsNullOrEmpty(h.BindingKey)) return h.BindingKey.Substring(h.BindingKey.IndexOf('|')+1); string path=h.OriginalProjectPath??h.ProjectPath; if(!String.IsNullOrEmpty(path)) return Path.GetFileName(path.TrimEnd('\\','/')); return "未命名项目"; }
    bool IsViewed(Hosted h) { string token; return !String.IsNullOrEmpty(h.CompletionToken)&&viewed.TryGetValue(h.CodexPath??"",out token)&&token==h.CompletionToken; }
    public void MarkViewed(Hosted source) {
        var h=projects.Find(x=>String.Equals(x.CodexPath,source.CodexPath,StringComparison.OrdinalIgnoreCase))??source;
        if(h.CodexStatus!="completed"||String.IsNullOrEmpty(h.CompletionToken)||IsViewed(h)) return;
        try { seen.Save(h.CodexPath,h.CompletionToken); viewed[h.CodexPath]=h.CompletionToken; nextPaint=DateTime.MinValue; }
        catch(Exception) { summary.Text="查看标记保存失败，下次可重新打开项目重试"; }
    }
    protected override void Dispose(bool disposing) { if(disposing) { monitor.Dispose(); searchDelay.Dispose(); } base.Dispose(disposing); }
    public static void Verify(string screenshot=null) {
        VerifyWindowMembership();
        string local=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        var prefixed=new Hosted { ProjectPath=@"\\?\"+local };
        string originalKey=ProjectKey(prefixed); ApplyDirectory(prefixed,new Dictionary<string,string>());
        if(prefixed.ProjectPath!=local||!Directory.Exists(prefixed.ProjectPath)||ProjectKey(prefixed)!=originalKey||prefixed.ProjectPath!=prefixed.OriginalProjectPath) throw new Exception("Extended path normalization or preference identity failed");
        if(NormalizeLocalPath(@"\\?\UNC\server\share\folder")!=@"\\server\share\folder"||NormalizeLocalPath(@"\\server\share")!=@"\\server\share"||NormalizeLocalPath("//?/D:/folder")!=@"D:\folder") throw new Exception("UNC / slash path normalization failed");
        var remotePath=new Hosted { ProjectPath="//server/project",RemoteHostId="remote" }; ApplyDirectory(remotePath,new Dictionary<string,string>());
        if(remotePath.ProjectPath!="//server/project"||!IsRemote(remotePath)) throw new Exception("Remote path changed");
        var separate=BuildConversations(new List<CodexSession> { new CodexSession { Id="a",Cwd=local },new CodexSession { Id="b",Cwd=local } },new List<LocalProject>());
        if(separate.Count!=2||separate.Select(ProjectKey).Distinct().Count()!=2) throw new Exception("Conversations sharing a directory merged");
        string testDir=screenshot==null?null:Path.GetDirectoryName(screenshot);
        using(var form=new Form { Width=1200,Height=820,Text="Codex 总览测试" }) using(var view=new Overview(testDir)) {
            view.hidden.Clear(); view.pinned.Clear(); view.titles.Clear(); view.nextScan=DateTime.MaxValue;
            var running=new Hosted { ProjectPath=@"D:\Projects\这是一个用于验证标题自动换行与操作按钮不会遮挡的长项目名称",CodexStatus="running" };
            var done=new Hosted { ProjectPath=@"D:\Projects\刚完成的项目",CodexStatus="completed",CodexPath="test-session",CompletionToken="turn-1" };
            var idle=new Hosted { ProjectPath=@"D:\Projects\已迁移的旧项目",CodexStatus="idle" };
            view.projects=new List<Hosted> { running,done,idle }; view.directories[ProjectKey(idle)]=false;
            form.Controls.Add(view); form.Show(); Application.DoEvents(); view.TickOverview();
            if(view.cards.Entries.Count!=3||!view.cards.Entries[2].Missing||!view.cards.ButtonsFit()) throw new Exception("Card layout or missing state failed");
            Hosted clicked=null; view.OpenProject=h=>clicked=h;
            view.cards.TestClick(0,false); if(clicked!=null||view.cards.SelectedKey!=ProjectKey(running)) throw new Exception("Single click must select");
            view.cards.TestClick(0,true); if(clicked!=running) throw new Exception("Double click must open");
            int openCount=0; view.OpenProject=h=> { clicked=h; openCount++; };
            view.cards.TestAction(0,0); if(clicked!=running||openCount!=1) throw new Exception("Open button must open exactly once");
            var renameAction=view.cards.RenameEntry; Hosted renameTarget=null; view.cards.RenameEntry=e=>renameTarget=e.Project;
            view.cards.TestAction(0,1); if(renameTarget!=running||openCount!=1) throw new Exception("Rename button must not open project"); view.cards.RenameEntry=renameAction;
            var hideAction=view.cards.HideEntry; int hideCount=0; view.cards.HideEntry=e=> { if(e.Project==running) hideCount++; };
            view.cards.TestAction(0,3); if(hideCount!=1||openCount!=1) throw new Exception("Direct hide button routing failed"); view.cards.HideEntry=hideAction;
            if(testDir!=null) {
                PortableConfig.Verify(testDir);
                using(var defaults=new Overview(Path.Combine(testDir,"defaults-"+Guid.NewGuid().ToString("N")))) if(defaults.viewMode.SelectedIndex!=1) throw new Exception("Conversation view is not the default");
                view.themeMode.SelectedIndex=1;
                using(var restored=new Overview(testDir)) if(restored.themeMode.SelectedIndex!=1) throw new Exception("Theme preference not restored");
                view.themeMode.SelectedIndex=0;
                view.SaveTitle(running,"重点 · "+ProjectName(running));
                using(var restored=new Overview(testDir)) if(restored.titles[ProjectKey(running)]!=view.titles[ProjectKey(running)]) throw new Exception("Renamed title not persisted");
                view.search.Text="重点"; view.ApplySearch(); if(view.cards.Entries.Count!=1) throw new Exception("Renamed title not searchable");
                view.SaveTitle(running,null); if(view.titles.ContainsKey(ProjectKey(running))) throw new Exception("Reset title failed");
                view.SaveTitle(running,"重点 · "+ProjectName(running)); view.search.Clear();
            }
            if(testDir!=null) { view.SetHidden(idle,true); if(!new BindingStore(Path.Combine(testDir,"hidden.json")).Read().ContainsKey(ProjectKey(idle))) throw new Exception("Hidden persistence failed"); }
            else { view.hidden[ProjectKey(idle)]="hidden"; view.RenderCards(); }
            if(view.cards.Entries.Count!=2) throw new Exception("Hide failed");
            view.showAll.Checked=true; if(view.cards.Entries.Count!=3) throw new Exception("Show all failed");
            if(testDir!=null) {
                string original=idle.ProjectPath,key=ProjectKey(idle);
                view.SaveDirectory(idle,testDir);
                var reloaded=new Hosted { ProjectPath=original,CodexPath="new-session" };
                ApplyDirectory(reloaded,new BindingStore(Path.Combine(testDir,"directories.json")).Read());
                if(reloaded.ProjectPath!=testDir||ProjectKey(reloaded)!=key||!view.hidden.ContainsKey(key)) throw new Exception("Directory mapping stability failed");
                view.SaveDirectory(idle,null); if(idle.ProjectPath!=original) throw new Exception("Directory reset failed");
                view.nextScan=DateTime.MaxValue; view.directories[key]=false;
                VerifyDiscovery(testDir);
                if(Directory.Exists(Path.Combine(testDir,"index-fixture"))) VerifyIndexedDiscovery(testDir);
            }
            view.viewed[done.CodexPath]="turn-1"; if(!view.IsViewed(done)) throw new Exception("Viewed state failed");
            done.CompletionToken="turn-2"; if(view.IsViewed(done)) throw new Exception("New completion must be unread");
            // Smoke tests keep preferences in memory; screenshot tests use isolated files.
            if(testDir==null) view.cards.PinEntry=e=> {
                if(e.Pinned) view.pinned.Remove(e.Key); else view.pinned[e.Key]=view.NextPinOrder();
                view.RenderCards();
            };
            clicked=null;
            view.cards.TestPinClick(view.cards.Entries.FindIndex(e=>e.Project==done));
            view.cards.TestPinClick(view.cards.Entries.FindIndex(e=>e.Project==idle));
            if(clicked!=null||view.cards.Entries[0].Project!=done||view.cards.Entries[1].Project!=idle) throw new Exception("Multiple pin order or action routing failed");
            view.projects.Reverse(); view.RenderCards();
            if(view.cards.Entries[0].Project!=done||view.cards.Entries[1].Project!=idle) throw new Exception("Refresh changed pinned order");
            view.showAll.Checked=false;
            if(view.cards.Entries.Any(e=>e.Project==idle)) throw new Exception("Pin must not override hidden preference");
            view.showAll.Checked=true;
            if(testDir!=null) using(var restored=new Overview(testDir)) {
                restored.projects=view.projects; restored.showAll.Checked=true; restored.RenderCards();
                if(restored.cards.Entries[0].Project!=done||restored.cards.Entries[1].Project!=idle) throw new Exception("Pin persistence failed");
                var moved=new Hosted { ProjectPath=idle.OriginalProjectPath??idle.ProjectPath,CodexPath="replacement-session" };
                ApplyDirectory(moved,new Dictionary<string,string> { { ProjectKey(idle),testDir } });
                if(!restored.pinned.ContainsKey(ProjectKey(moved))) throw new Exception("Directory or session change lost pin");
            }
            view.cards.TestPinClick(0);
            if(view.cards.Entries[0].Project!=idle||view.pinned.ContainsKey(ProjectKey(done))) throw new Exception("Unpin failed");
            if(testDir!=null&&new BindingStore(Path.Combine(testDir,"pinned.json")).Read().ContainsKey(ProjectKey(done))) throw new Exception("Unpin persistence failed");
            view.cards.TestClick(view.cards.Entries.FindIndex(e=>e.Project==running),false);
            view.showAll.Checked=false;
            view.search.Text="已迁移 旧项目"; view.ApplySearch();
            if(view.cards.Entries.Count!=1||view.cards.Entries[0].Project!=idle||!view.cards.Entries[0].Hidden) throw new Exception("Full search excluded hidden project or multi-term match");
            string originalPath=idle.ProjectPath; idle.OriginalProjectPath=originalPath; idle.ProjectPath=@"D:\Moved\NewLocation"; view.searchIndex.Remove(idle);
            view.search.Text="newlocation"; view.ApplySearch(); if(view.cards.Entries.Count!=1) throw new Exception("Replacement directory not searchable");
            view.search.Text="已迁移"; view.ApplySearch(); if(view.cards.Entries.Count!=1) throw new Exception("Original directory not searchable");
            idle.ProjectPath=originalPath; view.searchIndex.Remove(idle);
            view.search.Text="no-such-project"; view.ApplySearch(); if(view.cards.Entries.Count!=0) throw new Exception("Empty search result failed");
            view.search.Clear(); if(view.cards.Entries.Any(e=>e.Project==idle)) throw new Exception("Clearing search did not restore hidden filter");
            view.showAll.Checked=true;
            view.RenderCards(); Application.DoEvents();
            view.openWindows.Names.Add(Path.GetFileName(running.ProjectPath)); view.openWindows.Names.Add(Path.GetFileName(done.ProjectPath));
            view.managedWindows.Names.Add(Path.GetFileName(running.ProjectPath));
            view.pinned[ProjectKey(running)]="1"; view.RenderCards();
            if(!view.cards.Entries.Single(e=>e.Project==running).Managed||!view.cards.Entries.Single(e=>e.Project==running).Opened||!view.cards.Entries.Single(e=>e.Project==running).Pinned||view.cards.Entries.Single(e=>e.Project==done).Managed) throw new Exception("Managed / external / pinned state missing");
            if(screenshot!=null) using(var image=new Bitmap(view.Width,view.Height)) { view.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size)); image.Save(screenshot); }
            view.ApplyTheme(true);
            Application.DoEvents();
            if(view.cards.BackColor.GetBrightness()<0.8f||!view.cards.Light) throw new Exception("Light theme failed");
            if(screenshot!=null) using(var image=new Bitmap(view.Width,view.Height)) { view.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size)); image.Save(Path.Combine(testDir,"overview-light.png")); }
            view.ApplyTheme(false); view.openWindows.Names.Clear(); view.managedWindows.Clear(); view.pinned.Remove(ProjectKey(running)); view.RenderCards();
            for(int i=0;i<10000;i++) view.projects.Add(new Hosted { ProjectPath=@"D:\Projects\resize-fixture-"+i,CodexStatus="idle" });
            view.RenderCards(); var watch=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<40;i++) { form.Width=760+i*16; Application.DoEvents(); }
            watch.Stop();
            if(view.cards.Controls.Count!=0||view.cards.LastPainted>30||view.cards.SelectedKey!=ProjectKey(running)) throw new Exception("Viewport rendering regression");
            view.cards.AutoScrollPosition=new Point(0,Int32.MaxValue); Application.DoEvents();
            if(view.cards.LastPainted>30) throw new Exception("Scroll renders offscreen cards");
            view.search.Text="resize-fixture-1999"; view.ApplySearch(); if(view.cards.Entries.Count!=1) throw new Exception("All-project search failed");
            var timings=new List<double>();
            for(int i=0;i<60;i++) {
                view.search.Text=i%3==0?"resize-fixture":i%3==1?"RESIZE 9999":"unmatched-keyword";
                var queryWatch=System.Diagnostics.Stopwatch.StartNew(); view.ApplySearch(); queryWatch.Stop(); timings.Add(queryWatch.Elapsed.TotalMilliseconds);
                int expected=i%3==0?10000:i%3==1?1:0;
                if(view.cards.Entries.Count!=expected) throw new Exception("Large catalogue query correctness failed");
            }
            timings.Sort();
            Console.Error.WriteLine(String.Format(System.Globalization.CultureInfo.InvariantCulture,"Search benchmark: 10003 projects, 60 mixed queries, median {0:F2} ms, p95 {1:F2} ms, max {2:F2} ms",timings[30],timings[56],timings[59]));
            view.search.Text="resize-fixture-9999"; view.ApplySearch();
            foreach(float size in new[]{9f,12f,16f}) {
                using(var font=new Font("Microsoft YaHei UI",size)) {
                    view.cards.Font=font; form.Width=900; Application.DoEvents();
                    if(!view.cards.ButtonsFit()) throw new Exception("Scaled card actions clipped");
                    if(screenshot!=null) using(var image=new Bitmap(view.Width,view.Height)) { view.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size)); image.Save(Path.Combine(testDir,"scaled-"+size+".png")); }
                    view.cards.Font=view.Font;
                }
            }
            Console.Error.WriteLine("Virtual overview: 10003 projects, 0 child windows, 40 resize frames "+watch.ElapsedMilliseconds+" ms, viewport paint "+view.cards.LastPainted);
            form.Close();
        }
        if(testDir!=null) VerifyVisualStates(testDir);
    }
    static void VerifyWindowMembership() {
        using(var view=new Overview()) using(var window=new Form { Text="managed-project - Visual Studio Code" }) {
            var hosted=new Hosted { Handle=window.Handle,IsVSCode=true,ApplicationName="Code" };
            var project=new Hosted { ProjectPath=@"D:\test\managed-project" };
            view.SetOpenWindows(new[]{hosted},new Hosted[0]);
            if(!view.managedWindows.Contains(project)||!view.openWindows.Contains(project)) throw new Exception("Managed window missing from card catalogue");
            view.SetOpenWindows(new Hosted[0],new[]{hosted});
            if(view.managedWindows.Contains(project)||!view.openWindows.Contains(project)) throw new Exception("Detached window still marked managed");
            hosted.CodexPath="bound-fixture"; view.sessionDirectories[hosted.CodexPath]=@"D:\one\shared";
            view.SetOpenWindows(new[]{hosted},new Hosted[0]);
            if(!view.managedWindows.Contains(new Hosted { ProjectPath=@"D:\one\shared" })||view.managedWindows.Contains(new Hosted { ProjectPath=@"D:\two\shared" })) throw new Exception("Bound directories with same name confused");
            window.Dispose(); view.SetOpenWindows(new[]{hosted},new Hosted[0]);
            if(view.managedWindows.Contains(new Hosted { ProjectPath=@"D:\one\shared" })||view.openWindows.Contains(project)) throw new Exception("Closed window still marked open");
        }
    }
    static void VerifyVisualStates(string directory) {
        using(var form=new Form { ClientSize=new Size(1480,1010) }) using(var canvas=new CardCanvas { Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",9) }) {
            canvas.Entries=new List<Entry> {
                new Entry { Key="normal",Title="普通项目",Status="idle" },
                new Entry { Key="managed",Title="正在管理 · 执行中",Managed=true,Opened=true,Status="running" },
                new Entry { Key="pinned",Title="置顶项目 · 完成待查看",Pinned=true,Managed=true,Opened=true,Status="completed" },
                new Entry { Key="external",Title="独立窗口 · 已查看",Opened=true,Status="completed",Viewed=true },
                new Entry { Key="unknown",Title="置顶项目 · 状态未知",Pinned=true,Status="unknown" },
                new Entry { Key="missing",Title="目录异常 · 任务中断",Missing=true,Status="interrupted" },
                new Entry { Key="combined",Title="多种状态同时存在：管理、置顶、隐藏、目录异常",Managed=true,Opened=true,Pinned=true,Hidden=true,Missing=true,Status="completed" },
                new Entry { Key="hidden",Title="已隐藏的项目",Hidden=true,Status="idle" },
                new Entry { Key="selected",Title="当前选中 · 独立窗口",Opened=true,Status="running" }
            };
            foreach(var entry in canvas.Entries) { entry.Path=@"D:\Projects\"+entry.Key; entry.Project=new Hosted(); }
            canvas.SelectedKey="selected"; form.Controls.Add(canvas); form.Show(); Application.DoEvents();
            if(!canvas.ButtonsFit()) throw new Exception("Combined badge row clips");
            foreach(bool light in new[]{false,true}) {
                canvas.Light=light; canvas.BackColor=light?Color.FromArgb(243,245,248):Color.FromArgb(22,27,35); canvas.Invalidate(); Application.DoEvents();
                using(var bitmap=new Bitmap(canvas.Width,canvas.Height)) { canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(directory,light?"card-states-light.png":"card-states-dark.png")); }
            }
            form.Close();
        }
    }
    static void VerifyDiscovery(string directory) {
        string home=Path.Combine(directory,"discovery-fixture"),sessions=Path.Combine(home,"sessions"),archive=Path.Combine(home,"archived_sessions");
        Directory.CreateDirectory(sessions); Directory.CreateDirectory(archive);
        for(int i=0;i<505;i++) File.WriteAllText(Path.Combine(sessions,i+".jsonl"),"{\"type\":\"session_meta\",\"payload\":{\"source\":\"cli\",\"cwd\":\"D:\\\\test-"+i+"\",\"id\":\""+i+"\"}}\n");
        File.WriteAllText(Path.Combine(archive,"archived.jsonl"),"{\"type\":\"session_meta\",\"payload\":{\"source\":\"vscode\",\"cwd\":\"D:\\\\archived\",\"id\":\"archived\"}}\n");
        var all=CodexState.Discover(true,home);
        if(all.Count!=506||!all.Any(s=>s.Id=="archived")) throw new Exception("All sources / archived / >500 discovery failed");
    }
    static void VerifyIndexedDiscovery(string directory) {
        string fixture=Path.Combine(directory,"index-fixture");
        var files=Directory.GetFiles(fixture,"*",SearchOption.AllDirectories);
        Func<string,string> hash=file=> { using(var sha=System.Security.Cryptography.SHA256.Create()) using(var stream=File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)); };
        var before=files.ToDictionary(p=>p,hash);
        string warning; var records=LocalProjects.Discover(fixture,out warning); var rows=new List<Hosted>();
        if(!String.IsNullOrEmpty(warning)) throw new Exception(warning);
        MergeIndexedProjects(rows,records);
        if(rows.Count!=7||rows.Any(h=>h.CodexPath!=null)||!rows.Any(IsRemote)) throw new Exception("Indexed independent / remote projects / missing log state failed");
        foreach(string name in new[]{"中文独立A","中文独立B","无目录项目","多根工程"}) if(!rows.Any(h=>h.OverviewTitle==name)) throw new Exception("Indexed name missing: "+name);
        if(rows.Where(h=>h.ProjectPath==@"C:\shared").Select(ProjectKey).Distinct().Count()!=2) throw new Exception("Independent projects sharing a root were merged");
        if(!rows.Single(h=>h.OverviewTitle=="多根工程").ProjectSearchText.Contains(@"C:\two")) throw new Exception("Secondary project root missing from search text");
        foreach(var file in before) if(hash(file.Key)!=file.Value) throw new Exception("Read-only database was changed");
        Console.Error.WriteLine("SQLite fixtures passed: independent projects, Chinese names, multiple roots, missing logs; database hashes unchanged");
        using(var view=new Overview(directory)) {
            var clock=System.Diagnostics.Stopwatch.StartNew(); var snapshot=view.Scan(0);
            Console.Error.WriteLine("Actual catalogue: "+snapshot.Projects.Count+" directories, "+snapshot.Conversations.Count+" independent conversations, "+snapshot.Conversations.Count(IsRemote)+" remote; "+clock.ElapsedMilliseconds+" ms; warnings: "+snapshot.Warning);
            view.directoryProjects=snapshot.Projects; view.conversationProjects=snapshot.Conversations;
            view.viewMode.SelectedIndex=0; view.viewMode.SelectedIndex=1; if(view.projects.Count!=snapshot.Conversations.Count) throw new Exception("Conversation view failed");
            using(var restored=new Overview(directory)) if(restored.viewMode.SelectedIndex!=1) throw new Exception("View selection persistence failed");
            view.viewMode.SelectedIndex=0;
        }
    }
}
