using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services.Updates;
using Windows.Graphics;

namespace SiloPlayer.UpdateHost;

/// <summary>
/// The small update panel docked in the bottom-right corner of Silo's window.
/// View only; constructing it cannot access packages, elevate or control Silo.
/// </summary>
public sealed class UpdateProgressWindow : Window
{
    public TextBlock Status { get; }
    public ProgressBar Progress { get; }
    public TextBlock Percent { get; }
    public Button Cancel { get; }
    public HyperlinkButton Details { get; }
    public Button RestartNow { get; }
    public Button Later { get; }
    public UpdatePanelModel Model { get; } = new();
    private readonly TextBlock explanation, speed, files, currentFile, total, detailsLabel;
    private readonly FontIcon detailsGlyph;
    private readonly Grid details, progressRow, footer;
    private readonly StackPanel choice;
    private readonly Border root;
    private readonly IntPtr owner;
    private readonly UpdateSpeedMeter meter=new(TimeSpan.FromSeconds(3));
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private TaskCompletionSource<bool>? answer;

    public void Apply(UpdateHostMessage message)
    {
        if(message.Kind=="download"){ApplyDownload(message);return;}
        if(message.Kind=="failed"){ShowFailure(message.Text??"The update could not finish.");return;}
        speed.Text="—";meter.Reset();
        Status.Text=message.Text??"Preparing update…";
        SetProgress(message.Completed,message.Total);
        Refresh();
    }
    // Counts and sizes cover only what is downloaded: changed files, or one complete package.
    private void ApplyDownload(UpdateHostMessage message)
    {
        Status.Text=UpdateProgressPresentation.DownloadingText;
        SetProgress(message.Completed,message.Total);
        files.Text=UpdateProgressPresentation.Files(message.Files,message.FileCount);
        currentFile.Text=message.FileTotal>0 ? UpdateProgressPresentation.Size(message.FileBytes,message.FileTotal) : "—";
        total.Text=UpdateProgressPresentation.Size(message.Completed,message.Total);
        var rate=meter.Add(clock.Elapsed,message.Completed);
        speed.Text=rate is { } value && message.Completed<message.Total ? UpdateProgressPresentation.Speed(value) : "—";
        Refresh();
    }
    private void SetProgress(long completed,long totalBytes)
    {
        Progress.IsIndeterminate=totalBytes==0;
        Progress.Value=totalBytes>0 ? Math.Clamp(100d*completed/totalBytes,0,100) : 0;
        Percent.Text=UpdatePanelModel.Percent(completed,totalBytes)??"";
    }

    /// <summary>Everything is downloaded. Completes true for Restart now and false for Later.</summary>
    public Task<bool> AskRestartAsync(CancellationToken ct)
    {
        Model.Ready();
        Status.Text=UpdatePanelModel.ReadyTitle;explanation.Text=UpdatePanelModel.ReadyText;
        answer=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=answer;ct.Register(()=>pending.TrySetCanceled(ct));
        Refresh();
        return pending.Task;
    }
    private void Answer(bool restart)
    {
        if(answer is not { } pending || Model.Phase!=UpdatePanelPhase.Ready)return;
        if(restart)
        {
            Model.Restart();
            Status.Text="Preparing your update…";SetProgress(0,0);
        }
        else
        {
            Model.Defer();
            Status.Text="Update downloaded";explanation.Text=UpdatePanelModel.DeferredText;
            Cancel.Content="Close";Cancel.IsEnabled=true;
        }
        Refresh();
        pending.TrySetResult(restart);
    }
    public void ShowFinishing(string text)
    {
        Model.Finish();
        Status.Text=text;SetProgress(0,0);Cancel.IsEnabled=false;
        Refresh();
    }
    public void ShowFailure(string text)
    {
        Model.Fail();
        Status.Text=text;
        explanation.Text="Your previous version is preserved. You can retry from Silo or use the full installer.";
        Progress.IsIndeterminate=false;Progress.Value=0;Percent.Text="";
        Cancel.Content="Close";Cancel.IsEnabled=true;
        Refresh();
    }

    private void Refresh()
    {
        details.Visibility=Model.ShowsDetails ? Visibility.Visible : Visibility.Collapsed;
        progressRow.Visibility=Model.ShowsProgress ? Visibility.Visible : Visibility.Collapsed;
        Details.Visibility=Model.ShowsDetailsToggle ? Visibility.Visible : Visibility.Collapsed;
        detailsLabel.Text=Model.DetailsLabel;detailsGlyph.Glyph=Model.DetailsGlyph;
        AutomationProperties.SetName(Details,Model.ShowsDetails ? "Hide update details" : "Show update details");
        explanation.Visibility=Model.Phase is UpdatePanelPhase.Ready or UpdatePanelPhase.Deferred or UpdatePanelPhase.Failed ? Visibility.Visible : Visibility.Collapsed;
        choice.Visibility=Model.ShowsChoice ? Visibility.Visible : Visibility.Collapsed;
        footer.Visibility=Model.ShowsChoice ? Visibility.Collapsed : Visibility.Visible;
        Cancel.Visibility=Model.Phase==UpdatePanelPhase.Finishing ? Visibility.Collapsed : Visibility.Visible;
        layoutDirty=true;Track();
    }

    public UpdateProgressWindow(bool restart,long owner)
    {
        this.owner=(IntPtr)owner;
        if(this.owner!=IntPtr.Zero && GetWindowThreadProcessId(this.owner,out var ownerPid)!=0)ownerProcess=ownerPid;
        // Kept as the window title: tools and the startup smoke test identify the updater by it. The panel
        // has no WS_CAPTION, so read it with WM_GETTEXT rather than GetWindowText/Process.MainWindowTitle.
        Title="Updating Silo Desktop Player";
        try
        {
            // The client owns Assets; this assembly lives in its Updater subfolder.
            // Resolve from the loaded helper, not its caller's working directory.
            AppWindow.SetIcon(Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(UpdateProgressWindow).Assembly.Location)!, "..", "Assets", "app.ico")));
        }
        catch(Exception ex) { Console.Error.WriteLine($"Updater window icon unavailable: {ex.GetType().Name}"); }
        var background=ColorHelper.FromArgb(255,24,27,32);
        var white=new SolidColorBrush(Colors.White);var muted=new SolidColorBrush(ColorHelper.FromArgb(255,160,166,176));
        var green=ColorHelper.FromArgb(255,74,222,128);
        var panel=new StackPanel{Spacing=8};
        root=new Border{Child=panel,Padding=new Thickness(16,14,16,10),RequestedTheme=ElementTheme.Dark,VerticalAlignment=VerticalAlignment.Top,
            Background=new SolidColorBrush(background)};

        // Details sit above the status so the panel grows upward and Details stays under the pointer.
        details=new Grid{RowSpacing=4,ColumnSpacing=12,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,6)};
        details.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        details.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        TextBlock Row(int row,string label)
        {
            details.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var name=new TextBlock{Text=label,FontSize=12,Foreground=muted};Grid.SetRow(name,row);details.Children.Add(name);
            var value=new TextBlock{Text="—",FontSize=12,Foreground=white,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(value,row);Grid.SetColumn(value,1);details.Children.Add(value);
            return value;
        }
        files=Row(0,"Files");currentFile=Row(1,"Current file");total=Row(2,"Total");speed=Row(3,"Speed");
        details.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var note=new TextBlock{Text=restart ? "Silo stays open while the update is prepared, then asks before restarting." : "Silo stays open until installation finishes, then closes. It won't reopen automatically.",
            FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=muted,Margin=new Thickness(0,4,0,0)};
        Grid.SetRow(note,4);Grid.SetColumnSpan(note,2);details.Children.Add(note);
        panel.Children.Add(details);

        Status=new TextBlock{Text="Preparing your update…",FontSize=14,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Foreground=white};
        panel.Children.Add(Status);
        explanation=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=muted,Visibility=Visibility.Collapsed};
        panel.Children.Add(explanation);

        progressRow=new Grid{ColumnSpacing=10};
        progressRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        progressRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        Progress=new ProgressBar{Minimum=0,Maximum=100,IsIndeterminate=true,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(green)};
        progressRow.Children.Add(Progress);
        Percent=new TextBlock{FontSize=12,Foreground=white,MinWidth=32,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
        Grid.SetColumn(Percent,1);progressRow.Children.Add(Percent);
        panel.Children.Add(progressRow);

        footer=new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        detailsLabel=new TextBlock{Text="Details",FontSize=12};
        detailsGlyph=new FontIcon{Glyph=UpdatePanelModel.ExpandGlyph,FontSize=10,Margin=new Thickness(6,1,0,0)};
        var detailsContent=new StackPanel{Orientation=Orientation.Horizontal};detailsContent.Children.Add(detailsLabel);detailsContent.Children.Add(detailsGlyph);
        Details=new HyperlinkButton{Content=detailsContent,Padding=new Thickness(0,2,4,2),Foreground=muted,HorizontalAlignment=HorizontalAlignment.Left};
        Details.Click+=(_,_)=>{Model.ToggleDetails();Refresh();};
        footer.Children.Add(Details);
        Cancel=new Button{Content="Cancel",FontSize=12,Padding=new Thickness(8,2,8,3),MinHeight=0,HorizontalAlignment=HorizontalAlignment.Right};
        Quiet(Cancel);Grid.SetColumn(Cancel,1);footer.Children.Add(Cancel);
        panel.Children.Add(footer);

        choice=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Visibility=Visibility.Collapsed,Margin=new Thickness(0,4,0,4)};
        Later=new Button{Content="Later",FontSize=13,Padding=new Thickness(14,5,14,6)};
        RestartNow=new Button{Content="Restart now",FontSize=13,Padding=new Thickness(14,5,14,6),
            Foreground=new SolidColorBrush(ColorHelper.FromArgb(255,11,15,20)),Background=new SolidColorBrush(green)};
        foreach(var (key,value) in new[]{("ButtonBackgroundPointerOver",ColorHelper.FromArgb(255,94,234,148)),("ButtonBackgroundPressed",ColorHelper.FromArgb(255,52,199,105)),
            ("ButtonForegroundPointerOver",ColorHelper.FromArgb(255,11,15,20)),("ButtonForegroundPressed",ColorHelper.FromArgb(255,11,15,20))})
            RestartNow.Resources[key]=new SolidColorBrush(value);
        Later.Click+=(_,_)=>Answer(false);RestartNow.Click+=(_,_)=>Answer(true);
        choice.Children.Add(Later);choice.Children.Add(RestartNow);
        panel.Children.Add(choice);
        Content=root;

        AppWindow.IsShownInSwitchers=false;
        if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;
            // A frameless panel, so its window edge is its visible edge and it docks exactly. Topmost
            // only while it is shown, which is only while Silo is in front, so it never covers other apps.
            presenter.SetBorderAndTitleBar(false,false);
            presenter.IsAlwaysOnTop=true;
        }
        // Windows 11 draws the outline itself, following the rounded corners, like its own flyouts.
        // Older Windows rejects the requests, so the panel then draws its own 1px outline (see UpdateOutline).
        var self=WinRT.Interop.WindowNative.GetWindowHandle(this);
        // A frameless WinUI window still keeps a classic 3px frame, painted light grey inside the
        // outline. Drop the frame styles so the panel's own background meets the outline.
        SetWindowLongPtr(self,-16,(IntPtr)(GetWindowLongPtr(self,-16).ToInt64() & ~0x00C40000L)); // WS_BORDER|WS_DLGFRAME|WS_THICKFRAME
        SetWindowLongPtr(self,-20,(IntPtr)(GetWindowLongPtr(self,-20).ToInt64() & ~0x00020301L)); // WS_EX_DLGMODALFRAME|WINDOWEDGE|CLIENTEDGE|STATICEDGE
        SetWindowPos(self,IntPtr.Zero,0,0,0,0,0x0237); // SWP_FRAMECHANGED|NOMOVE|NOSIZE|NOZORDER|NOACTIVATE|NOOWNERZORDER
        var round=2;outlineRounded=DwmSetWindowAttribute(self,33,ref round,sizeof(int))>=0;
        var outline=0x00473F3A;outlineColored=DwmSetWindowAttribute(self,34,ref outline,sizeof(int))>=0; // #3A3F47 as COLORREF (0x00BBGGRR).
        root.SizeChanged+=(_,_)=>{layoutDirty=true;Track();};
        Track();
        tracking.Tick+=(_,_)=>Track();
        tracking.Start();
        winEvent=OnWinEvent;
        if(ownerProcess!=0)
        {
            // Follow moves and resizes as they happen; the timer remains a fallback.
            hooks.Add(SetWinEventHook(EventObjectShow,EventObjectHide,IntPtr.Zero,winEvent,ownerProcess,0,0));
            hooks.Add(SetWinEventHook(EventObjectLocationChange,EventObjectLocationChange,IntPtr.Zero,winEvent,ownerProcess,0,0));
        }
        hooks.Add(SetWinEventHook(EventSystemForeground,EventSystemForeground,IntPtr.Zero,winEvent,0,0,0));
        hooks.Add(SetWinEventHook(EventSystemMinimizeStart,EventSystemMinimizeEnd,IntPtr.Zero,winEvent,0,0,0));
        Closed+=(_,_)=>{tracking.Stop();foreach(var hook in hooks)if(hook!=IntPtr.Zero)UnhookWinEvent(hook);hooks.Clear();};
    }

    private static void Quiet(Button button)
    {
        var clear=new SolidColorBrush(Colors.Transparent);
        button.Background=clear;button.BorderThickness=new Thickness(0);
        button.Foreground=new SolidColorBrush(ColorHelper.FromArgb(255,200,205,212));
        button.Resources["ButtonBackgroundPointerOver"]=new SolidColorBrush(ColorHelper.FromArgb(40,255,255,255));
        button.Resources["ButtonBackgroundPressed"]=new SolidColorBrush(ColorHelper.FromArgb(24,255,255,255));
        button.Resources["ButtonBackgroundDisabled"]=clear;
    }

    // ---- Docking and visibility (native physical pixels) ----
    private readonly DispatcherTimer tracking=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly List<IntPtr> hooks=[];
    private readonly WinEventProc winEvent;
    private uint ownerProcess;
    private bool layoutDirty=true;
    private bool outlineRounded,outlineColored;
    private bool? dwmOutline;
    private bool outlineTraced;
    private PanelRect? anchor;
    private SizeInt32 clientSize;
    private RectInt32 lastBounds;
    /// <summary>Fixtures that render the panel off-screen keep it shown; placement still follows the owner.</summary>
    public bool ManagesVisibility { get; set; } = true;

    private void OnWinEvent(IntPtr hook,uint eventType,IntPtr hwnd,int idObject,int idChild,uint thread,uint time)
    {
        if(idObject!=0)return; // Window objects only, never the caret or cursor.
        // Silo's own events: only its main window and its video surface matter.
        if(eventType is EventObjectShow or EventObjectHide or EventObjectLocationChange && hwnd!=owner && !IsVideoSurface(hwnd))return;
        if(eventType is EventSystemMinimizeStart or EventSystemMinimizeEnd && hwnd!=owner)return;
        Track();
    }

    private void Track()
    {
        try
        {
            var self=WinRT.Interop.WindowNative.GetWindowHandle(this);
            UpdateOutline(self);
            var scale=root.XamlRoot?.RasterizationScale ?? Math.Max(1,(owner!=IntPtr.Zero ? GetDpiForWindow(owner) : GetDpiForWindow(self))/96d);
            if(layoutDirty)
            {
                root.Measure(new Windows.Foundation.Size(UpdatePanelPlacement.PanelWidth,double.PositiveInfinity));
                var height=Math.Max(64,root.DesiredSize.Height);
                var size=new SizeInt32((int)Math.Ceiling(UpdatePanelPlacement.PanelWidth*scale),(int)Math.Ceiling(height*scale));
                if(size.Width!=clientSize.Width || size.Height!=clientSize.Height){AppWindow.ResizeClient(size);clientSize=size;}
                layoutDirty=false;
            }
            var outer=AppWindow.Size;
            var ownerAlive=owner!=IntPtr.Zero && IsWindow(owner);
            var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            var hasMonitor=GetMonitorInfo(MonitorFromWindow(ownerAlive ? owner : self,2),ref monitor);
            var work=hasMonitor ? new PanelRect(monitor.Work.Left,monitor.Work.Top,monitor.Work.Right,monitor.Work.Bottom) : new PanelRect(0,0,outer.Width,outer.Height);
            var playback=false;var minimized=false;
            if(ownerAlive && GetClientRect(owner,out var rect))
            {
                var origin=new Point();
                minimized=IsIconic(owner) || !IsWindowVisible(owner);
                if(!minimized && ClientToScreen(owner,ref origin))
                {
                    var client=new PanelRect(origin.X,origin.Y,origin.X+rect.Right,origin.Y+rect.Bottom);
                    anchor=client;
                    playback=hasMonitor && PlaybackCovers(client,new PanelRect(monitor.Monitor.Left,monitor.Monitor.Top,monitor.Monitor.Right,monitor.Monitor.Bottom));
                }
            }
            // No Silo window yet, or it already closed to restart: keep the last corner (or the work
            // area's), so a taller final status still grows upward inside the screen.
            anchor??=work;
            var (x,y)=UpdatePanelPlacement.DockBottomRight(anchor.Value,outer.Width,outer.Height,(int)Math.Round(UpdatePanelPlacement.Margin*scale),work);
            var bounds=new RectInt32(x,y,outer.Width,outer.Height);
            if(!bounds.Equals(lastBounds)){AppWindow.Move(new PointInt32(x,y));lastBounds=bounds;}
            var foreground=GetForegroundWindow();
            bool? silo=foreground==IntPtr.Zero ? null
                : GetWindowThreadProcessId(foreground,out var pid)!=0 && (pid==ownerProcess || pid==(uint)Environment.ProcessId);
            if(!ManagesVisibility)return;
            var show=UpdatePanelPlacement.ShouldShow(ownerAlive,minimized,playback,silo,AppWindow.IsVisible);
            if(show && !AppWindow.IsVisible)AppWindow.Show(false);
            else if(!show && AppWindow.IsVisible)AppWindow.Hide();
        }
        catch(Exception ex){Console.Error.WriteLine($"Update panel placement failed: {ex.GetType().Name}");}
    }

    // Windows 11 draws a border in DWMWA_BORDER_COLOR around every window it rounds, frame styles or not
    // (DWMWA_COLOR_NONE is the documented way to suppress it), so DWM accepting both requests decides the
    // path. DWMWA_VISIBLE_FRAME_BORDER_THICKNESS describes the frame this window no longer has, so it never
    // decides anything; it is only traced, with the path taken, once the panel is first shown.
    // The fallback draws the same colour inside the client area, rounded by XAML (DWMWCP_ROUND is 8px).
    private void UpdateOutline(IntPtr self)
    {
        if(dwmOutline is null)
        {
            var drawn=outlineRounded && outlineColored;
            dwmOutline=drawn;
            root.BorderThickness=new Thickness(drawn ? 0 : 1);
            root.BorderBrush=drawn ? null : new SolidColorBrush(ColorHelper.FromArgb(255,58,63,71));
            root.CornerRadius=new CornerRadius(!drawn && outlineRounded ? 8 : 0);
            layoutDirty=true;
        }
        if(outlineTraced || !AppWindow.IsVisible)return;
        outlineTraced=true;
        // Not stderr: the startup smoke test treats any stderr output as a failure.
        var probed=DwmGetWindowAttribute(self,37,out var border,sizeof(int))>=0; // DWMWA_VISIBLE_FRAME_BORDER_THICKNESS
        Trace.WriteLine($"Update panel outline: {(dwmOutline==true ? "DWM" : "app")} (round {outlineRounded}, colour {outlineColored}, DWM frame border {(probed ? $"{border}px" : "unavailable")})");
    }

    private bool IsVideoSurface(IntPtr hwnd)
    {
        if(hwnd==IntPtr.Zero || ownerProcess==0 || GetWindowThreadProcessId(hwnd,out var pid)==0 || pid!=ownerProcess)return false;
        var name=new System.Text.StringBuilder(32);
        return GetClassName(hwnd,name,name.Capacity)>0 && name.ToString()=="SiloMpvHost";
    }

    // Silo plays video in its own "SiloMpvHost" window over the main window (or the whole monitor).
    private bool PlaybackCovers(PanelRect client,PanelRect monitor)
    {
        for(var hwnd=FindWindowEx(IntPtr.Zero,IntPtr.Zero,"SiloMpvHost",null);hwnd!=IntPtr.Zero;hwnd=FindWindowEx(IntPtr.Zero,hwnd,"SiloMpvHost",null))
        {
            if(GetWindowThreadProcessId(hwnd,out var pid)==0 || pid!=ownerProcess || !IsWindowVisible(hwnd) || IsIconic(hwnd) || !GetWindowRect(hwnd,out var r))continue;
            if(UpdatePanelPlacement.IsPlaybackSurface(new PanelRect(r.Left,r.Top,r.Right,r.Bottom),client,monitor))return true;
        }
        return false;
    }

    private const uint EventSystemForeground=0x0003,EventSystemMinimizeStart=0x0016,EventSystemMinimizeEnd=0x0017,
        EventObjectShow=0x8002,EventObjectHide=0x8003,EventObjectLocationChange=0x800B;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventProc(IntPtr hook,uint eventType,IntPtr hwnd,int idObject,int idChild,uint thread,uint time);
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc proc,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr hwnd,ref Point point);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string? title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder name,int capacity);
}
