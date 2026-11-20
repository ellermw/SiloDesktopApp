using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.UI.Text;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private UIElement BuildFiles(AdminPreparedFilesSnapshot state)
    {
        var root = Section("Prepared files", "Completed server and node copies that supply device downloads.");
        var query = state.FileQuery;
        var locationChoices = new List<(string, string)> { ("", "All locations") };
        if (state.Storage.Value != null)
            locationChoices.AddRange(state.Storage.Value.Locations.Select(x => (x.Key, x.Name)));
        var filters = Toolbar(
            SearchBox("PreparedFilesSearch", "Search titles", query.Search, value => _ = Controller.SetFileQueryAsync(query with { Search = value })),
            Select("PreparedFilesLocationFilter", locationChoices, query.Location ?? "", value =>
            {
                var next = value.Length == 0 ? null : value;
                var request = new AdminRouteRequest(AdminRoute.PreparedFiles, new Dictionary<string, string> { ["tab"] = "files" });
                if (next != null) request = request.WithQuery("location", next);
                if (AdminShellPage.TryReplace(this, request)) _request = request;
                _ = Controller.SetFileQueryAsync(Controller.State.FileQuery with { Location = next });
            }),
            Select("PreparedFilesStateFilter", new[] { ("", "All states"), ("in_use", "In use"), ("cached", "Cached"), ("expired", "Expired") }, query.State ?? "",
                value => _ = Controller.SetFileQueryAsync(Controller.State.FileQuery with { State = value.Length == 0 ? null : value })),
            Select("PreparedFilesFormatFilter", new[] { ("", "All formats"), ("transcode", "Transcode"), ("remux", "Remux") }, query.Format ?? "",
                value => _ = Controller.SetFileQueryAsync(Controller.State.FileQuery with { Format = value.Length == 0 ? null : value })),
            Select("PreparedFilesSort", new[] { ("size", "Largest first"), ("last_used", "Last used"), ("created", "Newest") }, query.Sort,
                value => _ = Controller.SetFileQueryAsync(Controller.State.FileQuery with { Sort = value })));
        root.Children.Add(new ScrollViewer { Content = filters, HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollMode = ScrollMode.Disabled });

        var visibleIds = state.Files.Items.Where(x => x.State != "expired").Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        _selectedFiles.RemoveWhere(id => !visibleIds.Contains(id));
        if (_selectedFiles.Count > 0)
        {
            var chosen = state.Files.Items.Where(x => _selectedFiles.Contains(x.Id)).ToArray();
            var selection = Toolbar(Text($"{chosen.Length} selected · {chosen.Count(x => x.State == "in_use")} in use", 13),
                Button("Clear", "PreparedFilesClearSelection", () => { _selectedFiles.Clear(); Render(Controller.State); }),
                Button("Delete", "PreparedFilesDeleteSelected", () => ConfirmDeleteFiles(chosen)));
            root.Children.Add(Card(selection, new Thickness(10)));
        }

        if (state.Files.Loading && !state.Files.Loaded) root.Children.Add(Message("Reading prepared files..."));
        else if (state.Files.Error != null) root.Children.Add(Message(state.Files.Error, "Try again", () => _ = Controller.ReloadFilesAsync()));
        else if (state.Files.Items.Count == 0) root.Children.Add(Message("No prepared files match these filters."));
        else
        {
            foreach (var file in state.Files.Items) root.Children.Add(BuildFileRow(file));
            if (state.Files.NextCursor != null)
            {
                var more = Button(state.Files.Loading ? "Loading..." : "Load more", "PreparedFilesLoadMore", () => _ = Controller.LoadMoreFilesAsync());
                more.IsEnabled = !state.Files.Loading; root.Children.Add(more);
            }
            if (state.Files.Truncated) root.Children.Add(Message("Only the first 500 prepared files are retained here. Narrow the filters to continue."));
        }
        return root;
    }

    private UIElement BuildFileRow(AdminPreparedFile file)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var selected = _selectedFiles.Contains(file.Id);
        var choose = Button(selected ? "Selected" : "Select", "PreparedFilesSelect_" + file.Id, () =>
        {
            if (!_selectedFiles.Add(file.Id)) _selectedFiles.Remove(file.Id);
            Render(Controller.State);
        });
        choose.IsEnabled = file.State != "expired";
        AutomationProperties.SetName(choose, (selected ? "Deselect " : "Select ") + (string.IsNullOrWhiteSpace(file.Title) ? "prepared file" : file.Title));
        row.Children.Add(choose);

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(Text(string.IsNullOrWhiteSpace(file.Title) ? "Unknown title" : file.Title, 15, FontWeights.SemiBold));
        var subtitle = file.SeasonNumber is { } season && file.EpisodeNumber is { } episode
            ? $"S{season:00}E{episode:00}" + (string.IsNullOrWhiteSpace(file.EpisodeTitle) ? "" : " · " + file.EpisodeTitle)
            : file.Year?.ToString() ?? file.MediaType;
        if (!string.IsNullOrWhiteSpace(subtitle)) content.Children.Add(Text(subtitle, 12));
        var recipe = AdminPreparedFilesPresentation.FileRecipe(file);
        content.Children.Add(Text(recipe.Main + (recipe.Detail.Length > 0 ? " · " + recipe.Detail : "") +
            $" · {file.Container.ToUpperInvariant()} · {AdminPreparedFilesPresentation.FormatBytes(file.Bytes)}", 12));
        content.Children.Add(Text($"{LocationName(Controller.State.Storage.Value, file.Location)} · Last used {AdminPreparedFilesPresentation.Relative(file.LastUsedAt)} · {AdminPreparedFilesPresentation.FileUsage(file)}", 12));
        if (file.StaleWaiting > 0 && file.OldestWaiting != null)
            content.Children.Add(Text($"{file.StaleWaiting} waiting on {file.OldestWaiting.DeviceName}; last seen {AdminPreparedFilesPresentation.Relative(file.OldestWaiting.LastSeenAt)}", 12));
        Grid.SetColumn(content, 1); row.Children.Add(content);
        var state = Text(file.State switch { "in_use" => "In use", "cached" => "Cached", "expired" => "Expired", _ => file.State }, 12, FontWeights.SemiBold);
        Grid.SetColumn(state, 2); row.Children.Add(state);
        var card = Card(row, new Thickness(12));
        AutomationProperties.SetAutomationId(card, "PreparedFilesRow_" + file.Id);
        return card;
    }

    private async void ConfirmDeleteFiles(IReadOnlyList<AdminPreparedFile> files)
    {
        if (files.Count == 0 || _dialog != null || XamlRoot == null) return;
        var include = new CheckBox { Content = $"Also delete {files.Count(x => x.State == "in_use")} in-use files" };
        AutomationProperties.SetAutomationId(include, "PreparedFilesIncludeInUse");
        var initial = AdminPreparedFilesPresentation.DeletePrompt(files, false);
        var details = new StackPanel { Spacing = 8 };
        details.Children.Add(Text("Choose which selected server-side prepared files to remove.", 13));
        details.Children.Add(Text(initial.Description, 13));
        if (initial.InUseWarning.Length > 0) details.Children.Add(Text(initial.InUseWarning, 13));
        if (files.Any(x => x.State == "in_use")) details.Children.Add(include);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = initial.Title, Content = details, CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        void Update()
        {
            var prompt = AdminPreparedFilesPresentation.DeletePrompt(files, include.IsChecked == true);
            dialog.PrimaryButtonText = prompt.ConfirmLabel;
            dialog.IsPrimaryButtonEnabled = files.Any(x => include.IsChecked == true || x.State != "in_use");
        }
        include.Checked += (_, _) => Update(); include.Unchecked += (_, _) => Update(); Update();
        _dialog = dialog;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        catch (OperationCanceledException) { return; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
        if (result != ContentDialogResult.Primary) return;
        var includeInUse = include.IsChecked == true;
        var targets = files.Where(x => includeInUse || x.State != "in_use").ToArray();
        if (targets.Length == 0) return;
        _selectedFiles.Clear();
        await Controller.DeleteFilesAsync(targets, includeInUse);
    }
}
