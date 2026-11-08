using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.UpdateHost;

/// <summary>View only; constructing it cannot access packages, elevate or control Silo.</summary>
public sealed class UpdateProgressWindow : Window
{
    public TextBlock Status { get; }
    public ProgressBar Progress { get; }
    public Button Cancel { get; }
    private readonly TextBlock explanation, speed, files, currentFile, total;
    private readonly Grid details;
    private readonly UpdateSpeedMeter meter=new(TimeSpan.FromSeconds(3));
    private readonly Stopwatch clock=Stopwatch.StartNew();
    public void Apply(UpdateHostMessage message)
    {
        if(message.Kind=="download"){ApplyDownload(message);return;}
        details.Visibility=Visibility.Collapsed;speed.Text="";meter.Reset();
        Status.Text=message.Text??"Preparing update…";
        Progress.IsIndeterminate=message.Total==0 && message.Kind!="failed";
        Progress.Value=message.Total>0 ? Math.Clamp(100d*message.Completed/message.Total,0,100) : 0;
        if(message.Kind=="failed"){explanation.Text="Your previous version is preserved. You can retry from Silo or use the full installer.";Cancel.Content="Close";Cancel.IsEnabled=true;}
    }
    // Counts and sizes cover only what is downloaded: changed files, or one complete package.
    private void ApplyDownload(UpdateHostMessage message)
    {
        Status.Text=UpdateProgressPresentation.DownloadingText;
        Progress.IsIndeterminate=message.Total==0;
        Progress.Value=message.Total>0 ? Math.Clamp(100d*message.Completed/message.Total,0,100) : 0;
        details.Visibility=Visibility.Visible;
        files.Text=UpdateProgressPresentation.Files(message.Files,message.FileCount);
        currentFile.Text=message.FileTotal>0 ? UpdateProgressPresentation.Size(message.FileBytes,message.FileTotal) : "—";
        total.Text=UpdateProgressPresentation.Size(message.Completed,message.Total);
        var rate=meter.Add(clock.Elapsed,message.Completed);
        speed.Text=rate is { } value && message.Completed<message.Total ? UpdateProgressPresentation.Speed(value) : "";
    }
    private readonly DispatcherTimer ownerTracking=new(){Interval=TimeSpan.FromMilliseconds(250)};
    public UpdateProgressWindow(bool restart,long owner)
    {
        Title="Updating Silo Desktop Player";
        try
        {
            // The client owns Assets; this assembly lives in its Updater subfolder.
            // Resolve from the loaded helper, not its caller's working directory.
            AppWindow.SetIcon(Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(UpdateProgressWindow).Assembly.Location)!, "..", "Assets", "app.ico")));
        }
        catch(Exception ex) { Console.Error.WriteLine($"Updater window icon unavailable: {ex.GetType().Name}"); }
        var white=new SolidColorBrush(Colors.White);var muted=new SolidColorBrush(Colors.LightGray);
        var panel=new StackPanel{Spacing=16,Padding=new Thickness(28),RequestedTheme=ElementTheme.Dark,
            Background=new SolidColorBrush(ColorHelper.FromArgb(255,24,27,32))};
        panel.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=white});
        var statusRow=new Grid();
        statusRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        statusRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        Status=new TextBlock{Text="Preparing your update…",FontSize=14,TextWrapping=TextWrapping.Wrap,Foreground=white};statusRow.Children.Add(Status);
        speed=new TextBlock{FontSize=14,Foreground=muted,Margin=new Thickness(12,0,0,0),HorizontalAlignment=HorizontalAlignment.Right};
        Grid.SetColumn(speed,1);statusRow.Children.Add(speed);panel.Children.Add(statusRow);
        Progress=new ProgressBar{Minimum=0,Maximum=100,IsIndeterminate=true,Foreground=new SolidColorBrush(ColorHelper.FromArgb(255,74,222,128))};panel.Children.Add(Progress);
        details=new Grid{RowSpacing=4,Visibility=Visibility.Collapsed};
        details.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        details.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        TextBlock Row(int row,string label)
        {
            details.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var name=new TextBlock{Text=label,FontSize=13,Foreground=muted};Grid.SetRow(name,row);details.Children.Add(name);
            var value=new TextBlock{FontSize=13,Foreground=white,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(value,row);Grid.SetColumn(value,1);details.Children.Add(value);
            return value;
        }
        files=Row(0,"Files");currentFile=Row(1,"Current file");total=Row(2,"Total");
        panel.Children.Add(details);
        explanation=new TextBlock{Text=restart ? "Silo stays open while the new version is installed. It will restart when everything is ready." : "Silo stays open until installation finishes, then closes. It won't reopen automatically.",
            FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=muted};panel.Children.Add(explanation);
        Cancel=new Button{Content="Cancel",HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(Cancel);Content=panel;
        if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsResizable=false;presenter.IsMaximizable=false;
            // Keep progress and Cancel accessible while Silo remains interactive, including during elevation.
            presenter.IsAlwaysOnTop=true;
        }
        AppWindow.TitleBar.BackgroundColor=ColorHelper.FromArgb(255,24,27,32);AppWindow.TitleBar.ForegroundColor=Colors.White;
        UpdateApplication.CenterWindow(this,owner);
        ownerTracking.Tick+=(_,_)=>UpdateApplication.CenterWindow(this,owner);
        ownerTracking.Start();Closed+=(_,_)=>ownerTracking.Stop();
    }
}
