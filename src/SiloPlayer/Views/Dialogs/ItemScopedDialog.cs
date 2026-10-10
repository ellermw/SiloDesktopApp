using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views.Dialogs;

/// <summary>One captured item/profile authority, bounded content and real native dialog commands.</summary>
public abstract class ItemScopedDialog : ContentDialog
{
    protected readonly SiloApiClient Client = App.Services.GetRequiredService<SiloApiClient>();
    protected readonly AuthService Auth = App.Services.GetRequiredService<AuthService>();
    protected readonly ApiRequestContext Context;
    protected readonly CancellationTokenSource Lifetime = new();
    protected readonly StackPanel Body = new() { Spacing = 16 };
    protected readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    protected readonly string ItemId;
    private readonly Func<AuthService, bool> _authorized;
    private readonly double _width;
    private readonly double _maxHeightRatio;
    private readonly ScrollViewer _scroll;
    private readonly Button _cornerClose;
    protected bool Busy;
    protected bool Ready;
    private CancellationTokenRegistration _navigationRegistration;
    protected bool CanAct => !Lifetime.IsCancellationRequested && Client.IsCurrentContext(Context) && _authorized(Auth);
    public bool HasSaved { get; protected set; }

    protected ItemScopedDialog(string itemId, string title, string description, double width, string primary, Func<AuthService, bool> authorized, double maxHeightRatio = 1)
    {
        ItemId = itemId; Context = Client.CaptureContext(); _authorized = authorized; _width = width; _maxHeightRatio = maxHeightRatio;
        var header = new Grid(); var titleText = Text(title, 18); titleText.FontWeight = FontWeights.SemiBold;
        header.Children.Add(titleText); Title = header;
        Status.Visibility = Visibility.Collapsed;
        Status.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Status.Visibility = string.IsNullOrEmpty(Status.Text) ? Visibility.Collapsed : Visibility.Visible);
        Body.Children.Add(Text(description, 14, true)); Body.Children.Add(Status);
        _scroll = new() { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Content = _scroll; PrimaryButtonText = primary; CloseButtonText = "Cancel"; DefaultButton = ContentDialogButton.Primary; IsPrimaryButtonEnabled = false;
        EditorDialogPresentation.Configure(this, width, new Thickness(24));
        var close = _cornerClose = EditorDialogPresentation.CornerClose(this);
        _scroll.Loaded += (_, _) => { EditorDialogPresentation.PlaceCornerClose(this, header, close); Reflow(); };
        Opened += async (_, _) =>
        {
            EditorDialogPresentation.PlaceCornerClose(this, header, close); if (XamlRoot != null) XamlRoot.Changed += RootChanged;
            Reflow(); await LoadSafelyAsync();
        };
        PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            if (Busy || !Ready || !CanAct) { deferral.Complete(); return; }
            try { await RunActionAsync(SubmitAsync); } finally { deferral.Complete(); }
        };
        Closing += (_, args) => { if (Busy) args.Cancel = true; };
        Closed += (_, _) => { Lifetime.Cancel(); _navigationRegistration.Dispose(); if (XamlRoot != null) XamlRoot.Changed -= RootChanged; };
    }
    public void BindNavigation(CancellationToken token)
    {
        _navigationRegistration.Dispose();
        _navigationRegistration = token.Register(() => { Lifetime.Cancel(); DispatcherQueue.TryEnqueue(() => { if (!Busy) Hide(); }); });
    }
    protected static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    protected static TextBlock Text(string value, double size = 14, bool muted = false)
        => new() { Text = value, FontSize = size, LineHeight = size * 1.45, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap, Foreground = Brush(muted ? "SecondaryTextBrush" : "PrimaryTextBrush") };
    protected void RequireAuthority() { Lifetime.Token.ThrowIfCancellationRequested(); if (!CanAct) throw new OperationCanceledException("Media action authority changed.", Lifetime.Token); }
    protected async Task LoadSafelyAsync()
    {
        Ready = false; SetEditing(false); Status.Text = "Loading…";
        try { RequireAuthority(); await LoadAsync(); RequireAuthority(); Ready = true; Status.Text = ""; }
        catch (OperationCanceledException) { if (!Lifetime.IsCancellationRequested) Status.Text = "The selected profile or item changed. Close and reopen this dialog."; }
        catch (Exception error) { Status.Text = error.Message; }
        SetEditing(Ready && CanAct); UpdateCommands();
    }
    protected virtual void UpdateCommands() => IsPrimaryButtonEnabled = Ready && CanAct && !Busy;
    protected async Task RunActionAsync(Func<Task> action)
    {
        if (Busy || !Ready || !CanAct) return;
        Busy = true; IsPrimaryButtonEnabled = false; SetCloseEnabled(false); _cornerClose.IsEnabled = false; SetEditing(false);
        try { RequireAuthority(); await action(); }
        catch (OperationCanceledException) { if (!Lifetime.IsCancellationRequested) Status.Text = "The selected profile or item changed. Close and reopen this dialog."; }
        catch (Exception error) { Status.Text = error.Message; LocalLog.AppendLine("media_detail_errors.txt", $"action={GetType().Name} | error={error.GetType().Name} | status={(error as ApiException)?.StatusCode}"); }
        finally { Busy = false; SetCloseEnabled(true); _cornerClose.IsEnabled = true; SetEditing(Ready && CanAct); UpdateCommands(); if (Lifetime.IsCancellationRequested) DispatcherQueue.TryEnqueue(Hide); }
    }
    protected virtual void SetEditing(bool enabled) { }
    private void SetCloseEnabled(bool enabled)
    {
        static void Walk(DependencyObject node, bool value)
        {
            if (node is Button { Name: "CloseButton" } button) button.IsEnabled = value;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i), value);
        }
        Walk(this, enabled);
    }
    protected abstract Task LoadAsync();
    protected abstract Task SubmitAsync();
    private void RootChanged(XamlRoot root, XamlRootChangedEventArgs args) => Reflow();
    private void Reflow()
    {
        var viewport = XamlRoot?.Size ?? new Windows.Foundation.Size(900, 800);
        var width = Math.Min(_width, Math.Max(180, viewport.Width - 32)); _scroll.Width = width - 50; Body.Width = double.NaN;
        if (Title is Grid header) header.Width = _scroll.Width;
        // Reserve the native title, padding and pinned36px command area;
        // constraining only the outer template doesn't constrain its ScrollViewer.
        var dialogHeight = Math.Min(viewport.Height - 64, viewport.Height * _maxHeightRatio);
        _scroll.MaxHeight = Math.Max(96, dialogHeight - 180);
        EditorDialogPresentation.ReflowCommands(this, viewport.Width < 640, new Thickness(24), buttonHeight: 36);
    }
}
