using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.UI.Text;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private string? _preparationStateFilter;
    private string? _preparationWorkerFilter;
    private string _preparationSearch = "";

    private UIElement BuildPreparation(AdminPreparedFilesSnapshot state)
    {
        var root = Section("Preparation", "Jobs creating a prepared file for one or more requested downloads.");
        var counts = state.PreparationCounts;
        var summaries = Toolbar(
            StateFilter("Running", "running", counts.Running), StateFilter("Queued", "queued", counts.Queued),
            StateFilter("Retrying", "retrying", counts.Retrying), StateFilter("Paused", "paused", counts.Paused),
            StateFilter("Failed recently", "failed", counts.FailedRecent));
        root.Children.Add(new ScrollViewer { Content = summaries, HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollMode = ScrollMode.Disabled });

        var workers = state.Preparations.Select(x => x.Worker).Where(x => x != null)
            .Select(x => (Key: x!.Kind == "node" ? "node:" + (x.NodeId ?? x.Name) : x.Kind, Label: string.IsNullOrWhiteSpace(x.Name) ? x.Kind : x.Name))
            .Distinct().ToArray();
        var workerChoices = new List<(string, string)> { ("", "All workers") };
        workerChoices.AddRange(workers.Select(x => (x.Key, x.Label)));
        root.Children.Add(Horizontal(Toolbar(
            SearchBox("PreparedFilesPreparationSearch", "Filter jobs", _preparationSearch, value => { _preparationSearch = value; Render(Controller.State); }),
            Select("PreparedFilesPreparationWorker", workerChoices, _preparationWorkerFilter ?? "", value => { _preparationWorkerFilter = value.Length == 0 ? null : value; Render(Controller.State); }),
            Button("Refresh", "PreparedFilesPreparationRefresh", () => _ = Controller.ReloadPreparationsAsync()))));

        var filtered = state.Preparations.Where(PreparationMatches).ToArray();
        var visible = filtered.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        _selectedPreparations.RemoveWhere(x => !visible.Contains(x));
        if (_selectedPreparations.Count > 0)
        {
            var chosen = filtered.Where(x => _selectedPreparations.Contains(x.Id)).ToArray();
            var bulk = Toolbar(Text($"{chosen.Length} selected", 13));
            var pausable = chosen.Where(CanPause).ToArray();
            var resumable = chosen.Where(x => x.State == "paused").ToArray();
            if (pausable.Length > 0) bulk.Children.Add(Button($"Pause {pausable.Length}", "PreparedFilesPauseSelected", () => _ = Controller.RunPreparationActionAsync("pause", pausable)));
            if (resumable.Length > 0) bulk.Children.Add(Button($"Resume {resumable.Length}", "PreparedFilesResumeSelected", () => _ = Controller.RunPreparationActionAsync("resume", resumable)));
            bulk.Children.Add(Button("Cancel", "PreparedFilesCancelSelected", () => ConfirmCancelPreparations(chosen)));
            bulk.Children.Add(Button("Clear", "PreparedFilesPreparationClear", () => { _selectedPreparations.Clear(); Render(Controller.State); }));
            root.Children.Add(Card(bulk, new Thickness(10)));
        }

        if (state.PreparationsLoading && !state.PreparationsLoaded) root.Children.Add(Message("Reading preparation jobs..."));
        else if (state.PreparationsError != null) root.Children.Add(Message(state.PreparationsError, "Try again", () => _ = Controller.ReloadPreparationsAsync()));
        else if (filtered.Length == 0) root.Children.Add(Message(state.Preparations.Count == 0 ? "Nothing is preparing." : "No preparation jobs match these filters."));
        else foreach (var item in filtered) root.Children.Add(BuildPreparationRow(item, state.ActionPending));
        return root;
    }

    private Button StateFilter(string label, string state, int count)
    {
        var button = Button($"{label}  {count}", "PreparedFilesPreparationFilter_" + state, () =>
        {
            _preparationStateFilter = _preparationStateFilter == state ? null : state;
            Render(Controller.State);
        });
        button.FontWeight = _preparationStateFilter == state ? FontWeights.SemiBold : FontWeights.Normal;
        return button;
    }

    private bool PreparationMatches(AdminDownloadPreparation item)
    {
        if (_preparationStateFilter != null && item.State != _preparationStateFilter) return false;
        var worker = item.Worker == null ? "" : item.Worker.Kind == "node" ? "node:" + (item.Worker.NodeId ?? item.Worker.Name) : item.Worker.Kind;
        if (_preparationWorkerFilter != null && worker != _preparationWorkerFilter) return false;
        if (_preparationSearch.Length == 0) return true;
        var search = string.Join(' ', new[] { item.MediaTitle, item.SeriesName, item.EpisodeName, item.Worker?.Name }
            .Concat(item.Requesters.SelectMany(x => new[] { x.Username, x.ProfileName, x.DeviceName, x.DevicePlatform })));
        return search.Contains(_preparationSearch, StringComparison.CurrentCultureIgnoreCase);
    }

    private UIElement BuildPreparationRow(AdminDownloadPreparation item, bool busy)
    {
        var root = new StackPanel { Spacing = 8 };
        var title = new Grid { ColumnSpacing = 8 };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var selected = _selectedPreparations.Contains(item.Id);
        var select = Button(selected ? "Selected" : "Select", "PreparedFilesPreparationSelect_" + item.Id, () =>
        {
            if (!_selectedPreparations.Add(item.Id)) _selectedPreparations.Remove(item.Id);
            Render(Controller.State);
        });
        title.Children.Add(select);
        var identity = new StackPanel { Spacing = 2 };
        identity.Children.Add(Text(AdminPreparedFilesPresentation.PreparationTitle(item), 15, FontWeights.SemiBold));
        identity.Children.Add(Text(AdminPreparedFilesPresentation.PreparationSubtitle(item), 12));
        Grid.SetColumn(identity, 1); title.Children.Add(identity);
        var state = Text(item.State switch { "running" => "Running", "queued" => "Queued", "retrying" => "Retrying", "paused" => "Paused", "failed" => "Failed", _ => item.State }, 12, FontWeights.SemiBold);
        Grid.SetColumn(state, 2); title.Children.Add(state);
        root.Children.Add(title);

        var source = string.Join(' ', new[] { item.Source.Resolution, item.Source.VideoCodec.ToUpperInvariant() }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var output = item.Format == "remux" ? "Remux to " + item.Output.Container.ToUpperInvariant()
            : string.Join(' ', new[] { item.Output.Resolution, item.Output.VideoCodec.ToUpperInvariant() }.Where(x => !string.IsNullOrWhiteSpace(x)));
        root.Children.Add(Text($"{source} → {output} · Attempt {item.Attempts} of {item.MaxAttempts}", 12));
        if (item.Worker != null) root.Children.Add(Text($"Worker: {(string.IsNullOrWhiteSpace(item.Worker.Name) ? item.Worker.Kind : item.Worker.Name)}", 12));
        if (item.Progress is { DurationSeconds: > 0 } progress)
        {
            var percent = Math.Clamp(progress.EncodedSeconds / progress.DurationSeconds * 100, 0, 100);
            root.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = percent, Height = 7 });
            root.Children.Add(Text($"{Math.Round(percent)}% · {progress.Speed:0.0}× · updated {AdminPreparedFilesPresentation.Relative(progress.UpdatedAt)}", 12));
        }
        else if (item.ProgressUnavailable) root.Children.Add(Text("Live progress is unavailable for this job.", 12));
        if (!string.IsNullOrWhiteSpace(item.Error)) root.Children.Add(Text(item.Error, 12));
        if (item.Requesters.Count > 0)
            root.Children.Add(Text("Requested by " + string.Join(", ", item.Requesters.Select(x =>
                (string.IsNullOrWhiteSpace(x.ProfileName) ? x.Username : x.ProfileName) + (string.IsNullOrWhiteSpace(x.DeviceName) ? "" : " on " + x.DeviceName))), 12));

        var actions = Toolbar();
        if (CanPause(item)) actions.Children.Add(ActionButton("Pause", "PreparedFilesPause_" + item.Id, () => _ = Controller.RunPreparationActionAsync("pause", [item]), busy));
        if (item.State == "paused") actions.Children.Add(ActionButton("Resume", "PreparedFilesResume_" + item.Id, () => _ = Controller.RunPreparationActionAsync("resume", [item]), busy));
        actions.Children.Add(ActionButton(item.State == "failed" ? "Remove" : "Cancel", "PreparedFilesCancel_" + item.Id, () => ConfirmCancelPreparations([item]), busy));
        root.Children.Add(actions);
        var card = Card(root);
        AutomationProperties.SetAutomationId(card, "PreparedFilesPreparation_" + item.Id);
        return card;
    }

    private static bool CanPause(AdminDownloadPreparation item) => item.State is "running" or "queued" or "retrying";

    private static Button ActionButton(string label, string id, Action action, bool busy)
    {
        var button = Button(label, id, action); button.IsEnabled = !busy; return button;
    }

    private async void ConfirmCancelPreparations(IReadOnlyList<AdminDownloadPreparation> items)
    {
        if (items.Count == 0) return;
        var waiting = items.Sum(x => x.Requesters.Count(r => r.Status == "preparing"));
        var description = items.Any(x => x.State == "running") ? "Current encoding progress will be lost. " : "";
        description += waiting > 0 ? $"{waiting} waiting {(waiting == 1 ? "download" : "downloads")} will fail." : "No downloads are currently waiting on these jobs.";
        var title = items.Count == 1
            ? (items[0].State == "failed" ? $"Remove failed job for {AdminPreparedFilesPresentation.PreparationTitle(items[0])}?" : $"Cancel {AdminPreparedFilesPresentation.PreparationTitle(items[0])}?")
            : $"Cancel {items.Count} preparation jobs?";
        if (!await ConfirmAsync(title, description, items.Count == 1 ? (items[0].State == "failed" ? "Remove job" : "Cancel job") : $"Cancel {items.Count} jobs")) return;
        _selectedPreparations.Clear();
        await Controller.RunPreparationActionAsync("cancel", items);
    }
}
