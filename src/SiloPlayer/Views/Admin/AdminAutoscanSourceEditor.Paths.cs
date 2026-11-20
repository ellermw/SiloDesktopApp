using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using P = SiloPlayer.Core.Services.Admin.AdminAutoscanPresentation;

namespace SiloPlayer.Views.Admin;

internal sealed partial class AdminAutoscanSourceEditor
{
    private StackPanel _mappingRows = new();
    private FrameworkElement PathsSection()
    {
        var draft = _draft!; var section = new StackPanel { Spacing = 12 };
        var prefix = draft.DeliveryMode == "webhook" ? "pathMapping.webhook." : "pathMapping.poll.";
        section.Children.Add(U.Text(T(prefix + "heading"), 14)); section.Children.Add(U.Text(T(prefix + "description"), 12, true));
        _mappingRows = new StackPanel { Spacing = 12 }; AutomationProperties.SetAutomationId(_mappingRows, "AutoscanMappings"); section.Children.Add(_mappingRows); RenderMappings();
        var tools = new StackPanel { Spacing = 8 }; var add = U.Action(T("pathMapping.add"), "AutoscanMappingAdd");
        add.Click += (_, _) => { if (draft.Mappings.Count >= 1000) return; draft.Mappings.Add(new()); draft.MappingsDirty = true; Changed(); RenderMappings(); }; tools.Children.Add(add);
        if (_baseline != null && draft.DeliveryMode != "webhook" && draft.Descriptor.Connection != "none")
        {
            var sync = U.Action(T("rewrites.sync"), "AutoscanMappingsSync"); var preview = new StackPanel { Spacing = 8 };
            var hint = U.Text("", 12, true);
            void UpdateSync()
            { hint.Text = _baseline.ConnectionId == null ? T("sourceDialog.sync.noServer") : draft.ConnectionId != _baseline.ConnectionId ? T("sourceDialog.sync.unsaved") : ""; sync.IsEnabled = hint.Text.Length == 0; }
            UpdateSync(); tools.Children.Add(sync); tools.Children.Add(hint); tools.Children.Add(preview);
            sync.Click += async (_, _) =>
            {
                UpdateSync(); if (!sync.IsEnabled || !Current) return;
                sync.IsEnabled = false; preview.Children.Clear();
                var generation = _generation;
                var result = await _controller.SuggestionsAsync(_baseline.Id, _owner, _reads.Token);
                if (!Current || !ReferenceEquals(draft, _draft)) return;
                UpdateSync();
                if (generation != _generation) return;
                if (result == null) { preview.Children.Add(U.Text("Could not read rewrite suggestions. Try Sync from server again.", 13)); return; }
                preview.Children.Add(U.Text(T("rewrites.proposed"), 14));
                var selected = new List<(CheckBox Check, AdminAutoscanProposedRewrite Row)>();
                foreach (var proposed in result.Proposed.Take(1000))
                {
                    var check = new CheckBox { IsChecked = true, Content = U.Text(proposed.From + " → " + proposed.To + " · " + (proposed.MatchDepth >= 2 ? T("rewrites.segments", ("count", proposed.MatchDepth)) : T("rewrites.weak")), 12) };
                    preview.Children.Add(check); selected.Add((check, proposed));
                }
                if (selected.Count == 0) preview.Children.Add(U.Text(T("rewrites.noneProposed"), 12, true));
                void Details(string key, IEnumerable<string> values)
                { var rows = values.Take(1000).ToArray(); if (rows.Length > 0) preview.Children.Add(new Expander { Header = T(key, ("count", rows.Length)), Content = U.Text(string.Join("\n", rows), 12, true), HorizontalAlignment = HorizontalAlignment.Stretch }); }
                Details("rewrites.unmatched", result.Unmatched); Details("rewrites.covered", result.Covered); Details("rewrites.ambiguous", result.Ambiguous.Select(a => a.Root + " → " + string.Join(", ", a.Candidates)));
                var apply = U.Action(T("rewrites.addSelected"), "AutoscanMappingsApply"); apply.IsEnabled = selected.Count > 0;
                apply.Click += (_, _) =>
                {
                    if (!Current || !ReferenceEquals(draft, _draft) || generation != _generation || draft.ConnectionId != _baseline.ConnectionId) return;
                    var froms = draft.Mappings.Select(row => row.From.Trim()).ToHashSet();
                    foreach (var (check, row) in selected.Where(s => s.Check.IsChecked == true))
                        if (draft.Mappings.Count < 1000 && froms.Add(row.From)) draft.Mappings.Add(new() { From = row.From, To = row.To });
                    draft.MappingsDirty = true; Changed(); RenderMappings(); preview.Children.Clear();
                };
                var dismiss = U.Action(T("rewrites.dismiss"), "AutoscanMappingsDismiss"); dismiss.Click += (_, _) => preview.Children.Clear();
                preview.Children.Add(apply); preview.Children.Add(dismiss);
            };
        }
        else if (_baseline == null && draft.DeliveryMode != "webhook" && draft.Descriptor.Connection != "none") tools.Children.Add(U.Text(T("sourceDialog.syncHint"), 12, true));
        section.Children.Add(tools); return section;
    }
    private void RenderMappings()
    {
        if (_draft is not { } draft) return; _mappingRows.Children.Clear();
        if (draft.Mappings.Count == 0) _mappingRows.Children.Add(U.Text(T(draft.DeliveryMode == "webhook" ? _controller.State.Libraries.Count > 0 ? "pathMapping.webhook.empty" : "pathMapping.webhook.emptyNoLibraries" : "pathMapping.poll.empty"), 12, true));
        foreach (var row in draft.Mappings.ToArray())
        {
            var index = draft.Mappings.IndexOf(row); var panel = new StackPanel { Spacing = 6 };
            var fields = new Grid { ColumnSpacing = 8, RowSpacing = 6 }; fields.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); fields.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); fields.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            fields.RowDefinitions.Add(new() { Height = GridLength.Auto }); fields.RowDefinitions.Add(new() { Height = GridLength.Auto }); fields.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var fromLabel = T(draft.DeliveryMode == "webhook" ? "pathMapping.webhook.fromLabel" : "pathMapping.poll.fromLabel");
            var from = Input(fromLabel, "AutoscanMappingFrom_" + index, row.From); from.TextChanged += (_, _) => { if (from.Text == row.From) return; row.From = from.Text; draft.MappingsDirty = true; Changed(); };
            var to = Input(T("pathMapping.toLabel"), "AutoscanMappingTo_" + index, row.To); to.TextChanged += (_, _) => { if (to.Text == row.To) return; row.To = to.Text; draft.MappingsDirty = true; Changed(); };
            var fromField = Field(fromLabel, from); var toField = Field(T("pathMapping.toLabel"), to); fields.Children.Add(fromField); fields.Children.Add(toField); Grid.SetColumn(toField, 1);
            var remove = U.Action("×", "AutoscanMappingRemove_" + index); AutomationProperties.SetName(remove, T("pathMapping.removeAria", ("index", index + 1))); remove.Click += (_, _) => { draft.Mappings.Remove(row); draft.MappingsDirty = true; Changed(); RenderMappings(); }; fields.Children.Add(remove); Grid.SetColumn(remove, 2);
            fields.SizeChanged += (_, args) => { var narrow = args.NewSize.Width < 480; Grid.SetRow(toField, narrow ? 1 : 0); Grid.SetColumn(toField, narrow ? 0 : 1); Grid.SetColumnSpan(fromField, narrow ? 2 : 1); Grid.SetColumnSpan(toField, narrow ? 2 : 1); Grid.SetRow(remove, narrow ? 1 : 0); };
            panel.Children.Add(fields);
            var children = draft.DeliveryMode == "webhook" ? P.ExpandedRoots(row.To, _controller.State.Libraries.SelectMany(l => l.Paths)) : [];
            if (children.Count > 0)
            { var split = U.Action(T("pathMapping.split", ("count", children.Count)), "AutoscanMappingSplit_" + index); split.Click += (_, _) => { var at = draft.Mappings.IndexOf(row); if (at < 0 || draft.Mappings.Count - 1 + children.Count > 1000) return; draft.Mappings.RemoveAt(at); draft.Mappings.InsertRange(at, children.Select(path => new AdminAutoscanPathRewrite { To = path })); draft.MappingsDirty = true; Changed(); RenderMappings(); }; panel.Children.Add(split); }
            _mappingRows.Children.Add(panel);
        }
    }
}
