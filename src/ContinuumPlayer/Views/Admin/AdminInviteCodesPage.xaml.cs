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

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.InviteCodes.CollectionChanged += (_, _) => ScheduleRebuild();
        try { await ViewModel.LoadCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

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
        Grid.SetColumn(codeBorder, 0); row.Children.Add(codeBorder);

        var label = new TextBlock
        {
            Text = code.Label, FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(label, 1); row.Children.Add(label);

        var maxUses = new TextBlock
        {
            Text = code.MaxUses > 0 ? code.MaxUses.ToString() : "\u221E",
            FontSize = 13, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(maxUses, 2); row.Children.Add(maxUses);

        var useCount = new TextBlock
        {
            Text = code.UseCount.ToString(), FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(useCount, 3); row.Children.Add(useCount);

        var statusBadge = code.Enabled
            ? MakeBadge("Active", Color.FromArgb(40, 34, 197, 94), Color.FromArgb(255, 34, 197, 94))
            : MakeBadge("Disabled", Color.FromArgb(40, 120, 120, 120), Color.FromArgb(255, 160, 160, 160));
        Grid.SetColumn(statusBadge, 4); row.Children.Add(statusBadge);

        string createdText = "\u2014";
        if (!string.IsNullOrEmpty(code.CreatedAt) && DateTime.TryParse(code.CreatedAt, out var dt))
            createdText = dt.ToLocalTime().ToString("d");
        var created = new TextBlock
        {
            Text = createdText, FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(created, 5); row.Children.Add(created);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var capturedCode = code;

        // Toggle
        var toggleBtn = MakeIconButton(code.Enabled ? "\uE8FB" : "\uE73E", code.Enabled ? "Disable" : "Enable");
        toggleBtn.Click += async (_, _) =>
        {
            await ViewModel.ToggleInviteCodeCommand.ExecuteAsync(capturedCode);
            ShowStatus(capturedCode.Enabled ? "Code disabled." : "Code enabled.");
        };
        actions.Children.Add(toggleBtn);

        // Delete
        var deleteBtn = MakeIconButton("\uE74D", "Delete", 28, Color.FromArgb(255, 220, 90, 90));
        deleteBtn.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Invite Code",
                Content = $"Delete invite code \"{capturedCode.Code}\"?",
                PrimaryButtonText = "Delete", CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot, DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteInviteCodeCommand.ExecuteAsync(capturedCode.Id);
                ShowStatus("Invite code deleted.");
            }
        };
        actions.Children.Add(deleteBtn);
        Grid.SetColumn(actions, 6); row.Children.Add(actions);

        return row;
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var codeBox = new TextBox { PlaceholderText = "Leave blank to auto-generate", CornerRadius = new CornerRadius(8), FontSize = 13 };
        var labelBox = new TextBox { PlaceholderText = "e.g. Friends & Family", CornerRadius = new CornerRadius(8), FontSize = 13 };
        var maxUsesBox = new NumberBox { Value = 1, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };

        var form = new StackPanel { Width = 380, Spacing = 16 };
        AddField(form, "Code (optional)", codeBox);
        AddField(form, "Label", labelBox);
        AddField(form, "Max Uses (0 = unlimited)", maxUsesBox);

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
            MaxUses = (int)maxUsesBox.Value
        });
        ShowStatus("Invite code created.");
    }

    private static void AddField(StackPanel form, string label, FrameworkElement control)
    {
        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(new TextBlock
        {
            Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
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
