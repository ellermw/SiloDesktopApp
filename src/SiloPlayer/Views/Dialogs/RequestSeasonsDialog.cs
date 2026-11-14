using SiloPlayer.Controls;
using SiloPlayer.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views.Dialogs;

public sealed record RequestSeasonPickResult(List<int>? Seasons);
public static class RequestSeasonsDialog
{
    public static async Task<RequestSeasonPickResult?> PickAsync(XamlRoot root, RequestMediaDetail item, Func<List<int>?, Task>? submit = null)
    {
        var sending = false;
        List<int>? submittedSeasons = null;
        var (dialog, _) = BuildCore(root, item, () => sending);
        var state = (RequestSeasonPickerState)dialog.Tag;
        var submissionContent = (ContentControl)dialog.Content;
        var body = (StackPanel)submissionContent.Content;
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("ErrorBrush"), Visibility = Visibility.Collapsed };
        body.Children.Add(error);
        var pending = new ProgressRing { IsActive = false, Width = 16, Height = 16, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(pending);
        if (submit != null) dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (sending) return;
            sending = true;
            var deferral = args.GetDeferral(); dialog.IsPrimaryButtonEnabled = false;
            pending.IsActive = true; pending.Visibility = Visibility.Visible;
            var seasons = state.SubmissionSeasons;
            try { await submit(seasons); submittedSeasons = seasons; args.Cancel = false; }
            catch (Exception ex) { SiloPlayer.Controls.MediaItemCardActions.BindText(error, "Text", element => element.Text = SiloPlayer.Controls.MediaItemCardActions.Problem(ex, $"Request failed: {ex.Message}")); error.Visibility = Visibility.Visible; }
            finally { sending = false; pending.IsActive = false; pending.Visibility = Visibility.Collapsed; dialog.IsPrimaryButtonEnabled = state.CanSubmit; deferral.Complete(); }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? new(submit == null ? state.SubmissionSeasons : submittedSeasons) : null;
    }
    // Compatibility for explicit-selection callers. New request entry points use PickAsync.
    public static async Task<List<int>?> ShowAsync(XamlRoot root, RequestMediaDetail item)
        => await PickAsync(root, item) is { } result ? result.Seasons ?? [] : null;

    internal static (ContentDialog Dialog, List<ToggleSwitch> Checks) Build(XamlRoot root, RequestMediaDetail item)
        => BuildCore(root, item, () => false);

    private static (ContentDialog Dialog, List<ToggleSwitch> Checks) BuildCore(XamlRoot root, RequestMediaDetail item, Func<bool> isSubmitting)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var state = new RequestSeasonPickerState(item, today);
        var body = new StackPanel { Spacing = 14, MaxWidth = 400 };
        body.Children.Add(new TextBlock { Text = AppStrings.Get("web.requests.seasons.choose", new Dictionary<string, object?> { ["title"] = item.Title }), TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Brush("SecondaryTextBrush") });
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Brush("SecondaryTextBrush") };
        body.Children.Add(description);
        var dialog = SiloPlayer.Controls.MediaItemCardActions.Localize(new ContentDialog { Title = "Request seasons", XamlRoot = root, Content = new ContentControl { Content = body, HorizontalContentAlignment = HorizontalAlignment.Stretch }, Tag = state, PrimaryButtonText = state.SubmitLabel, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary }, title: "web.requests.seasons.title", closeButton: "common.cancel");
        var checks = new List<ToggleSwitch>(); var rows = new StackPanel();
        var all = new ToggleSwitch { OffContent = "", OnContent = "", IsOn = state.AllSelected };
        SiloPlayer.Services.Localized.SetName(all, "web.requests.seasons.all");
        var suppress = false;
        void Update()
        {
            ((TextBlock)body.Children[0]).Text = AppStrings.Get("web.requests.seasons.choose", new Dictionary<string, object?> { ["title"] = item.Title });
            suppress = true;
            foreach (var check in checks) if (check.IsEnabled) check.IsOn = state.Selected.Contains((int)check.Tag);
            all.IsOn = state.AllSelected; suppress = false;
            dialog.IsPrimaryButtonEnabled = state.CanSubmit && !isSubmitting(); dialog.PrimaryButtonText = MediaItemCardActions.SeasonSubmitLabel(state);
            description.Text = state.Description == null ? "" : AppStrings.Get(state.Seasons.Count == 0 ? "web.requests.seasons.noSeasons" : item.Availability == "available" ? "web.requests.seasons.pickUpcoming" : "web.requests.seasons.noneAired"); description.Visibility = state.Description == null ? Visibility.Collapsed : Visibility.Visible;
        }
        if (state.Choices.Count > 1 && (state.Latest != null || state.Upcoming.Count > 0))
        {
            var quick = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            if (state.Latest is int latest) { var button = SiloPlayer.Controls.MediaItemCardActions.Localize(new Button { Content = "Latest season" }, content: "web.requests.seasons.latest"); button.Click += (_, _) => { state.Select([latest]); Update(); }; quick.Children.Add(button); }
            if (state.Upcoming.Count > 0) { var button = SiloPlayer.Controls.MediaItemCardActions.Localize(new Button { Content = "Upcoming seasons" }, content: "web.requests.seasons.upcoming"); button.Click += (_, _) => { state.Select(state.Upcoming); Update(); }; quick.Children.Add(button); }
            body.Children.Add(quick);
        }
        if (state.Choices.Count > 1)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(all); row.Children.Add(SiloPlayer.Controls.MediaItemCardActions.Localize(new TextBlock { Text = "All seasons", VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.Medium }, text: "web.requests.seasons.all"));
            rows.Children.Add(new Border { Child = row, Padding = new Thickness(12, 10, 12, 10), Background = Brush("SurfaceRaisedBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1) });
            all.Toggled += (_, _) => { if (suppress) return; state.SelectAll(all.IsOn); Update(); };
        }
        foreach (var season in state.Seasons)
        {
            var check = new ToggleSwitch { Tag = season.SeasonNumber, IsEnabled = state.IsChoosable(season), IsOn = state.IsChoosable(season) ? state.Selected.Contains(season.SeasonNumber) : season.Requested, OnContent = "", OffContent = "" };
            var title = $"Season {season.SeasonNumber}";
            if (!string.IsNullOrWhiteSpace(season.Name) && season.Name != title) title += ": " + season.Name;
            var copy = new StackPanel { Spacing = 3 };
            copy.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
            copy.Children.Add(new TextBlock { Text = MediaItemCardActions.SeasonMeta(season), FontSize = 12, Foreground = Brush("SecondaryTextBrush") });
            var status = MediaItemCardActions.SeasonStatus(season, today);
            AutomationProperties.SetName(check, $"Season {season.SeasonNumber}"); AutomationProperties.SetHelpText(check, $"{RequestSeasonPickerState.Meta(season)} {status}");
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(12, 10, 12, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(check); Grid.SetColumn(copy, 1); row.Children.Add(copy);
            if (status != null) { var label = new TextBlock { Text = status, FontSize = 12, Foreground = Brush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(label, 2); row.Children.Add(label); }
            rows.Children.Add(new Border { Child = row, BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1) });
            MediaItemCardActions.BindText(copy, "Season", _ =>
            {
                var heading = MediaItemCardActions.SeasonName(season);
                if (!string.IsNullOrWhiteSpace(season.Name)) heading = season.Name;
                ((TextBlock)copy.Children[0]).Text = heading;
                ((TextBlock)copy.Children[1]).Text = MediaItemCardActions.SeasonMeta(season);
                AutomationProperties.SetName(check, MediaItemCardActions.SeasonName(season));
                AutomationProperties.SetHelpText(check, MediaItemCardActions.SeasonMeta(season) + " " + MediaItemCardActions.SeasonStatus(season, today));
                if (row.Children.OfType<TextBlock>().FirstOrDefault() is { } statusLabel) statusLabel.Text = MediaItemCardActions.SeasonStatus(season, today) ?? "";
            });
            checks.Add(check); check.Toggled += (_, _) => { if (suppress || !check.IsEnabled) return; state.SetSeason((int)check.Tag, check.IsOn); Update(); };
        }
        if (state.Seasons.Count > 0) body.Children.Add(new Border { Child = new ScrollViewer { Content = rows, MaxHeight = Math.Min(root.Size.Height * .6, 448) }, CornerRadius = new CornerRadius(6), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1) });
        SiloPlayer.Services.AppLocalization.Register(dialog, Update);
        Update(); return (dialog, checks);
    }
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
