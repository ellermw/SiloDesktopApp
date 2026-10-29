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
    private readonly TextBlock explanation;
    public void Apply(UpdateHostMessage message)
    {
        Status.Text=message.Text??"Preparing update…";
        Progress.IsIndeterminate=message.Total==0 && message.Kind!="failed";
        Progress.Value=message.Total>0 ? Math.Clamp(100d*message.Completed/message.Total,0,100) : 0;
        if(message.Kind=="failed"){explanation.Text="Your previous version is preserved. You can retry from Silo or use the full installer.";Cancel.Content="Close";Cancel.IsEnabled=true;}
    }
    private readonly DispatcherTimer ownerTracking=new(){Interval=TimeSpan.FromMilliseconds(250)};
    public UpdateProgressWindow(bool restart,long owner)
    {
        Title="Updating Silo";
        var panel=new StackPanel{Spacing=18,Padding=new Thickness(28),RequestedTheme=ElementTheme.Dark,
            Background=new SolidColorBrush(ColorHelper.FromArgb(255,24,27,32))};
        panel.Children.Add(new TextBlock{Text="Updating Silo",FontSize=24,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=new SolidColorBrush(Colors.White)});
        Status=new TextBlock{Text="Preparing your update…",FontSize=14,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Colors.White)};panel.Children.Add(Status);
        Progress=new ProgressBar{Minimum=0,Maximum=100,IsIndeterminate=true,Foreground=new SolidColorBrush(ColorHelper.FromArgb(255,74,222,128))};panel.Children.Add(Progress);
        explanation=new TextBlock{Text=restart ? "Silo stays open while the new version is installed. It will restart when everything is ready." : "Silo stays open until installation finishes, then closes. It won't reopen automatically.",
            FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Colors.LightGray)};panel.Children.Add(explanation);
        Cancel=new Button{Content="Cancel",HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(Cancel);Content=panel;
        if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter){presenter.IsResizable=false;presenter.IsMaximizable=false;}
        AppWindow.TitleBar.BackgroundColor=ColorHelper.FromArgb(255,24,27,32);AppWindow.TitleBar.ForegroundColor=Colors.White;
        UpdateApplication.CenterWindow(this,owner);
        ownerTracking.Tick+=(_,_)=>UpdateApplication.CenterWindow(this,owner);
        ownerTracking.Start();Closed+=(_,_)=>ownerTracking.Stop();
    }
}
