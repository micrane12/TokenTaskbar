using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("0.1.4.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.4.0")]
static class AppVersion {
    public static string Display {get {return "v"+typeof(AppVersion).Assembly.GetName().Version.ToString(3);}}
}

public class Quota { public string label; public double used, reset; public bool valid; public bool active=true; }
public class Provider {
    public string id, name, state, source; public double tokens5h, tokensWeek, observed;
    public bool found, stale; public Quota[] windows;
}
public class Snapshot { public double generated; public string machine; public Provider[] providers; }

public class WidgetSettings {
    public int SizePercent=100, Offset=0;
    public string Position="Left";
    public bool ShowClaude=true, ShowCodex=true, Enabled=true, ShowLogos=true;
    public void Normalize() {
        SizePercent=Math.Max(70,Math.Min(125,SizePercent));Offset=Math.Max(-400,Math.Min(400,Offset));
        if(Position!="Left" && Position!="Center" && Position!="Right") Position="Left";
        if(!ShowClaude && !ShowCodex) {ShowClaude=true;ShowCodex=true;}
    }
    public string[] VisibleProviders() {
        if(ShowClaude && !ShowCodex) return new[]{"claude"};
        if(ShowCodex && !ShowClaude) return new[]{"codex"};
        return Art.AllProviders;
    }
    public WidgetSettings Clone() {return (WidgetSettings)MemberwiseClone();}
    public bool SameAs(WidgetSettings other) {
        return other!=null && SizePercent==other.SizePercent && Offset==other.Offset && Position==other.Position
            && ShowClaude==other.ShowClaude && ShowCodex==other.ShowCodex && Enabled==other.Enabled && ShowLogos==other.ShowLogos;
    }
    public static WidgetSettings Load(string path) {
        try {var v=new JavaScriptSerializer().Deserialize<WidgetSettings>(File.ReadAllText(path));if(v!=null){v.Normalize();return v;}} catch { }
        return new WidgetSettings();
    }
    public void Save(string path) {
        Normalize();string temporary=path+".tmp";
        File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(this));
        if(File.Exists(path)) File.Replace(temporary,path,null);else File.Move(temporary,path);
    }
}
public static class Startup {
    public static string ShortcutPath {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Token Taskbar.lnk");}}
    public static bool Enabled {get {return File.Exists(ShortcutPath);}}
    public static void SetEnabled(bool enabled) {
        if(!enabled) {if(Enabled) File.Delete(ShortcutPath);return;}
        object shell=null,shortcut=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            shortcut=shell.GetType().InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{ShortcutPath});
            var type=shortcut.GetType();
            type.InvokeMember("TargetPath",System.Reflection.BindingFlags.SetProperty,null,shortcut,new object[]{Application.ExecutablePath});
            type.InvokeMember("WorkingDirectory",System.Reflection.BindingFlags.SetProperty,null,shortcut,new object[]{AppDomain.CurrentDomain.BaseDirectory});
            type.InvokeMember("Description",System.Reflection.BindingFlags.SetProperty,null,shortcut,new object[]{"Launch Token Taskbar at Windows sign-in"});
            type.InvokeMember("Save",System.Reflection.BindingFlags.InvokeMethod,null,shortcut,null);
        } finally {
            if(shortcut!=null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if(shell!=null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
// Owner-drawn controls: the stock TrackBar, ComboBox and CheckBox ignore the dark palette.
class Slider : Control {
    public int Minimum, Maximum, Step=1, KeyStep=1; public bool FillFromCenter;
    public event EventHandler ValueChanged;
    int current; bool dragging;
    public Slider() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.Slider;
    }
    public int Value {
        get {return current;}
        set {
            int next=Math.Max(Minimum,Math.Min(Maximum,Minimum+(int)Math.Round((value-Minimum)/(double)Step)*Step));
            if(next==current) return;
            current=next;AccessibleDescription=next.ToString();Invalidate();
            if(ValueChanged!=null) ValueChanged(this,EventArgs.Empty);
        }
    }
    float Radius {get {return Height*0.375f;}}
    float XFor(int v) {return Radius+(Width-2*Radius)*(v-Minimum)/(float)(Maximum-Minimum);}
    int ValueAt(int x) {return Minimum+(int)Math.Round((x-Radius)/(Width-2*Radius)*(Maximum-Minimum));}
    protected override void OnMouseDown(MouseEventArgs e) {base.OnMouseDown(e);if(e.Button!=MouseButtons.Left) return;Focus();dragging=true;Value=ValueAt(e.X);}
    protected override void OnMouseMove(MouseEventArgs e) {base.OnMouseMove(e);if(dragging) Value=ValueAt(e.X);}
    protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);dragging=false;}
    protected override void OnEnabledChanged(EventArgs e) {base.OnEnabledChanged(e);Invalidate();}
    protected override bool IsInputKey(Keys key) {
        var k=key&Keys.KeyCode;
        return k==Keys.Left || k==Keys.Right || k==Keys.Up || k==Keys.Down || base.IsInputKey(key);
    }
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);int step=e.Shift ? Step : KeyStep;
        if(e.KeyCode==Keys.Left || e.KeyCode==Keys.Down) Value-=step;
        else if(e.KeyCode==Keys.Right || e.KeyCode==Keys.Up) Value+=step;
        else if(e.KeyCode==Keys.PageDown) Value-=KeyStep*5;
        else if(e.KeyCode==Keys.PageUp) Value+=KeyStep*5;
        else if(e.KeyCode==Keys.Home) Value=Minimum;
        else if(e.KeyCode==Keys.End) Value=Maximum;
        else return;
        e.Handled=true;
    }
    protected override void OnGotFocus(EventArgs e) {base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e) {base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        float r=Radius,track=Math.Max(2,Height/6f),y=Height/2f,x=XFor(current);
        Color accent=Enabled ? Art.Accent("codex") : Art.Line,core=Enabled ? Art.Ink : Art.Muted;
        Art.Round(g,Art.Line,new RectangleF(r,y-track/2,Width-2*r,track),track/2);
        float from=FillFromCenter ? XFor(Math.Max(Minimum,Math.Min(Maximum,0))) : r;
        Art.Round(g,accent,new RectangleF(Math.Min(from,x),y-track/2,Math.Abs(x-from),track),track/2);
        using(var ring=new SolidBrush(accent)) g.FillEllipse(ring,x-r,y-r,2*r,2*r);
        using(var dot=new SolidBrush(core)) g.FillEllipse(dot,x-r*0.55f,y-r*0.55f,r*1.1f,r*1.1f);
        if(Focused) using(var pen=new Pen(Art.Ink,Math.Max(1,Height/16f))) g.DrawEllipse(pen,x-r-2,y-r-2,2*r+4,2*r+4);
    }
}
class ToggleSwitch : Control {
    bool on; public event EventHandler CheckedChanged;
    public ToggleSwitch() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.CheckButton;
    }
    public bool Checked {
        get {return on;}
        set {if(on==value) return;on=value;AccessibleDescription=on ? "On" : "Off";Invalidate();if(CheckedChanged!=null) CheckedChanged(this,EventArgs.Empty);}
    }
    protected override void OnMouseClick(MouseEventArgs e) {base.OnMouseClick(e);if(e.Button==MouseButtons.Left && Enabled) {Focus();Checked=!Checked;}}
    protected override void OnKeyDown(KeyEventArgs e) {base.OnKeyDown(e);if(e.KeyCode==Keys.Space && Enabled) {Checked=!Checked;e.Handled=true;}}
    protected override void OnEnabledChanged(EventArgs e) {base.OnEnabledChanged(e);Cursor=Enabled ? Cursors.Hand : Cursors.Default;Invalidate();}
    protected override void OnGotFocus(EventArgs e) {base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e) {base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        int alpha=Enabled ? 255 : 110;float h=Height-3,w=Width-3,knob=h*0.6f;
        var body=new RectangleF(1.5f,1.5f,w,h);
        Color accent=Color.FromArgb(alpha,Art.Accent("codex")),muted=Color.FromArgb(alpha,Art.Muted);
        if(on) Art.Round(g,accent,body,h/2); else Art.Outline(g,muted,body,h/2,Math.Max(1,Height/20f));
        float kx=on ? body.Right-h/2-knob/2 : body.Left+h/2-knob/2;
        using(var brush=new SolidBrush(on ? Art.Bg : muted)) g.FillEllipse(brush,kx,body.Top+(h-knob)/2,knob,knob);
        if(Focused) Art.Outline(g,Art.Ink,new RectangleF(0.5f,0.5f,Width-1,Height-1),Height/2f,1);
    }
}
class Segmented : Control {
    readonly string[] options; int selected; public event EventHandler SelectedChanged; public bool ShowGlyphs=true;
    protected override void OnEnabledChanged(EventArgs e) {base.OnEnabledChanged(e);Invalidate();}
    public Segmented(string[] values) {
        options=values;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.PageTabList;
    }
    public string Selected {
        get {return options[selected];}
        set {int i=Array.IndexOf(options,value);if(i<0 || i==selected) return;selected=i;AccessibleDescription=value;Invalidate();if(SelectedChanged!=null) SelectedChanged(this,EventArgs.Empty);}
    }
    Rectangle Segment(int i) {
        int pad=Math.Max(2,Height/11),w=(Width-pad*(options.Length+1))/options.Length;
        return new Rectangle(pad+i*(w+pad),pad,w,Height-2*pad);
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);Focus();
        for(int i=0;i<options.Length;i++) if(Segment(i).Contains(e.Location)) Selected=options[i];
    }
    protected override bool IsInputKey(Keys key) {var k=key&Keys.KeyCode;return k==Keys.Left || k==Keys.Right || base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if(e.KeyCode==Keys.Left && selected>0) {Selected=options[selected-1];e.Handled=true;}
        if(e.KeyCode==Keys.Right && selected<options.Length-1) {Selected=options[selected+1];e.Handled=true;}
    }
    protected override void OnGotFocus(EventArgs e) {base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e) {base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        float radius=Height/5f;
        Art.Round(g,Art.Bg,new RectangleF(0,0,Width,Height),radius);
        Art.Outline(g,Art.Line,new RectangleF(0.5f,0.5f,Width-1,Height-1),radius,1);
        for(int i=0;i<options.Length;i++) {
            var r=Segment(i);bool active=i==selected;Color ink=active ? Art.Ink : Art.Muted;
            if(active) {Art.Round(g,Art.Card,r,radius*0.7f);Art.Outline(g,Focused ? Art.Accent("codex") : Art.Line,r,radius*0.7f,1);}
            if(!Enabled) ink=Color.FromArgb(90,ink);
            var text=TextRenderer.MeasureText(g,options[i],Font);
            float gh=Height*0.3f,gw=ShowGlyphs ? gh*2.2f : 0,gap=ShowGlyphs ? Height*0.22f : 0;
            float start=r.Left+(r.Width-(gw+gap+text.Width))/2f,gy=r.Top+(r.Height-gh)/2f;
            if(ShowGlyphs) {
                // Small picture of where the widget sits on the taskbar.
                Art.Outline(g,Color.FromArgb(130,ink),new RectangleF(start,gy,gw,gh),gh/4,1);
                float bw=gw*0.32f,bx=i==0 ? start+gw*0.1f : i==1 ? start+(gw-bw)/2 : start+gw*0.9f-bw;
                Art.Round(g,ink,new RectangleF(bx,gy+gh*0.3f,bw,gh*0.4f),1);
            }
            TextRenderer.DrawText(g,options[i],Font,new Point((int)Math.Round(start+gw+gap),r.Top+(r.Height-text.Height)/2),ink,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
        }
    }
}
// Settings tab of the Token Taskbar window. Changes preview on the taskbar; Save writes them.
class SettingsPage : UserControl {
    readonly Widget widget;
    readonly float k;
    readonly Slider size=new Slider {Minimum=70,Maximum=125,Step=5,KeyStep=5,AccessibleName="Widget size, percent"};
    readonly Slider nudge=new Slider {Minimum=-400,Maximum=400,Step=1,KeyStep=10,FillFromCenter=true,AccessibleName="Nudge, pixels"};
    readonly Segmented position=new Segmented(new[]{"Left","Center","Right"}) {AccessibleName="Taskbar position"};
    readonly ToggleSwitch enabled=new ToggleSwitch {AccessibleName="Show widget on taskbar"},
        showClaude=new ToggleSwitch {AccessibleName="Show Claude"},showCodex=new ToggleSwitch {AccessibleName="Show Codex"},
        startup=new ToggleSwitch {AccessibleName="Start with Windows"},logos=new ToggleSwitch {AccessibleName="Show provider logos"};
    readonly Button nudgeLeft=new Button {Text="◀",AccessibleName="Nudge left 10 pixels"},nudgeRight=new Button {Text="▶",AccessibleName="Nudge right 10 pixels"},
        undo=new Button {Text="Undo changes"};
    public readonly Button SaveButton=new Button {Text="Save"};
    readonly LinkLabel nudgeReset=new LinkLabel {Text="Reset",AutoSize=true},reset=new LinkLabel {Text="Reset to defaults",AutoSize=true};
    readonly System.Windows.Forms.Timer preview=new System.Windows.Forms.Timer {Interval=300};
    bool loading,startupSaved; string errorTitle="",errorText="";
    static readonly Color Ok=Color.FromArgb(111,207,151),Warn=Color.FromArgb(240,201,75),Bad=Color.FromArgb(237,111,106);
    int S(float v) {return (int)Math.Round(v*k);}
    Rectangle R(int x,int y,int w,int h) {return new Rectangle(S(x),S(y),S(w),S(h));}
    public SettingsPage(Widget owner,float scale) {
        widget=owner;k=scale;DoubleBuffered=true;BackColor=Art.Bg;ForeColor=Art.Ink;Font=Art.UiFont((float)Math.Round(14*k),false);
        size.Bounds=R(36,64,308,24);position.Bounds=R(36,168,308,34);
        nudgeLeft.Bounds=R(36,250,32,32);nudge.Bounds=R(78,254,224,24);nudgeRight.Bounds=R(312,250,32,32);nudgeReset.Location=new Point(S(196),S(228));
        enabled.Bounds=R(654,44,40,20);showClaude.Bounds=R(654,182,40,20);showCodex.Bounds=R(654,226,40,20);startup.Bounds=R(654,331,40,20);logos.Bounds=R(304,333,40,20);
        reset.Location=new Point(S(20),S(402));undo.Bounds=R(470,394,120,34);SaveButton.Bounds=R(598,394,112,34);
        foreach(Control c in new Control[]{size,nudge,position,nudgeReset,enabled,showClaude,showCodex,startup,logos}) c.BackColor=Art.Card;
        foreach(var b in new[]{nudgeLeft,nudgeRight,undo,SaveButton}) {
            b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Art.Line;b.BackColor=Art.Bg;b.ForeColor=Art.Ink;b.Cursor=Cursors.Hand;b.UseVisualStyleBackColor=false;
        }
        nudgeLeft.Font=nudgeRight.Font=new Font("Segoe UI Symbol",8);
        foreach(var link in new[]{nudgeReset,reset}) {link.LinkColor=Art.Muted;link.ActiveLinkColor=Art.Ink;link.VisitedLinkColor=Art.Muted;link.Font=Art.UiFont((float)Math.Round(12*k),false);}
        reset.BackColor=Art.Bg;
        Controls.AddRange(new Control[]{size,position,nudgeLeft,nudge,nudgeRight,nudgeReset,logos,enabled,showClaude,showCodex,startup,reset,undo,SaveButton});
        size.ValueChanged+=(s,e)=>Changed();nudge.ValueChanged+=(s,e)=>Changed();position.SelectedChanged+=(s,e)=>Changed();
        foreach(var t in new[]{enabled,showClaude,showCodex,startup,logos}) t.CheckedChanged+=(s,e)=>Changed();
        nudgeLeft.Click+=(s,e)=>Nudge(-1);nudgeRight.Click+=(s,e)=>Nudge(1);
        nudgeReset.LinkClicked+=(s,e)=>nudge.Value=0;
        reset.LinkClicked+=(s,e)=> {
            var d=new WidgetSettings();loading=true;
            size.Value=d.SizePercent;position.Selected=d.Position;nudge.Value=d.Offset;
            enabled.Checked=true;showClaude.Checked=true;showCodex.Checked=true;logos.Checked=true;
            loading=false;Changed();
        };
        preview.Tick+=(s,e)=> {preview.Stop();widget.PreviewSettings(Read());};
        SaveButton.Click+=(s,e)=>Save();
        undo.Click+=(s,e)=> {Revert();Reload();};
        widget.PlacementChanged+=(s,e)=>Invalidate();
    }
    WidgetSettings Read() {
        return new WidgetSettings {SizePercent=size.Value,Position=position.Selected,Offset=nudge.Value,
            ShowClaude=showClaude.Checked,ShowCodex=showCodex.Checked,Enabled=enabled.Checked,ShowLogos=logos.Checked};
    }
    bool Dirty {get {return !Read().SameAs(widget.SavedSettings) || startup.Checked!=startupSaved;}}
    void Nudge(int direction) {nudge.Value+=direction*((ModifierKeys & Keys.Shift)!=0 ? 1 : 10);}
    void Changed() {
        if(loading) return;
        errorTitle="";preview.Stop();preview.Start();UpdateChrome();
    }
    void UpdateChrome() {
        bool dirty=Dirty,on=enabled.Checked;
        SaveButton.Enabled=dirty;undo.Enabled=dirty;
        SaveButton.BackColor=dirty ? Art.Accent("codex") : Art.Card;SaveButton.ForeColor=dirty ? Art.Bg : Art.Muted;SaveButton.FlatAppearance.BorderSize=dirty ? 0 : 1;
        undo.ForeColor=dirty ? Art.Ink : Art.Muted;
        foreach(Control c in new Control[]{size,position,nudge,nudgeLeft,nudgeRight,nudgeReset,logos}) c.Enabled=on;
        nudgeReset.Visible=nudge.Value!=0;
        showClaude.Enabled=on && !(showClaude.Checked && !showCodex.Checked);
        showCodex.Enabled=on && !(showCodex.Checked && !showClaude.Checked);
        Invalidate();
    }
    void ShowError(string title,string text) {errorTitle=title;errorText=text;UpdateChrome();}
    void Save() {
        preview.Stop();
        if(!widget.SaveSettings(Read())) {ShowError("Couldn't save your changes","Move the folder somewhere you own, like Documents.");return;}
        if(startup.Checked!=startupSaved) {
            try {Startup.SetEnabled(startup.Checked);startupSaved=Startup.Enabled;}
            catch {ShowError("Couldn't change Start with Windows","Your other settings were saved.");return;}
        }
        errorTitle="";UpdateChrome();
    }
    // Puts the taskbar back to the saved settings.
    public void Revert() {preview.Stop();errorTitle="";widget.RevertSettings();}
    public void Reload() {
        var saved=widget.SavedSettings;loading=true;
        size.Value=saved.SizePercent;position.Selected=saved.Position;nudge.Value=saved.Offset;
        enabled.Checked=saved.Enabled;showClaude.Checked=saved.ShowClaude;showCodex.Checked=saved.ShowCodex;logos.Checked=saved.ShowLogos;
        startupSaved=Startup.Enabled;startup.Checked=startupSaved;loading=false;errorTitle="";UpdateChrome();
    }
    static void Divider(Graphics g,int left,int right,int y) {using(var pen=new Pen(Color.FromArgb(38,38,44))) g.DrawLine(pen,left,y,right,y);}
    static void Group(Graphics g,string text,int x,int y) {Art.Text(g,text,11,Art.Muted,new Rectangle(x+2,y,300,16),true);}
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.ScaleTransform(k,k);
        bool on=enabled.Checked;Color label=on ? Art.Ink : Art.Muted;

        // Left column: size and placement.
        Group(g,"ON THE TASKBAR",20,8);
        Art.Round(g,Art.Card,new Rectangle(20,28,340,336),10);
        Art.Text(g,"Size",13,label,new Rectangle(36,38,200,22),true);
        Art.Text(g,size.Value+"%",13,label,new Rectangle(244,38,100,22),true,true);
        Art.Text(g,"70%",11,Art.Muted,new Rectangle(36,90,60,16));
        Art.Text(g,"100%",11,Art.Muted,new Rectangle(188,90,60,16));
        Art.Text(g,"125%",11,Art.Muted,new Rectangle(284,90,60,16),false,true);
        Art.Text(g,"Never taller than the taskbar.",12,Art.Muted,new Rectangle(36,108,308,18));
        Divider(g,20,360,134);
        Art.Text(g,"Position",13,label,new Rectangle(36,144,200,22),true);
        Divider(g,20,360,216);
        Art.Text(g,"Nudge",13,label,new Rectangle(36,226,150,22),true);
        Art.Text(g,(nudge.Value>0 ? "+" : "")+nudge.Value+" px",13,label,new Rectangle(244,226,100,22),true,true);
        Art.Text(g,"Arrows move 10 px. Hold Shift for 1 px.",12,Art.Muted,new Rectangle(36,288,308,18));
        Divider(g,20,360,320);
        Art.Text(g,"Show provider logos",13,label,new Rectangle(36,332,250,22),true);

        // Right column: on/off and where it landed, providers, startup.
        Group(g,"WIDGET",380,8);
        Art.Round(g,Art.Card,new Rectangle(380,28,330,108),10);
        Art.Text(g,"Show on taskbar",13,Art.Ink,new Rectangle(396,38,240,22),true);
        Art.Text(g,"Turn off to hide it. The tray icon stays.",12,Art.Muted,new Rectangle(396,58,250,18));
        string title,detail;Color color;
        string placement=!on ? "off" : preview.Enabled ? "pending" : widget.Placement;
        if(placement=="off") {color=Art.Muted;title="Widget is off";detail="Open Token Taskbar from the tray icon.";}
        else if(placement=="ok") {color=Ok;title="On the taskbar, "+(position.Selected=="Center" ? "centered" : position.Selected.ToLower()+" side");detail="Placed where you asked.";}
        else if(placement=="moved") {color=Warn;title="Moved to the nearest free space";detail="Buttons are in the way. Try a smaller size.";}
        else if(placement=="tray") {color=Bad;title="Not enough room on the taskbar";detail="Showing in the tray until there is room.";}
        else {color=Art.Muted;title="Checking the taskbar…";detail="Finding a free space for this size.";}
        Art.Round(g,Art.Bg,new Rectangle(390,84,310,44),8);
        using(var dot=new SolidBrush(color)) g.FillEllipse(dot,402,102,8,8);
        Art.Text(g,title,13,Art.Ink,new Rectangle(418,86,276,20),true);
        Art.Text(g,detail,11,Art.Muted,new Rectangle(418,105,276,16));

        Group(g,"PROVIDERS",380,150);
        Art.Round(g,Art.Card,new Rectangle(380,170,330,108),10);
        Art.Logo(g,"claude",new Rectangle(396,181,20,20));Art.Text(g,"Show Claude",13,label,new Rectangle(426,180,200,22),true);
        Divider(g,380,710,214);
        Art.Logo(g,"codex",new Rectangle(396,225,20,20));Art.Text(g,"Show Codex",13,label,new Rectangle(426,224,200,22),true);
        Art.Text(g,showClaude.Checked && showCodex.Checked ? "Hiding one makes the widget about half as wide." : "At least one provider stays on.",
            12,Art.Muted,new Rectangle(396,254,300,18));

        Group(g,"STARTUP",380,292);
        Art.Round(g,Art.Card,new Rectangle(380,312,330,58),10);
        Art.Text(g,"Start with Windows",13,Art.Ink,new Rectangle(396,320,240,22),true);
        Art.Text(g,"Opens Token Taskbar when you sign in.",12,Art.Muted,new Rectangle(396,342,250,18));

        // Footer: save state or error between the reset link and the buttons.
        using(var pen=new Pen(Color.FromArgb(45,45,53))) g.DrawLine(pen,20,380,710,380);
        if(errorTitle!="") {
            Art.Text(g,errorTitle,13,Bad,new Rectangle(160,394,300,18),true);
            Art.Text(g,errorText,11,Art.Muted,new Rectangle(160,412,300,16));
        } else {
            bool dirty=Dirty;
            using(var dot=new SolidBrush(dirty ? Warn : Ok)) g.FillEllipse(dot,262,408,6,6);
            Art.Text(g,dirty ? "Not saved yet · previewing" : "Saved",12,dirty ? Warn : Art.Muted,new Rectangle(274,401,190,20));
        }
    }
}
static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; public POINT(int x,int y){X=x;Y=y;} }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int Width,Height; public SIZE(int w,int h){Width=w;Height=h;} }
    [StructLayout(LayoutKind.Sequential,Pack=1)] public struct BLEND { public byte Operation,Flags,Alpha,Format; }
    public delegate bool EnumWindow(IntPtr h,IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent,EnumWindow callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,StringBuilder name,int capacity);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h,ref POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h,uint command);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool UpdateLayeredWindow(IntPtr h,IntPtr screen,ref POINT destination,ref SIZE size,IntPtr source,ref POINT origin,uint key,ref BLEND blend,uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetStyle(IntPtr h, int n);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetStyle(IntPtr h, int n, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int z, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
    public static string ClassName(IntPtr h) {var text=new StringBuilder(256);GetClassName(h,text,text.Capacity);return text.ToString();}
    public static IntPtr TaskbarContentHost(IntPtr bar) {
        IntPtr host=bar;
        EnumChildWindows(bar,(h,p)=> {
            for(IntPtr candidate=h;candidate!=IntPtr.Zero && candidate!=bar;candidate=GetParent(candidate)) {
                string name=ClassName(candidate);
                if(name.Contains("DesktopWindowContentBridge")) {
                    RECT root,bounds; GetWindowRect(bar,out root);GetWindowRect(candidate,out bounds);
                    if(IsWindowVisible(candidate) && bounds.Right-bounds.Left>=root.Right-root.Left-4) {host=candidate;return false;}
                }
            }
            return true;
        },IntPtr.Zero);
        return host;
    }
}

static class Layout {
    // Find a real gap between interactive taskbar controls. Never cover a button.
    public static Rectangle Gap(int left, int right, int top, int height, int width, List<Rectangle> occupied) {
        return PreferredGap(left,right,top,height,width,occupied,"Left",0);
    }
    public static int Target(int left,int right,int width,string position,int offset) {
        return (position=="Right" ? right-width-12 : position=="Center" ? left+(right-left-width)/2 : left+12)+offset;
    }
    public static Rectangle PreferredGap(int left,int right,int top,int height,int width,List<Rectangle> occupied,string position,int offset) {
        int target=Target(left,right,width,position,offset);
        int cursor=left+12;var choices=new List<Rectangle>();
        foreach(var r in occupied.OrderBy(r=>r.Left)) {
            if(r.Right<=left || r.Left>=right) continue;
            int end=Math.Min(right-12,r.Left-12);
            if(end-cursor>=width) choices.Add(new Rectangle(Math.Max(cursor,Math.Min(target,end-width)),top,width,height));
            cursor=Math.Max(cursor,r.Right+12);
        }
        if(right-12-cursor>=width) choices.Add(new Rectangle(Math.Max(cursor,Math.Min(target,right-12-width)),top,width,height));
        return choices.OrderBy(r=>Math.Abs((long)r.Left-target)).FirstOrDefault();
    }
    public static Rectangle FitGap(int left,int right,int top,int height,int wanted,int minimum,List<Rectangle> occupied,string position,int offset) {
        var full=PreferredGap(left,right,top,height,wanted,occupied,position,offset);
        if(!full.IsEmpty) return full;
        int low=Math.Max(1,minimum),high=wanted-1;Rectangle best=Rectangle.Empty;
        while(low<=high) {
            int width=low+(high-low)/2;
            var candidate=PreferredGap(left,right,top,height,width,occupied,position,offset);
            if(candidate.IsEmpty) high=width-1;else {best=candidate;low=width+1;}
        }
        return best;
    }
    public static Rectangle Find(IntPtr taskbar, int width, int self,string position="Left",int offset=0,int minimum=0) {
        Native.RECT nr; Native.GetWindowRect(taskbar, out nr);
        if(nr.Right<=nr.Left || nr.Bottom<=nr.Top) throw new InvalidOperationException("Taskbar geometry is transitioning");
        if (nr.Right - nr.Left < nr.Bottom - nr.Top) return Rectangle.Empty;
        var root = AutomationElement.FromHandle(taskbar);
        var condition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));
        var elements = root.FindAll(TreeScope.Descendants, condition);
        var boxes = new List<Rectangle>();
        foreach (AutomationElement el in elements) {
            try {
                if (el.Current.ProcessId == self || el.Current.IsOffscreen) continue;
                var b = el.Current.BoundingRectangle;
                if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) continue;
                boxes.Add(Rectangle.FromLTRB((int)b.Left, (int)b.Top, (int)b.Right, (int)b.Bottom));
            } catch (ElementNotAvailableException) { }
        }
        // No accessibility controls means layout is unknown: fail closed.
        if (boxes.Count == 0) throw new InvalidOperationException("Taskbar accessibility is transitioning");
        var gap=minimum>0 ? FitGap(nr.Left,nr.Right,nr.Top,nr.Bottom-nr.Top,width,minimum,boxes,position,offset) : PreferredGap(nr.Left,nr.Right,nr.Top,nr.Bottom-nr.Top,width,boxes,position,offset);
        if(gap.IsEmpty) try {File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layout-state.txt"),"root="+nr.Left+","+nr.Top+","+nr.Right+","+nr.Bottom+"; wanted="+width+"; occupied="+String.Join(";",boxes.Select(b=>b.ToString())));} catch { }
        return gap;
    }
}

static class Art {
    static readonly Image ClaudeLogo=LoadLogo("TokenTaskbar.Claude.png");
    static readonly Image CodexLogo=LoadLogo("TokenTaskbar.Codex.png");
    static Image LoadLogo(string name) {
        using(var stream=typeof(Art).Assembly.GetManifestResourceStream(name)) {
            if(stream==null) return null;
            using(var source=Image.FromStream(stream)) return new Bitmap(source);
        }
    }
    public static void Logo(Graphics g,string id,Rectangle bounds) {
        var image=id=="claude" ? ClaudeLogo : CodexLogo;
        if(image==null) return;
        // Resample once, straight to whole device pixels, so the icon stays sharp at any scale.
        var device=Rectangle.Round(ToDevice(g,bounds));
        var transform=g.Transform;var interpolation=g.InterpolationMode;var offset=g.PixelOffsetMode;
        try {
            g.ResetTransform();g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            using(var attributes=new ImageAttributes()) {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(image,device,0,0,image.Width,image.Height,GraphicsUnit.Pixel,attributes);
            }
        } finally {g.Transform=transform;g.InterpolationMode=interpolation;g.PixelOffsetMode=offset;transform.Dispose();}
    }
    // Maps a logical rectangle through a scale/translate transform to device pixels.
    static RectangleF ToDevice(Graphics g,RectangleF r) {
        using(var m=g.Transform) {
            var e=m.Elements;
            return new RectangleF(r.X*e[0]+e[4],r.Y*e[3]+e[5],r.Width*e[0],r.Height*e[3]);
        }
    }
    // Rounds a logical rectangle's edges to whole device pixels and maps it back, so fills have crisp edges.
    static RectangleF Snap(Graphics g,RectangleF r) {
        using(var m=g.Transform) {
            var e=m.Elements;float sx=e[0],sy=e[3];
            float left=(float)Math.Round(r.X*sx+e[4]),top=(float)Math.Round(r.Y*sy+e[5]);
            float right=(float)Math.Round(r.Right*sx+e[4]),bottom=(float)Math.Round(r.Bottom*sy+e[5]);
            if(right<=left) right=left+1; if(bottom<=top) bottom=top+1;
            return RectangleF.FromLTRB((left-e[4])/sx,(top-e[5])/sy,(right-e[4])/sx,(bottom-e[5])/sy);
        }
    }
    // One-device-pixel vertical rule.
    public static void Rule(Graphics g,Color color,float x,float top,float bottom) {
        float width;
        using(var m=g.Transform) width=1/m.Elements[0];
        using(var brush=new SolidBrush(color)) g.FillRectangle(brush,Snap(g,new RectangleF(x,top,width,bottom-top)));
    }
    public static Icon TrayIcon() {
        using(var bitmap=new Bitmap(32,32,PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                Round(g,Color.FromArgb(28,28,34),new Rectangle(1,1,30,30),7);
                Round(g,Accent("claude"),new Rectangle(5,7,22,6),3);
                Round(g,Accent("codex"),new Rectangle(5,19,22,6),3);
            }
            var handle=bitmap.GetHicon();
            try {using(var icon=Icon.FromHandle(handle)) return (Icon)icon.Clone();}
            finally {Native.DestroyIcon(handle);}
        }
    }
    public static Color Bg = Color.FromArgb(20,20,23), Card = Color.FromArgb(29,29,33),
        Muted = Color.FromArgb(156,156,168), Ink = Color.FromArgb(242,242,245), Line = Color.FromArgb(53,53,60);
    public static readonly string[] AllProviders={"claude","codex"};
    // Logical strip width: both providers, or one provider block when the other is hidden.
    // Without logos each provider block is LogoSpace narrower.
    public const int LogoSpace=31;
    public static int LogicalWidth(string[] ids,bool logos=true) {
        int count=ids!=null && ids.Length==1 ? 1 : 2;
        return (count==1 ? 444 : Widget.StripWidth)-(logos ? 0 : LogoSpace*count);
    }
    public static Color Accent(string id) { return id == "claude" ? Color.FromArgb(216,135,99) : Color.FromArgb(130,148,232); }
    public static double Now { get { return (DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds; } }
    public static bool Valid(Quota w) { return w != null && w.valid && w.reset > Now; }
    public static string Number(double n) { return n >= 1000000 ? (n/1000000).ToString("0.0")+"M" : n >= 1000 ? (n/1000).ToString("0.0")+"K" : n.ToString("0"); }
    public static string Countdown(double reset) {
        double remaining = reset-Now;
        if (remaining <= 0) return "Awaiting new reading";
        TimeSpan t = TimeSpan.FromSeconds(remaining);
        if (t.TotalDays >= 1) return String.Format("Resets in {0}d {1}h", t.Days,t.Hours);
        return String.Format("Resets in {0}h {1:00}m", (int)t.TotalHours,t.Minutes);
    }
    public static string CompactCountdown(Quota w) {
        if(!Valid(w)) return "—";
        var t=TimeSpan.FromSeconds(w.reset-Now);
        return t.TotalDays>=1 ? String.Format("{0}d {1}h",t.Days,t.Hours)
            : String.Format("{0}h {1:00}m",(int)t.TotalHours,t.Minutes);
    }
    public static string ResetTime(Quota w,bool weekly) {
        if(!Valid(w)) return "—";
        var local=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(w.reset).ToLocalTime();
        return local.ToString(weekly ? "ddd HH:mm" : "HH:mm");
    }
    public static bool Stale(Snapshot data, Provider p) {
        return data==null || Now-data.generated>35 || p==null || p.stale || p.state!="ok";
    }
    public static string FrameKey(Snapshot data,int width,int height,bool hover,string[] ids=null,bool logos=true) {
        ids=ids ?? AllProviders;
        var key=new StringBuilder().Append(width).Append(',').Append(height).Append(',').Append(hover).Append(',').Append(String.Join("+",ids)).Append(',').Append(logos);
        foreach(string id in ids) {
            var p=data==null || data.providers==null ? null : data.providers.FirstOrDefault(v=>v.id==id);
            key.Append('|').Append(Stale(data,p));
            for(int j=0;j<2;j++) {
                var w=p==null || p.windows==null || p.windows.Length<=j ? null : p.windows[j];
                key.Append('|').Append(Valid(w) ? w.used.ToString("R") : "—").Append(':').Append(CompactCountdown(w)).Append(ResetTime(w,j==1));
            }
        }
        return key.ToString();
    }
    public static Bitmap Frame(Snapshot data,int width,int height,bool hover,string[] ids=null,bool logos=true) {
        ids=ids ?? AllProviders;
        // Render above device resolution, then downsample premultiplied alpha once.
        // Font positions retain fractional device pixels at small widget scales.
        const int sampling=3;
        using(var high=new Bitmap(width*sampling,height*sampling,PixelFormat.Format32bppPArgb)) {
            using(var g=Graphics.FromImage(high)) {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.HighQuality;
                g.TextRenderingHint=TextRenderingHint.AntiAlias;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                float scale=width/(float)LogicalWidth(ids,logos);
                g.ScaleTransform(scale*sampling,scale*sampling);
                int logicalHeight=(int)(height/scale);int y=Math.Max(0,(logicalHeight-38)/2);
                Strip(g,data,2,y,hover,ids,logos);
            }
            var result=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
            using(var g=Graphics.FromImage(result)) {
                g.CompositingMode=CompositingMode.SourceCopy;
                g.CompositingQuality=CompositingQuality.HighQuality;
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.DrawImage(high,new Rectangle(0,0,width,height),0,0,high.Width,high.Height,GraphicsUnit.Pixel);
            }
            return result;
        }
    }
    public static void Strip(Graphics g, Snapshot data, int x, int y, bool hover, string[] ids, bool logos=true) {
        int cut=logos ? 0 : LogoSpace;
        for(int i=0;i<ids.Length;i++) {
            string id=ids[i];
            var p=data==null || data.providers==null ? null : data.providers.FirstOrDefault(v=>v.id==id);
            var windows=p==null || p.windows==null ? new Quota[0] : p.windows;
            // Everything after the logo shifts left by `cut` when logos are hidden.
            int left=x+i*(453-cut); bool stale=Stale(data,p); Color accent=Accent(id);
            if(hover) Round(g,Color.FromArgb(30,30,35),new Rectangle(left,y+2,439-cut,34),5);
            if(i==1) Rule(g,Line,left-8,y+6,y+31);
            if(logos) Logo(g,id,new Rectangle(left+8,y+7,22,22));
            Text(g,id=="claude" ? "Claude" : "Codex",12,Ink,new Rectangle(left+39-cut,y+4,61,28),true);
            Rule(g,Line,left+271-cut,y+8,y+34);
            for(int j=0;j<2;j++) {
                var w=windows.Length>j ? windows[j] : null; int q=left+(j==0 ? 103 : 281)-cut;
                string percentage=Valid(w) ? w.used.ToString("0")+"%"+(stale ? "*" : "") : "—";
                int percentWidth=PercentageWidth(g,percentage);
                int barWidth=158-percentWidth-7;
                Bar(g,w,accent,new Rectangle(q,y+10,barWidth,4));
                Text(g,percentage,16,UsageColor(w,Ink),new Rectangle(q+barWidth+7,y+3,percentWidth,32),true,true);
                Text(g,CompactCountdown(w),13,Ink,new Rectangle(q,y+20,46,18),true,false,true);
                Text(g,ResetTime(w,j==1),11,Color.FromArgb(190,190,202),new Rectangle(q+46,y+21,barWidth-46,16),true,true);
            }
        }
    }
    static int PercentageWidth(Graphics g,string text) {
        float scale;using(var transform=g.Transform) scale=transform.Elements[3];
        var state=g.Save();
        try {
            g.ResetTransform();
            var font=UiFont(Math.Max(6f,(float)Math.Round(16*scale)),true,true);
            return (int)Math.Ceiling(g.MeasureString(text,font,1000,StringFormat.GenericTypographic).Width/scale)+2;
        } finally {g.Restore(state);}
    }
    // Static Segoe UI faces: GDI+ cannot select weights of the variable Windows 11 font, and these are hinted for small sizes.
    static readonly bool Semibold=Installed("Segoe UI Semibold");
    static readonly Dictionary<string,Font> Fonts=new Dictionary<string,Font>();
    static bool Installed(string family) {try {using(var f=new Font(family,10)) return f.Name==family;} catch {return false;}}
    // strong: full Bold, which keeps two-pixel stems at taskbar sizes; panels use Semibold like Windows 11.
    public static Font UiFont(float pixels,bool bold,bool strong=false) {
        string family=bold && Semibold && !strong ? "Segoe UI Semibold" : "Segoe UI";
        var style=bold && (strong || !Semibold) ? FontStyle.Bold : FontStyle.Regular;
        string key=family+"|"+pixels+"|"+style;
        lock(Fonts) {
            Font font;
            if(!Fonts.TryGetValue(key,out font)) {font=new Font(family,pixels,style,GraphicsUnit.Pixel);Fonts[key]=font;}
            return font;
        }
    }
    // Text is laid out in logical units but drawn at a whole-pixel size in device space, so hinting works.
    // On opaque surfaces (TextRenderingHint.ClearTypeGridFit) it uses GDI ClearType, like native Windows text.
    public static void Text(Graphics g, string s, float size, Color color, Rectangle bounds, bool bold=false,bool right=false,bool precise=false) {
        var device=ToDevice(g,bounds);float scale;
        using(var m=g.Transform) scale=m.Elements[3];
        bool clearType=g.TextRenderingHint==TextRenderingHint.ClearTypeGridFit;
        var font=UiFont(Math.Max(6f,(float)Math.Round(size*scale)),bold,!clearType && size>=15);
        if(clearType) {
            TextRenderer.DrawText(g,s,font,Rectangle.Round(device),color,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis
                |TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|(right ? TextFormatFlags.Right : TextFormatFlags.Left));
            return;
        }
        var transform=g.Transform;
        try {
            g.ResetTransform();
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat(right || precise ? StringFormat.GenericTypographic : StringFormat.GenericDefault) { LineAlignment=StringAlignment.Center, Alignment=right ? StringAlignment.Far : StringAlignment.Near, Trimming=StringTrimming.EllipsisCharacter, FormatFlags=StringFormatFlags.NoWrap })
                g.DrawString(s,font,brush,device,format);
        } finally {g.Transform=transform;transform.Dispose();}
    }
    public static void Round(Graphics g, Color color, RectangleF r, float radius) {
        if (r.Width <= 0 || r.Height <= 0) return;
        float d = Math.Min(radius*2, Math.Min(r.Width,r.Height));
        using (var path = new GraphicsPath()) using(var brush=new SolidBrush(color)) {
            path.AddArc(r.X,r.Y,d,d,180,90); path.AddArc(r.Right-d,r.Y,d,d,270,90);
            path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.X,r.Bottom-d,d,d,90,90);
            path.CloseFigure(); g.FillPath(brush,path);
        }
    }
    public static void Outline(Graphics g, Color color, RectangleF r, float radius, float thickness) {
        if (r.Width <= 0 || r.Height <= 0) return;
        float d = Math.Min(radius*2, Math.Min(r.Width,r.Height));
        using (var path = new GraphicsPath()) using(var pen=new Pen(color,thickness)) {
            path.AddArc(r.X,r.Y,d,d,180,90); path.AddArc(r.Right-d,r.Y,d,d,270,90);
            path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.X,r.Bottom-d,d,d,90,90);
            path.CloseFigure(); g.DrawPath(pen,path);
        }
    }
    public static Color UsageColor(Quota w,Color accent) {
        if(!Valid(w)) return Muted;
        if(w.used>=80) return Color.FromArgb(237,111,106);
        if(w.used>=60) return Color.FromArgb(240,163,77);
        return accent;
    }
    public static void Bar(Graphics g, Quota w, Color accent, Rectangle r) {
        var track=Snap(g,r);float radius=track.Height/2;
        Round(g, Line, track, radius);
        if (Valid(w) && w.used>0) Round(g, UsageColor(w,accent),
            new RectangleF(track.X,track.Y,Math.Max(track.Height,(float)(track.Width*Math.Min(100,w.used)/100)),track.Height),radius);
    }
}

// The Token Taskbar window: a Usage tab and a Settings tab.
class Detail : Form {
    readonly Widget widget;
    readonly float k;
    readonly Segmented tabs=new Segmented(new[]{"Usage","Settings"}) {ShowGlyphs=false,AccessibleName="View"};
    readonly SettingsPage settings;
    readonly List<Control> usageControls=new List<Control>();
    const int PageTop=64;
    int S(float v) {return (int)Math.Round(v*k);}
    Rectangle R(int x,int y,int w,int h) {return new Rectangle(S(x),S(y),S(w),S(h));}
    public Detail(Widget owner) {
        widget=owner; Text="Token Taskbar "+AppVersion.Display; FormBorderStyle=FormBorderStyle.FixedSingle;
        BackColor=Art.Bg; ForeColor=Art.Ink; ShowInTaskbar=true; Icon=Art.TrayIcon();
        StartPosition=FormStartPosition.Manual; AutoScaleMode=AutoScaleMode.None;
        MaximizeBox=false; MinimizeBox=false; DoubleBuffered=true;
        using(var g=CreateGraphics()) k=g.DpiX/96f;
        Font=Art.UiFont((float)Math.Round(14*k),false);
        ClientSize=new Size(S(730),S(506));
        tabs.Bounds=R(470,16,240,34);tabs.BackColor=Art.Bg;Controls.Add(tabs);
        tabs.SelectedChanged+=(s,e)=>ShowTab(tabs.Selected);
        settings=new SettingsPage(widget,k) {Bounds=new Rectangle(0,S(PageTop),S(730),S(506-PageTop)),Visible=false};
        Controls.Add(settings);
        var close=new Button {Text="Close panel",Bounds=R(20,458,116,34)};
        close.Click+=(s,e)=>Close();
        var refresh=new Button {Text="Refresh",Bounds=R(472,458,110,34)};
        refresh.Click+=(s,e)=>widget.RefreshUsage();
        var quit=new Button {Text="Quit widget",Bounds=R(594,458,116,34)};
        quit.Click+=(s,e)=>widget.Quit();
        usageControls.AddRange(new Control[]{close,refresh,quit});
        for(int i=0;i<2;i++) {
            string provider=i==0 ? "claude" : "codex";
            var renew=new Button {Text="Renew login",Bounds=R(230+i*354,141,110,27)};
            renew.Click+=(s,e)=>RenewLogin(provider);usageControls.Add(renew);
        }
        foreach(var c in usageControls) {
            var b=(Button)c;b.FlatStyle=FlatStyle.Flat;b.BackColor=Art.Card;b.FlatAppearance.BorderColor=Art.Line;
            b.ForeColor=Art.Ink;b.Cursor=Cursors.Hand;Controls.Add(b);
        }
        KeyPreview=true; KeyDown+=(s,e)=> { if(e.KeyCode==Keys.Escape) Close(); };
        // Closing the window drops unsaved settings and puts the taskbar back.
        FormClosing+=(s,e)=> { if(e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; settings.Revert(); Hide(); } };
    }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        // Dark title bar to match the panel (Windows 10 20H1+ / 11; ignored elsewhere).
        int dark=1; try {Native.DwmSetWindowAttribute(Handle,20,ref dark,4);} catch { }
    }
    void ShowTab(string name) {
        bool showSettings=name=="Settings";
        tabs.Selected=name;settings.Visible=showSettings;
        foreach(var c in usageControls) c.Visible=!showSettings;
        AcceptButton=showSettings ? settings.SaveButton : null;
        Invalidate();
    }
    void RenewLogin(string provider) {
        // Delegate authentication to the installed CLI; never handle credentials here.
        string command=provider=="claude" ? "claude auth login" : "codex login";
        string script="$ErrorActionPreference='Stop'; try { "+command+" } catch { Write-Host $_.Exception.Message }; Read-Host 'Press Enter to close. The widget will update on its next account check'";
        try {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe"),"-NoProfile -Command \""+script+"\"") {UseShellExecute=true,WorkingDirectory=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)});
        } catch {MessageBox.Show(this,"Could not open sign-in. Install the "+provider+" CLI and try again.","Token Taskbar");}
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode=PixelOffsetMode.HighQuality; g.ScaleTransform(k,k);
        Art.Text(g,AppVersion.Display,12,Art.Muted,new Rectangle(310,18,140,24),false,true);
        if(settings.Visible) {
            Art.Text(g,"Settings",18,Art.Ink,new Rectangle(20,14,240,25),true);
            Art.Text(g,"Changes show on the taskbar right away. Save to keep them.",11,Art.Muted,new Rectangle(20,38,440,18));
            return;
        }
        Art.Text(g,"AI AGENTS",18,Art.Ink,new Rectangle(20,14,240,25),true);
        Art.Text(g,widget.Machine+"  /  quota used · 5 hours then week",11,Art.Muted,new Rectangle(20,38,440,18));
        using(var strip=Art.Frame(widget.Data,S(626),S(38),false)) g.DrawImage(strip,new Rectangle(52,76,626,38));
        for(int i=0;i<2;i++) {
            string id=i==0 ? "claude" : "codex"; var p=widget.FindProvider(id); int x=20+i*354;
            Color accent=Art.Accent(id);
            Art.Round(g,Art.Card,new Rectangle(x,130,336,174),14);
            Art.Logo(g,id,new Rectangle(x+16,140,24,24));
            Art.Text(g,id=="claude" ? "Claude" : "Codex",18,accent,new Rectangle(x+48,140,170,28),true);
            var windows=p==null || p.windows==null ? new Quota[0] : p.windows;
            for(int j=0;j<2;j++) {
                var w=windows.Length>j ? windows[j] : null; int y=176+j*58;
                Art.Text(g,j==0 ? "5 hours" : "Week",12,Art.Muted,new Rectangle(x+16,y,184,18));
                Art.Text(g,Art.Valid(w) ? w.used.ToString("0")+"% used" : "Unavailable",12,Art.UsageColor(w,Art.Ink),new Rectangle(x+220,y,104,18),true);
                Art.Bar(g,w,accent,new Rectangle(x+16,y+24,304,5));
                Art.Text(g,Art.Valid(w) ? Art.Countdown(w.reset) : "Waiting for an account reading",11,Art.Muted,new Rectangle(x+16,y+32,304,18));
            }
            Art.Text(g,p==null ? "Reading local usage…" : Art.Number(p.tokens5h)+" tokens / 5h · "+Art.Number(p.tokensWeek)+" / 7d",
                12,Art.Ink,new Rectangle(x,316,336,22),true);
            Art.Text(g,widget.StatusFor(p),11,Art.Stale(widget.Data,p) ? Color.FromArgb(240,201,75) : Art.Muted,new Rectangle(x,340,336,20));
        }
        Art.Text(g,"* Last-known quota · token totals cover this PC",10,Art.Muted,new Rectangle(20,410,690,18));
        using(var pen=new Pen(Color.FromArgb(45,45,53))) g.DrawLine(pen,20,444,710,444);
    }
    public void Open(Rectangle bounds,string tab) {
        if(!Visible) {
            settings.Reload();
            var area=Screen.FromRectangle(bounds).WorkingArea;
            Location=new Point(Math.Max(area.Left,Math.Min(bounds.Left,area.Right-Width)),
                bounds.Top > area.Top+Height ? Math.Max(area.Top,bounds.Top-Height-10) : area.Top+10);
        }
        ShowTab(tab); Show(); Activate(); Invalidate();
    }
}

class Widget : Form {
    public bool QuitRequested {get;private set;}
    public Snapshot Data;
    // Settings is what the taskbar shows (possibly an unsaved preview); SavedSettings is what is on disk.
    public WidgetSettings Settings=WidgetSettings.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"widget-settings.json"));
    public WidgetSettings SavedSettings;
    // Result of the latest placement: pending, ok, moved (preferred spot blocked) or tray.
    public string Placement="pending";
    public int EffectiveSize=100;
    public event EventHandler PlacementChanged;
    bool settingsScan;
    public const int StripWidth=896;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    readonly NotifyIcon tray=new NotifyIcon(); readonly Icon trayIcon=Art.TrayIcon(); readonly ToolTip tip=new ToolTip();
    Detail detail; Process worker; IntPtr taskbar; Rectangle slot;
    readonly EventWaitHandle openRequest;
    readonly SynchronizationContext ui=new WindowsFormsSynchronizationContext();
    string renderedKey="",diagnosticMessage="",tooltipText="",panelKey="";
    bool scanning,closing,started; int ticks,scanGeneration; string workerError=""; double lastRestart,missingSince;
    public void Quit() {QuitRequested=true;Close();}
    public Provider FindProvider(string id) { return Data==null || Data.providers==null ? null : Data.providers.FirstOrDefault(p=>p.id==id); }
    public string Machine { get { return Data==null ? Environment.MachineName : Data.machine; } }
    public string StatusFor(Provider p) {
        if(workerError!="") return workerError;
        if(Data==null) return "Connecting to local agents…";
        if(Art.Now-Data.generated>35) return "Offline · last reading retained";
        if(p==null) return "Agent unavailable";
        if(p.observed<=0) return p.state=="loading" ? "Connecting to account…" : "Account unavailable · "+p.state;
        int age=Math.Max(0,(int)((Art.Now-p.observed)/60));
        return (Art.Stale(Data,p) ? "Stale · " : "Updated · ")+age+"m ago · "+p.source;
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x08080080; return p; } }
    public Widget(bool preview, EventWaitHandle request, bool silent=false) {
        openRequest=request; SavedSettings=Settings.Clone();
        Text="Token Taskbar"; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; BackColor=Art.Bg;
        DoubleBuffered=true; Width=StripWidth; Height=44; Cursor=Cursors.Hand; AccessibleName="AI agent token usage";
        detail=new Detail(this);
        var menu=new ContextMenuStrip();
        menu.Items.Add("Usage details",null,(s,e)=>OpenDetails());
        menu.Items.Add("Settings",null,(s,e)=>OpenSettings());
        menu.Items.Add("Quit",null,(s,e)=>Quit()); ContextMenuStrip=menu;
        tray.Icon=trayIcon; tray.Text="Token Taskbar - click to open panel"; tray.ContextMenuStrip=menu; tray.Visible=true;
        tray.MouseClick+=(s,e)=> { if(e.Button==MouseButtons.Left) OpenDetails(); };


        Shown+=(s,e)=> {
            if(started) return;
            started=true;
            StartWorker();
            if(preview) { Location=new Point(160,160); detail.Open(new Rectangle(160,200,Width,Height),"Usage"); }
            else { Hide(); DockToTaskbar(); }
        };
        timer.Interval=1000; timer.Tick+=(s,e)=> {
            ticks++; RenderFrame();
            if(detail.Visible) {
                string next=Art.FrameKey(Data,Width,Height,false)+StatusFor(FindProvider("claude"))+StatusFor(FindProvider("codex"));
                if(next!=panelKey) {panelKey=next;detail.Invalidate();}
            }
            if(openRequest.WaitOne(0)) OpenDetails();
            if(!preview && (!IsHandleCreated || ticks%6==0)) DockToTaskbar();
            if((worker==null || worker.HasExited) && Art.Now-lastRestart>30) StartWorker();
            tray.Text=Settings.Enabled ? "Token Taskbar - click for panel / Settings" : "Token Taskbar (widget off) - click for panel";
        }; timer.Start();
    }
    double lastManualRefresh;
    public void RefreshUsage() {
        if(Art.Now-lastManualRefresh<15) return;
        lastManualRefresh=Art.Now;
        if(worker!=null) {try {if(!worker.HasExited) worker.Kill();} catch { }}
        StartWorker();workerError="Refreshing account readings…";detail.Invalidate();
    }
    void StartWorker() {
        if(worker!=null) {worker.Dispose();worker=null;}
        lastRestart=Art.Now; workerError="";
        try {
            worker=new Process { StartInfo=new ProcessStartInfo(Path.Combine(root,"TokenTaskbar.Collector.exe")) {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
                WorkingDirectory=root } };
            worker.OutputDataReceived+=(s,e)=> {
                if(String.IsNullOrEmpty(e.Data) || closing || !Object.ReferenceEquals(s,worker)) return;
                try { var data=new JavaScriptSerializer().Deserialize<Snapshot>(e.Data);
                    if(data==null || data.providers==null) return;
                    ui.Post(_=> {if(closing || IsDisposed || !Object.ReferenceEquals(s,worker)) return;Data=data;workerError="";RenderFrame(); if(detail.Visible) detail.Invalidate();},null);
                } catch { }
            };
            worker.ErrorDataReceived+=(s,e)=> { /* Do not persist raw runtime output or credentials. */ };
            worker.Start(); worker.BeginOutputReadLine(); worker.BeginErrorReadLine();
        } catch { workerError="Collector unavailable · retrying"; }
    }
    public void OpenSettings() {
        detail.Open(PanelAnchor(),"Settings");
    }
    // Shows settings on the taskbar without writing them.
    public void PreviewSettings(WidgetSettings value) {
        value.Normalize();Settings=value;
        renderedKey="";missingSince=0;scanning=false;scanGeneration++;settingsScan=true;
        SetPlacement("pending");DockToTaskbar();
    }
    public bool SaveSettings(WidgetSettings value) {
        try {value.Save(Path.Combine(root,"widget-settings.json"));} catch {return false;}
        SavedSettings=value.Clone();PreviewSettings(value.Clone());return true;
    }
    public void RevertSettings() { if(!Settings.SameAs(SavedSettings)) PreviewSettings(SavedSettings.Clone()); }
    void SetPlacement(string value) {
        if(Placement==value) return;
        Placement=value; if(PlacementChanged!=null) PlacementChanged(this,EventArgs.Empty);
    }
    Rectangle PanelAnchor() { return slot.IsEmpty ? new Rectangle(Screen.PrimaryScreen.WorkingArea.Right-400,Screen.PrimaryScreen.WorkingArea.Bottom,228,44) : slot; }
    void OpenDetails() { detail.Open(PanelAnchor(),"Usage"); }
    void DockToTaskbar() {
        if(scanning || closing) return;
        if(!Settings.Enabled) {
            // Turned off in Settings: stay running with only the tray icon.
            if(Visible) Hide(); slot=Rectangle.Empty; settingsScan=false;
            SetPlacement("off"); Diagnostic("Widget turned off in Settings; tray icon only"); return;
        }
        var bar=Native.FindWindow("Shell_TrayWnd",null);
        if(bar==IntPtr.Zero || !Native.IsWindowVisible(bar)) return;
        scanning=true; int generation=++scanGeneration;int pid=Process.GetCurrentProcess().Id;
        double dpi=Native.GetDpiForWindow(bar)/96.0;
        Native.RECT barBounds;Native.GetWindowRect(bar,out barBounds);
        double scale=Math.Min(Settings.SizePercent/100.0*dpi,(barBounds.Bottom-barBounds.Top)/38.0);
        int logicalWidth=Art.LogicalWidth(Settings.VisibleProviders(),Settings.ShowLogos);
        int wanted=Math.Max(1,(int)(logicalWidth*scale));
        int minimum=Math.Min(wanted,Math.Max(1,(int)(logicalWidth*0.70*dpi)));
        string preferred=Settings.Position;int offset=(int)(Settings.Offset*dpi);
        int target=global::Layout.Target(barBounds.Left,barBounds.Right,wanted,preferred,offset);
        // A scan caused by a settings change is deliberate, so skip the transition grace period.
        bool deliberate=settingsScan; settingsScan=false;
        Task.Run(()=> {
            Rectangle found=Rectangle.Empty;bool reliable=false;
            try { found=global::Layout.Find(bar,wanted,pid,preferred,offset,minimum);reliable=true; } catch { }
            if(closing) return;
            try { ui.Post(_=> {
                if(closing || IsDisposed || generation!=scanGeneration) return;
                scanning=false; if(!Native.IsWindow(bar)) return;
                if(!reliable) {Diagnostic("Taskbar transition: waiting for a valid layout; app remains running");return;}
                if(found.IsEmpty) {
                    if(missingSince==0) missingSince=Art.Now;
                    if(!deliberate && !slot.IsEmpty && Art.Now-missingSince<4) {Diagnostic("Taskbar transition: retaining last slot");return;}
                    Hide();slot=Rectangle.Empty;SetPlacement("tray");Diagnostic("No stable taskbar gap; tray fallback active");return;
                }
                missingSince=0;
                EffectiveSize=(int)Math.Round(found.Width/(logicalWidth*dpi)*100);
                SetPlacement(found.Width<wanted ? "fitted" : found.Left==target ? "ok" : "moved");
                slot=found; taskbar=bar; var host=Native.TaskbarContentHost(bar);
                if(Native.GetParent(Handle)!=host) {
                    long style=Native.GetStyle(Handle,-16).ToInt64();
                    Native.SetStyle(Handle,-16,new IntPtr((style & ~0x80000000L)|0x40000000L));
                    Native.SetParent(Handle,host);
                    if(Native.GetParent(Handle)!=host) {Hide();Diagnostic("Taskbar attachment failed");return;}
                }
                var position=new Native.POINT(found.Left,found.Top);Native.ScreenToClient(host,ref position);
                Native.SetWindowPos(Handle,IntPtr.Zero,position.X,position.Y,found.Width,found.Height,0x0010|0x0040);
                Show(); RenderFrame();
                Diagnostic("Transparent taskbar child; host="+Native.ClassName(host)+"; parent="+host+"; visible="+Native.IsWindowVisible(Handle)+"; bounds="+found+"; Claude: "+StatusFor(FindProvider("claude"))+"; Codex: "+StatusFor(FindProvider("codex")));
            },null); } catch { }
        });
    }
    void Diagnostic(string message) {
        if(message==diagnosticMessage) return;diagnosticMessage=message;
        try {File.WriteAllText(Path.Combine(root,"status.txt"),message);} catch { }
    }
    protected override void WndProc(ref Message message) {
        // Accept clicks without activating the embedded taskbar window.
        if(message.Msg==0x0021) {message.Result=new IntPtr(3);return;}
        // Layered-window alpha hit testing routes only painted pixels here.
        // Handle release directly: WinForms MouseClick may be lost after Explorer reparenting.
        if(message.Msg==0x0202) {
            ui.Post(_=> {if(!closing && !IsDisposed) OpenSettings();},null);
            message.Result=IntPtr.Zero;return;
        }
        base.WndProc(ref message);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { RenderFrame(); }
    protected override void OnHandleDestroyed(EventArgs e) {
        scanning=false;scanGeneration++;renderedKey="";
        if(!closing) {Diagnostic("Taskbar handle replaced; app remains running");Lifecycle("Taskbar handle replaced");}
        base.OnHandleDestroyed(e);
    }
    void RenderFrame() {
        if(closing || !IsHandleCreated || !Visible || Width<=0 || Height<=0) return;
        // Raise only within our taskbar parent. Never make the widget globally topmost.
        if(Native.GetParent(Handle)!=IntPtr.Zero && Native.GetWindow(Handle,3)!=IntPtr.Zero)
            Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0010);
        var shown=Settings.VisibleProviders();
        string tooltip=String.Join("\n",shown.Select(id=>(id=="claude" ? "Claude: " : "Codex: ")+StatusFor(FindProvider(id))))+"\nSession then weekly usage. Below: time left | local reset time.\n* Last-known quota. Click visible text, bars or icons for Settings.";
        if(tooltip!=tooltipText) {tooltipText=tooltip;tip.SetToolTip(this,tooltip);}
        string nextKey=Art.FrameKey(Data,Width,Height,false,shown,Settings.ShowLogos)+Handle.ToString()+slot.ToString();
        if(nextKey==renderedKey) return;
        using(var bitmap=Art.Frame(Data,Width,Height,false,shown,Settings.ShowLogos)) {
            IntPtr screen=Native.GetDC(IntPtr.Zero),memory=IntPtr.Zero,pixels=IntPtr.Zero,previous=IntPtr.Zero;
            try {
                memory=Native.CreateCompatibleDC(screen);pixels=bitmap.GetHbitmap(Color.FromArgb(0));
                previous=Native.SelectObject(memory,pixels);
                Native.RECT r;Native.GetWindowRect(Handle,out r);
                var destination=new Native.POINT(r.Left,r.Top);var origin=new Native.POINT(0,0);
                var parent=Native.GetParent(Handle);
                if(parent!=IntPtr.Zero && (Native.GetStyle(Handle,-16).ToInt64() & 0x40000000L)!=0)
                    Native.ScreenToClient(parent,ref destination);
                var size=new Native.SIZE(Width,Height);var blend=new Native.BLEND{Operation=0,Alpha=255,Format=1};
                if(!Native.UpdateLayeredWindow(Handle,screen,ref destination,ref size,memory,ref origin,0,ref blend,2))
                    Diagnostic("Transparent rendering failed: Win32 "+Marshal.GetLastWin32Error());
                else renderedKey=nextKey;
            } finally {
                if(previous!=IntPtr.Zero) Native.SelectObject(memory,previous);
                if(pixels!=IntPtr.Zero) Native.DeleteObject(pixels);
                if(memory!=IntPtr.Zero) Native.DeleteDC(memory);
                if(screen!=IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero,screen);
            }
        }

    }
    protected override void OnFormClosed(FormClosedEventArgs e) {
        Lifecycle("Window closed: "+e.CloseReason+"; requestedQuit="+QuitRequested);
        closing=true; timer.Stop(); tray.Visible=false; tray.Dispose(); trayIcon.Dispose(); tip.Dispose();
        if(worker!=null) { try { if(!worker.HasExited) worker.Kill(); } catch { } worker.Dispose(); }
        detail.Dispose(); base.OnFormClosed(e);
    }
    void Lifecycle(string message) {
        try {File.AppendAllText(Path.Combine(root,"lifecycle.log"),DateTime.UtcNow.ToString("o")+" pid="+Process.GetCurrentProcess().Id+" "+message+Environment.NewLine);} catch { }
    }
}

static class Program {
    [STAThread] static void Main(string[] args) {
        AppDomain.CurrentDomain.UnhandledException+=(s,e)=>File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.txt"),e.ExceptionObject.ToString());
        try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        if(args.Contains("--test-startup")) {
            bool previous=Startup.Enabled;
            try {
                Startup.SetEnabled(false);if(Startup.Enabled) throw new Exception("Startup disable failed");
                Startup.SetEnabled(true);if(!Startup.Enabled) throw new Exception("Startup enable failed");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-test.txt"),"PASS: startup toggle off/on; original state restored");
            } finally {Startup.SetEnabled(previous);}
            return;
        }
        if(args.Contains("--test")) {
            var occupied=new List<Rectangle> {new Rectangle(0,0,100,48),new Rectangle(500,0,500,48)};
            if(Layout.Gap(0,1000,0,48,228,occupied).Left!=112) throw new Exception("Gap selection failed");
            if(!Layout.Gap(0,1000,0,48,500,occupied).IsEmpty) throw new Exception("Overlap protection failed");
            var dualSpace=new List<Rectangle> {new Rectangle(0,0,100,48),new Rectangle(1100,0,100,48)};
            if(Layout.Gap(0,1200,0,48,Widget.StripWidth,dualSpace).Left!=112) throw new Exception("Dual-agent gap selection failed");
            if(!Layout.Gap(0,1000,0,48,Widget.StripWidth,occupied).IsEmpty) throw new Exception("Dual-agent overlap protection failed");
            var slots=new List<Rectangle>{new Rectangle(400,0,100,48)};
            if(Layout.PreferredGap(0,1000,0,48,200,slots,"Right",0).Left!=788) throw new Exception("Right position failed");
            if(Layout.PreferredGap(0,1000,0,48,200,slots,"Left",60).Left!=72) throw new Exception("Offset failed");
            var center=Layout.PreferredGap(0,1000,0,48,200,slots,"Center",0);
            if(slots.Any(r=>r.IntersectsWith(center))) throw new Exception("Position covers a button");
            var settingsPath=Path.Combine(Path.GetTempPath(),"TokenTaskbar-settings-test-"+Guid.NewGuid()+".json");
            try {
                var preferences=new WidgetSettings {SizePercent=80,Position="Right",Offset=-20};preferences.Save(settingsPath);
                var restored=WidgetSettings.Load(settingsPath);
                if(restored.SizePercent!=80 || restored.Position!="Right" || restored.Offset!=-20) throw new Exception("Settings did not persist");
                preferences.SizePercent=90;preferences.Save(settingsPath);
                if(WidgetSettings.Load(settingsPath).SizePercent!=90) throw new Exception("Settings update failed");
                File.WriteAllText(settingsPath,"invalid");
                if(WidgetSettings.Load(settingsPath).SizePercent!=100) throw new Exception("Settings recovery failed");
                var single=new WidgetSettings {ShowClaude=false,Offset=-2000,Enabled=false};single.Save(settingsPath);
                var reloaded=WidgetSettings.Load(settingsPath);
                if(reloaded.ShowClaude || !reloaded.ShowCodex || reloaded.VisibleProviders().Length!=1) throw new Exception("Provider visibility did not persist");
                if(reloaded.Offset!=-400) throw new Exception("Nudge was not limited to 400 px");
                if(reloaded.Enabled) throw new Exception("Turning the widget off did not persist");
                var plain=new WidgetSettings {ShowLogos=false};plain.Save(settingsPath);
                if(WidgetSettings.Load(settingsPath).ShowLogos || plain.SameAs(new WidgetSettings())) throw new Exception("Hiding logos did not persist");
                if(!reloaded.SameAs(reloaded.Clone()) || reloaded.SameAs(new WidgetSettings())) throw new Exception("Settings comparison failed");
                var neither=new WidgetSettings {ShowClaude=false,ShowCodex=false};neither.Normalize();
                if(!neither.ShowClaude || !neither.ShowCodex) throw new Exception("Hiding both providers was allowed");
                File.WriteAllText(settingsPath,"{\"SizePercent\": 85, \"Offset\": 0, \"Position\": \"Left\"}");
                var legacy=WidgetSettings.Load(settingsPath);
                if(!legacy.ShowClaude || !legacy.ShowCodex || !legacy.Enabled || legacy.SizePercent!=85) throw new Exception("Older settings file did not load");
            } finally {if(File.Exists(settingsPath)) File.Delete(settingsPath);}
            var fitBoxes=new List<Rectangle>{new Rectangle(700,0,300,48)};
            var fitted=Layout.FitGap(0,1000,0,48,808,565,fitBoxes,"Left",0);
            if(fitted.Width!=676 || fitted.Left!=12 || fitBoxes.Any(r=>r.IntersectsWith(fitted))) throw new Exception("Auto fit failed");
            if(!Layout.FitGap(0,1000,0,48,808,700,fitBoxes,"Left",0).IsEmpty) throw new Exception("Auto fit ignored minimum size");
            if(Layout.FitGap(0,1200,0,48,808,565,new List<Rectangle>(),"Right",0).Width!=808) throw new Exception("Auto fit enlarged/shrank an available full size");
            var colorWindow=new Quota {valid=true,reset=Art.Now+7200,used=100};
            if(Art.UsageColor(colorWindow,Color.Blue)!=Color.FromArgb(237,111,106)) throw new Exception("Full quota color failed");
            colorWindow.used=80;
            if(Art.UsageColor(colorWindow,Color.Blue)!=Color.FromArgb(237,111,106)) throw new Exception("80 percent red threshold failed");
            colorWindow.used=59;
            if(Art.UsageColor(colorWindow,Color.Blue)!=Color.Blue) throw new Exception("Below 60 percent provider color failed");
            colorWindow.used=60;
            if(Art.UsageColor(colorWindow,Color.Blue)!=Color.FromArgb(240,163,77)) throw new Exception("Warning quota color failed");
            using(var check=new Bitmap(100,40)) using(var graphics=Graphics.FromImage(check)) {
                if(graphics.MeasureString("100%*",Art.UiFont(16,true,true),1000,StringFormat.GenericTypographic).Width>56) throw new Exception("Full percentage text would clip");
            }
            var sample=new Snapshot {generated=Art.Now,providers=new[]{new Provider {id="claude",state="ok",windows=new[]{new Quota {valid=true,used=25,reset=Art.Now+7200}}}}};
            string original=Art.FrameKey(sample,626,48,false);
            sample.generated+=1;
            if(original!=Art.FrameKey(sample,626,48,false)) throw new Exception("Unchanged display redraws");
            sample.providers[0].windows[0].used=26;
            if(original==Art.FrameKey(sample,626,48,false)) throw new Exception("Quota change did not redraw");
            if(original==Art.FrameKey(sample,626,48,true)) throw new Exception("Hover change did not redraw");
            using(var bitmap=Art.Frame(null,Widget.StripWidth,48,false)) {
                if(bitmap.GetPixel(0,0).A!=0 || bitmap.GetPixel(Widget.StripWidth-1,47).A!=0) throw new Exception("Background is not transparent");
                if(bitmap.GetPixel(21,23).A==0) throw new Exception("Provider icon did not render");
                int blended=0;
                for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++) {int alpha=bitmap.GetPixel(x,y).A;if(alpha>0 && alpha<255) blended++;}
                if(blended<30) throw new Exception("Antialiasing did not render");
            }
            using(var bitmap=Art.Frame(null,939,72,false)) {
                if(bitmap.GetPixel(0,0).A!=0 || bitmap.GetPixel(938,71).A!=0) throw new Exception("Scaled transparency failed");
            }
            if(Art.LogicalWidth(new[]{"codex"})!=444 || Art.LogicalWidth(Art.AllProviders)!=Widget.StripWidth) throw new Exception("Strip width failed");
            if(Art.LogicalWidth(Art.AllProviders,false)!=Widget.StripWidth-2*Art.LogoSpace || Art.LogicalWidth(new[]{"claude"},false)!=444-Art.LogoSpace) throw new Exception("Logo-free width failed");
            if(Art.FrameKey(null,746,48,false,Art.AllProviders,false)==Art.FrameKey(null,746,48,false,Art.AllProviders,true)) throw new Exception("Logo change did not redraw");
            using(var bitmap=Art.Frame(null,746,48,false,Art.AllProviders,false)) {
                if(bitmap.GetPixel(0,0).A!=0 || bitmap.GetPixel(745,47).A!=0) throw new Exception("Logo-free transparency failed");
                for(int y=0;y<48;y++) for(int x=0;x<bitmap.Width;x++) {var c=bitmap.GetPixel(x,y);if(c.A>200 && c.R>180 && c.G<150 && c.B<120) throw new Exception("Logo drawn while hidden");}
            }
            if(Art.FrameKey(null,400,48,false,new[]{"codex"})==Art.FrameKey(null,400,48,false,new[]{"claude"})) throw new Exception("Provider change did not redraw");
            using(var bitmap=Art.Frame(null,400,48,false,new[]{"codex"})) {
                if(bitmap.GetPixel(0,0).A!=0 || bitmap.GetPixel(399,47).A!=0) throw new Exception("Single-provider transparency failed");
                bool icon=false;
                for(int y=12;y<34 && !icon;y++) for(int x=10;x<32;x++) if(bitmap.GetPixel(x,y).A>0) {icon=true;break;}
                if(!icon) throw new Exception("Single-provider icon did not render");
            }
            if(Layout.Target(0,1000,200,"Right",0)!=788 || Layout.Target(0,1000,200,"Left",60)!=72) throw new Exception("Placement target failed");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layout-test.txt"),"PASS: settings persistence, provider visibility, single-provider rendering, positions, offsets, overlap protection, transparency and scale rendering"); return;
        }
        if(args.Contains("--diagnose")) {
            var bar=Native.FindWindow("Shell_TrayWnd",null);var lines=new List<string>();
            lines.Add("Taskbar: "+bar+"; content host: "+Native.TaskbarContentHost(bar));
            Native.EnumChildWindows(bar,(h,p)=> {Native.RECT r;Native.GetWindowRect(h,out r);var parent=Native.GetParent(h);lines.Add(h+" parent="+parent+" parentClass="+Native.ClassName(parent)+" "+Native.ClassName(h)+" visible="+Native.IsWindowVisible(h)+" rect="+r.Left+","+r.Top+","+r.Right+","+r.Bottom);return true;},IntPtr.Zero);
            lines.Add("Free slot="+Layout.Find(bar,(int)(Widget.StripWidth*Native.GetDpiForWindow(bar)/96.0),Process.GetCurrentProcess().Id));
            File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"taskbar-host.txt"),lines);return;
        }
        bool created; using(var mutex=new Mutex(true,"Local\\TokenTaskbar-2026",out created))
        using(var request=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\TokenTaskbar-Open")) {
            if(!created) {request.Set();return;}
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            // The process lifetime is independent of Explorer's child HWND.
            using(var context=new ApplicationContext()) {
                Widget widget=null;
                bool recoveryTest=args.Contains("--test-recovery");
                var ui=new WindowsFormsSynchronizationContext();
                Action<Snapshot> create=null;
                create=data=> {
                    try {File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lifecycle.log"),DateTime.UtcNow.ToString("o")+" pid="+Process.GetCurrentProcess().Id+" Creating widget window"+Environment.NewLine);} catch { }
                    widget=new Widget(args.Contains("--preview"),request,recoveryTest){Data=data};
                    var instance=widget;
                    instance.FormClosed+=(s,e)=> {
                        if(instance.QuitRequested || e.CloseReason==CloseReason.ApplicationExitCall || e.CloseReason==CloseReason.WindowsShutDown) context.ExitThread();
                        else {var last=instance.Data;ui.Post(_=>create(last),null);}
                    };
                    instance.Show();
                };
                create(null);
                using(var probe=new System.Windows.Forms.Timer()) {
                    int step=0,repairs=0;
                    if(recoveryTest) {
                        probe.Interval=500;probe.Tick+=(s,e)=> {
                            step++;
                            if(step==8 || step==18 || step==28) {
                                if(widget==null || widget.IsDisposed || !widget.IsHandleCreated) throw new Exception("Widget disappeared before recovery probe");
                                if(!Native.DestroyWindow(widget.Handle)) throw new Exception("Could not replace owned widget handle");
                            }
                            if(step==16 || step==26 || step==36) {
                                var bar=Native.FindWindow("Shell_TrayWnd",null);
                                if(widget==null || widget.IsDisposed || !widget.IsHandleCreated || Native.GetParent(widget.Handle)!=Native.TaskbarContentHost(bar)) throw new Exception("Widget did not recover its taskbar host");
                                Native.RECT bounds,parent;Native.GetWindowRect(widget.Handle,out bounds);Native.GetWindowRect(bar,out parent);
                                if(bounds.Top<parent.Top || bounds.Bottom>parent.Bottom) throw new Exception("Recovered widget is outside taskbar");
                                repairs++;
                            }
                            if(step==37) {
                                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"recovery-test.txt"),"PASS: "+repairs+" owned-window replacements recovered in the same process; pid="+Process.GetCurrentProcess().Id);
                                widget.Quit();
                            }
                        };probe.Start();
                    }
                    Application.Run(context);
                }
                if(widget!=null && !widget.IsDisposed) widget.Dispose();
            }
        }
    }
}
