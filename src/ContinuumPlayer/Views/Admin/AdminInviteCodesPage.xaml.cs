using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminInviteCodesPage : Page
{
    public AdminInviteCodesViewModel ViewModel { get; }
    private bool _rebuildPending;

    public AdminInviteCodesPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminInviteCodesViewModel>();
        this.InitializeComponent();
    }

    private bool _suppressSignupToggle;

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.InviteCodes.CollectionChanged += (_, _) => ScheduleRebuild();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AdminInviteCodesViewModel.SignupEnabled))
            {
                _suppressSignupToggle = true;
                SignupToggle.IsOn = ViewModel.SignupEnabled;
                _suppressSignupToggle = false;
            }
        };
        try { await ViewModel.LoadCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }
    }

    private async void SignupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSignupToggle) return;
        await ViewModel.SetSignupEnabledAsync(SignupToggle.IsOn);
        if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() => { _rebuildPending = false; RebuildRows(); });
    }

    private void RebuildRows()
    {
        CodesPanel.Children.Clear();
        if (ViewModel.InviteCodes.Count == 0) { EmptyState.Visibility = Visibility.Visible; return; }
        EmptyState.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var code in ViewModel.InviteCodes)
        {
            if (!first) CodesPanel.Children.Add(new Border
            {
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 1, 0, 0)
            });
            first = false;
            CodesPanel.Children.Add(BuildRow(code));
        }
    }

    private FrameworkElement BuildRow(InviteCode code)
    {
        var row = new Grid { Padding = new Thickness(20, 14, 20, 14), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        // Code (monospace)
        var codeBorder = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left
        };
        codeBorder.Child = new TextBlock
        {
            Text = code.Code, FontSize = 12,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        // Code cell: badge + copy button in a horizontal stack
        var codeCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        codeCell.Children.Add(codeBorder);
        var codeText = code.Code;
        var copyBtn = new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = "\uE8C8", FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] },
        };
        ToolTipService.SetToolTip(copyBtn, "Copy code");
        copyBtn.Click += async (_, _) =>
        {
            try
            {
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(codeText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                // Brief visual feedback
                copyBtn.Content = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)) };
                await Task.Delay(1500);
                copyBtn.Content = new FontIcon { Glyph = "\uE8C8", FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] };
            }
            catch { }
        };
        codeCell.Children.Add(copyBtn);
        Grid.SetColumn(codeCell, 0); row.Children.Add(codeCell);

        var label = new TextBlock
        {
            Text = string.IsNullOrEmpty(code.Label) ? "-" : code.Label,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(label, 1); row.Children.Add(label);

        // Usage: combined "used / max" — matches web InviteCodesTab
        bool maxedOut = code.MaxUses > 0 && code.UseCount >= code.MaxUses;
        var usageText = code.MaxUses > 0
            ? $"{code.UseCount} / {code.MaxUses}"
            : $"{code.UseCount} / \u221E";
        var usage = new TextBlock
        {
            Text = usageText, FontSize = 13,
            Foreground = maxedOut
                ? (SolidColorBrush)Application.Current.Resources["ErrorBrush"]
                : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(usage, 2); row.Children.Add(usage);

        // Webui has an inline Switch + dynamic "Enabled"/"Disabled" label in the status cell.
        var statusSwitch = new ToggleSwitch
        {
            IsOn = code.Enabled,
            OnContent = "Active",
            OffContent = "Disabled",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var capturedForToggle = code;
        statusSwitch.Toggled += async (_, _) =>
        {
            await ViewModel.ToggleInviteCodeCommand.ExecuteAsync(capturedForToggle);
        };
        Grid.SetColumn(statusSwitch, 3); row.Children.Add(statusSwitch);

        string createdText = "\u2014";
        if (!string.IsNullOrEmpty(code.CreatedAt) && DateTime.TryParse(code.CreatedAt, out var dt))
            createdText = dt.ToLocalTime().ToString("d");
        var created = new TextBlock
        {
            Text = createdText, FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(created, 4); row.Children.Add(created);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var capturedCode = code;

        // Top up
        var topUpBtn = MakeIconButton("\uE710", "Add uses");
        topUpBtn.Click += async (_, _) => await OpenTopUpDialogAsync(capturedCode);
        actions.Children.Add(topUpBtn);

        // Toggle
        var toggleBtn = MakeIconButton(code.Enabled ? "\uE8FB" : "\uE73E", code.Enabled ? "Disable" : "Enable");
        toggleBtn.Click += async (_, _) =>
        {
            await ViewModel.ToggleInviteCodeCommand.ExecuteAsync(capturedCode);
            ShowStatus(capturedCode.Enabled ? "Code disabled." : "Code enabled.");
        };
        actions.Children.Add(toggleBtn);

        // Delete
        var deleteBtn = MakeIconButton("\uE74D", "Delete", 28);
        deleteBtn.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Invite Code",
                Content = $"Delete invite code \"{capturedCode.Code}\"? This action cannot be undone.",
                PrimaryButtonText = "Delete", CloseButtonText = "Cancel",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                XamlRoot = this.XamlRoot, DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteInviteCodeCommand.ExecuteAsync(capturedCode.Id);
                ShowStatus("Invite code deleted.");
            }
        };
        actions.Children.Add(deleteBtn);
        Grid.SetColumn(actions, 5); row.Children.Add(actions);

        row.PointerEntered += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); };
        row.PointerExited += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); };
        return row;
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var codeBox = new TextBox { PlaceholderText = "Leave blank to auto-generate", CornerRadius = new CornerRadius(6), FontSize = 13, CharacterCasing = CharacterCasing.Upper };
        var labelBox = new TextBox { PlaceholderText = "e.g. Friends & Family", CornerRadius = new CornerRadius(6), FontSize = 13 };
        // Webui enforces min=1 (no unlimited via max_uses=0). Match that.
        var maxUsesBox = new NumberBox { Value = 1, Minimum = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };

        var form = new StackPanel { Width = 512, Spacing = 16 };
        AddField(form, "Code (optional)", codeBox);
        AddField(form, "Label", labelBox);
        AddField(form, "Max Uses", maxUsesBox);

        var dialog = new ContentDialog
        {
            Title = "Create Invite Code",
            PrimaryButtonText = "Create", CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot, Content = form,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await ViewModel.CreateInviteCodeAsync(new CreateInviteCodeRequest
        {
            Code = string.IsNullOrWhiteSpace(codeBox.Text) ? null : codeBox.Text.Trim(),
            Label = labelBox.Text.Trim(),
            MaxUses = double.IsNaN(maxUsesBox.Value) ? 1 : (int)maxUsesBox.Value
        });
        ShowStatus("Invite code created.");
    }

    private async Task OpenTopUpDialogAsync(InviteCode code)
    {
        var additionalUsesBox = new NumberBox
        {
            Value = 1,
            Minimum = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            CornerRadius = new CornerRadius(6),
            FontSize = 13
        };

        var form = new StackPanel { Width = 420, Spacing = 16 };
        AddField(form, "Additional Uses", additionalUsesBox);

        var dialog = new ContentDialog
        {
            Title = "Add Invite Uses",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = form,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var additionalUses = double.IsNaN(additionalUsesBox.Value) ? 0 : (int)additionalUsesBox.Value;
        var updated = await ViewModel.TopUpInviteCodeAsync(code, additionalUses);
        if (updated != null)
        {
            ShowStatus(ViewModel.StatusMessage ?? "Invite code updated.");
        }
    }

    private static void AddField(StackPanel form, string label, FrameworkElement control)
    {
        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(new TextBlock
        {
            Text = label, FontSize = 14, FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        group.Children.Add(control);
        form.Children.Add(group);
    }

    private static Border MakeBadge(string text, Color bg, Color fg)
    {
        return new Border
        {
            Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(fg) }
        };
    }

    private static Button MakeIconButton(string glyph, string tooltip, int size = 28, Color? fgColor = null)
    {
        var fg = fgColor.HasValue ? new SolidColorBrush(fgColor.Value)
            : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        var btn = new Button
        {
            Width = size, Height = size, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon { Glyph = glyph, FontSize = 12, Foreground = fg }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => { StatusBanner.Visibility = Visibility.Collapsed; timer.Stop(); };
        timer.Start();
    }
}
