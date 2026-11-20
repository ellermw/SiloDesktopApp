using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Shared native shell gutter, page/dialog lifetime and explicit discard confirmation.</summary>
internal sealed class AdminAccessPageUi
{
    internal readonly StackPanel ScrollContent = new();
    internal readonly Grid Root = new();
    internal readonly StackPanel Body = new() { Spacing = 20 };
    internal readonly StackPanel Actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    internal readonly StackPanel Fields = new() { Spacing = 20 };
    internal readonly TextBlock Feedback = U.Text("", 13);
    internal readonly ScrollViewer Scroller = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    internal readonly Button Reload = U.Action("Reload", "AccessReload");
    internal ContentDialog? Dialog { get; private set; }
    private readonly Page _page;
    private readonly AdminAccessController _controller;
    private readonly Action _render;
    private bool _active;
    internal bool Active => _active;
    internal AdminAccessPageUi(Page page, AdminAccessController controller, string title, string subtitle, Action render)
    {
        _page = page; _controller = controller; _render = render;
        Body.Children.Add(U.Title(title)); if (subtitle.Length > 0) Body.Children.Add(U.Text(subtitle, 14, true));
        Body.Children.Add(Actions); Body.Children.Add(Feedback); Body.Children.Add(Reload); Body.Children.Add(Fields);
        AutomationProperties.SetLiveSetting(Feedback, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(Scroller, title); Body.Margin = new(0, 0, 0, 32);
        ScrollContent.Children.Add(Body); Scroller.Content = ScrollContent; Root.Children.Add(Scroller); page.Content = Root;
        Scroller.SizeChanged += (_, _) => Fit(); page.Loaded += (_, _) => { _active = true; Fit(); };
        Reload.Click += async (_, _) => { if (RequiresDiscard?.Invoke() == true && !await ConfirmDiscardAsync()) return; await Reopen!(); };
        controller.Changed += QueueRender;
    }
    internal Func<Task>? Reopen { get; set; }
    internal Func<bool>? RequiresDiscard { get; set; }
    internal void Fit()
    {
        var width = Scroller.ActualWidth - ScrollContent.Margin.Left - ScrollContent.Margin.Right;
        if (width > 0) ScrollContent.Width = Math.Min(ScrollContent.MaxWidth, width);
        Actions.Orientation = width is > 0 and < 560 ? Orientation.Vertical : Orientation.Horizontal;
    }
    private void QueueRender()
    {
        _page.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active) return;
            Feedback.Text = _controller.Busy ? "Loading…" : _controller.Message ?? "";
            Feedback.Visibility = Feedback.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            Reload.IsEnabled = !_controller.Busy;
            if (!_controller.Current) { Dialog?.Hide(); ClearPrivateControls(Fields); Fields.Children.Clear(); }
            _render();
        });
    }
    internal async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        var owner = _controller.Owner;
        if (!_active || owner is not { IsCurrent: true } || Dialog != null) return ContentDialogResult.None;
        Dialog = dialog; dialog.XamlRoot = _page.XamlRoot;
        using var retirement = owner.Token.Register(() => _page.DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try { var result = await dialog.ShowAsync(); return _active && ReferenceEquals(owner, _controller.Owner) && owner.IsCurrent ? result : ContentDialogResult.None; }
        finally { ClearPrivateControls(dialog); dialog.Content = null; if (ReferenceEquals(Dialog, dialog)) Dialog = null; }
    }
    internal async Task<bool> ConfirmAsync(string title, string description, string action)
    {
        var dialog = new ContentDialog { Title = title, Content = U.Text(description), PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }
    internal Task<bool> ConfirmDiscardAsync() => ConfirmAsync("Unsaved changes", "You have unsaved changes. Discard them and leave?", "Discard");
    internal void Close()
    {
        _active = false; Dialog?.Hide(); if (Dialog != null) ClearPrivateControls(Dialog);
        ClearPrivateControls(Fields); _controller.Suspend(); _controller.Changed -= QueueRender;
    }
    internal static void ClearPrivateControls(DependencyObject root)
    {
        if (root is PasswordBox password) password.Password = "";
        if (root is TextBox input) input.Text = "";
        if (root is TextBlock text && AutomationProperties.GetAutomationId(text) == "ApiKeySecret") text.Text = "";
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ClearPrivateControls(VisualTreeHelper.GetChild(root, i));
    }
    internal static StackPanel Stack(params UIElement[] children) { var result = new StackPanel { Spacing = 12 }; foreach (var child in children) result.Children.Add(child); return result; }
    internal static string Selected(ComboBox input) => (input.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    internal static void Select(ComboBox input, string value) => input.SelectedItem = input.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == value);
    internal static TextBox Multiline(string label, string id, string value, int height = 220)
    {
        var input = U.Input(label, id); input.Text = value; input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap;
        input.Width = double.NaN; input.Height = height; input.HorizontalAlignment = HorizontalAlignment.Stretch; input.IsSpellCheckEnabled = false; input.IsTextPredictionEnabled = false; return input;
    }
}
