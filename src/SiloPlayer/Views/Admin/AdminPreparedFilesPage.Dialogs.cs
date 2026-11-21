using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using Presentation = SiloPlayer.Core.Services.Admin.AdminPreparedFilesPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private const string LocKey = "web.admin.downloads.location";
    private const double BytesPerGb = 1e9;

    /// <summary>Opens the official location editor: read-only server information, or a node's directory and budget overrides.</summary>
    private async void EditLocation(AdminDownloadStorageLocation location)
    {
        if (_disposed) return;
        var storage = Controller.State.Storage.Value;
        if (storage == null) return;
        if (location.Kind == "node") { await EditNodeLocationAsync(location, storage); return; }

        var info = Stack(12);
        info.Children.Add(Text(T(LocKey + ".serverDescription"), 14, true));
        info.Children.Add(Metric(T(LocKey + ".directory"), location.Dir, mono: true));
        info.Children.Add(Metric(T(LocKey + ".defaultBudget"), storage.DefaultBudgetBytes > 0 ? Presentation.FormatBytes(storage.DefaultBudgetBytes) : T(LocKey + ".noBudget")));
        info.Children.Add(Metric(T(LocKey + ".keepCached"), T(LocKey + ".keepCachedValue", Presentation.Count(storage.CacheHours))));
        info.Children.Add(Metric(T(LocKey + ".diskCeiling"), T(LocKey + ".percent", new { percent = storage.DiskCeilingPercent })));
        var dialog = new ContentDialog { Title = T(LocKey + ".serverTitle"), Content = info, PrimaryButtonText = T(LocKey + ".openSettings"), CloseButtonText = T(LocKey + ".close") };
        var settingsAvailable = AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.DownloadSettings);
        dialog.IsPrimaryButtonEnabled = settingsAvailable;
        AutomationProperties.SetAutomationId(dialog, "PreparedFilesServerLocationDialog");
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary) AdminShellPage.Open(new AdminRouteRequest(AdminRoute.DownloadSettings));
    }

    private async Task EditNodeLocationAsync(AdminDownloadStorageLocation location, AdminDownloadStorage storage)
    {
        MonitorAuthorityLease lease;
        try { lease = Controller.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        using (lease)
        {
            var form = new NodeLocationForm(this, location, storage, lease);
            var dialog = new ContentDialog { Title = T(LocKey + ".nodeTitle", new { name = location.Name }), Content = new ScrollViewer { Content = form.Root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 }, PrimaryButtonText = T("common.save"), CloseButtonText = T("common.cancel") };
            form.Bind(dialog);
            AutomationProperties.SetAutomationId(dialog, "PreparedFilesNodeLocationDialog");
            var shown = ShowDialogAsync(dialog);
            _ = form.LoadAsync();
            await shown;
            form.Close();
        }
    }

    /// <summary>One node's directory and budget editor. Only changed fields are sent, under the strong validator the node was read with.</summary>
    private sealed class NodeLocationForm
    {
        private readonly AdminPreparedFilesPage _page;
        private readonly AdminDownloadStorageLocation _location;
        private readonly AdminDownloadStorage _storage;
        private readonly MonitorAuthorityLease _lease;
        private readonly ContentControl _host = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 420 };
        private ContentDialog? _dialog;
        private AdminPreparedFilesNode? _node;
        private TextBox? _dir, _gb;
        private RadioButtons? _mode;
        private TextBlock? _dirError, _gbError, _saveError;
        private Button? _reload;
        private Border? _dirChange;
        private TextBlock? _scratch;
        private string _currentDir = "";
        private long? _currentBudget;
        private bool _closed, _saving, _syncing;

        public NodeLocationForm(AdminPreparedFilesPage page, AdminDownloadStorageLocation location, AdminDownloadStorage storage, MonitorAuthorityLease lease)
        {
            _page = page; _location = location; _storage = storage; _lease = lease;
            _host.Content = Text(T(LocKey + ".loadingNode"), 14, true);
        }

        public UIElement Root => _host;
        public void Close() { _closed = true; }

        public void Bind(ContentDialog dialog)
        {
            _dialog = dialog;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true;
                var deferral = args.GetDeferral();
                try { if (await SaveAsync()) args.Cancel = false; }
                finally { deferral.Complete(); }
            };
        }

        public async Task LoadAsync()
        {
            try
            {
                var nodes = await _page.Controller.ListNodesAsync(_lease);
                if (_closed || !_page.Controller.IsActionCurrent(_lease)) return;
                var node = nodes.FirstOrDefault(n => n.Id == _location.NodeId);
                if (node == null) { ShowError(); return; }
                Build(node);
            }
            catch (Exception) { if (!_closed) ShowError(); }
        }

        private void ShowError()
        {
            var stack = Stack(12);
            stack.Children.Add(Text(T(LocKey + ".nodeError"), 14, true));
            var retry = Button(T("configure.native.preparedFiles.reload"), "PreparedFilesNodeReload");
            retry.Click += async (_, _) => { _host.Content = Text(T(LocKey + ".loadingNode"), 14, true); await LoadAsync(); };
            stack.Children.Add(retry);
            _host.Content = stack;
            if (_dialog != null) _dialog.IsPrimaryButtonEnabled = false;
        }

        private void Build(AdminPreparedFilesNode node)
        {
            _node = node;
            _currentDir = node.DownloadArtifactDirOverride ?? "";
            _currentBudget = node.DownloadArtifactMaxBytesOverride;
            var server = _storage.Locations.FirstOrDefault(l => l.Kind == "server");
            var clusterDir = server?.DirSource == "setting" ? server.Dir : null;
            var indexed = Presentation.PreparedBytes(_location);
            var stored = _location.Usage?.Bytes ?? indexed;
            var root = Stack(18);
            root.Children.Add(Text(T(LocKey + ".nodeDescription"), 14, true));

            var dirBox = Stack(6);
            dirBox.Children.Add(Text(T(LocKey + ".directory"), 14));
            _dir = new TextBox { Text = _currentDir, PlaceholderText = clusterDir ?? T(LocKey + ".dirPlaceholder"), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12 };
            AutomationProperties.SetAutomationId(_dir, "PreparedFilesNodeDirectory");
            AutomationProperties.SetName(_dir, T(LocKey + ".directory"));
            dirBox.Children.Add(_dir);
            _dirError = Text("", 12); _dirError.Foreground = Brush("ErrorBrush"); _dirError.Visibility = Visibility.Collapsed;
            dirBox.Children.Add(_dirError);
            dirBox.Children.Add(RichText(AdminPreparedFilesText.Rich(clusterDir != null ? LocKey + ".dirHelpCluster" : LocKey + ".dirHelpDefault",
                clusterDir != null ? new { dir = clusterDir } : (object)new { }), 12, true));
            if (_location.Usage?.SharesScratch == true)
            {
                _scratch = Text(T(LocKey + ".sharesScratch"), 12); _scratch.Foreground = Brush("WarningBrush");
                dirBox.Children.Add(_scratch);
            }
            root.Children.Add(dirBox);
            _dirChange = new Border { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(12), Padding = new Thickness(12), BorderThickness = new Thickness(1),
                BorderBrush = Tint("WarningBrush", 0.3), Background = Tint("WarningBrush", 0.08) };
            AutomationProperties.SetAutomationId(_dirChange, "PreparedFilesNodeDirChange");
            root.Children.Add(_dirChange);

            var budget = Stack(8);
            budget.Children.Add(Text(T(LocKey + ".budget"), 14));
            _mode = new RadioButtons { MaxColumns = 1 };
            AutomationProperties.SetAutomationId(_mode, "PreparedFilesNodeBudgetMode");
            AutomationProperties.SetName(_mode, T(LocKey + ".budget"));
            _mode.Items.Add(new RadioButton { Content = T(LocKey + ".budgetDefault") + " — " + (_storage.DefaultBudgetBytes > 0 ? Presentation.FormatBytes(_storage.DefaultBudgetBytes) : T(LocKey + ".noBudget")), Tag = "default" });
            _mode.Items.Add(new RadioButton { Content = T(LocKey + ".budgetCustom"), Tag = "custom" });
            _mode.Items.Add(new RadioButton { Content = T(LocKey + ".budgetNone") + " — " + T(LocKey + ".budgetNoneHint"), Tag = "none" });
            var initial = _currentBudget == null ? 0 : _currentBudget == 0 ? 2 : 1;
            _mode.SelectedIndex = initial;
            budget.Children.Add(_mode);
            var gbRow = Inline(8);
            _gb = new TextBox { Width = 128, Text = _currentBudget is > 0 ? (_currentBudget.Value / BytesPerGb).ToString("0.###############", CultureInfo.InvariantCulture) : "" };
            AutomationProperties.SetAutomationId(_gb, "PreparedFilesNodeBudgetGb");
            AutomationProperties.SetName(_gb, T(LocKey + ".budgetInput"));
            gbRow.Children.Add(_gb);
            gbRow.Children.Add(Text(T(LocKey + ".gb"), 14, true));
            budget.Children.Add(gbRow);
            _gbError = Text("", 12); _gbError.Foreground = Brush("ErrorBrush"); _gbError.Visibility = Visibility.Collapsed;
            budget.Children.Add(_gbError);
            budget.Children.Add(Text(T(LocKey + ".budgetHelp", new { size = Presentation.FormatBytes(indexed) }), 12, true));
            root.Children.Add(budget);

            var ceiling = new LogsWrapPanel { Spacing = 0 };
            foreach (var run in AdminPreparedFilesText.Rich(LocKey + ".ceilingNote", new { percent = _storage.DiskCeilingPercent }))
            {
                if (run.Has("link"))
                {
                    var link = new HyperlinkButton { Content = run.Text, Padding = new Thickness(0), FontSize = 12 };
                    link.IsEnabled = AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.DownloadSettings);
                    link.Click += (_, _) => { _dialog?.Hide(); AdminShellPage.Open(new AdminRouteRequest(AdminRoute.DownloadSettings)); };
                    ceiling.Children.Add(link);
                }
                else { var piece = Text(run.Text, 12, true); piece.TextWrapping = TextWrapping.NoWrap; ceiling.Children.Add(piece); }
            }
            root.Children.Add(ceiling);

            _saveError = Text("", 12); _saveError.Foreground = Brush("ErrorBrush"); _saveError.Visibility = Visibility.Collapsed; _saveError.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(_saveError, "PreparedFilesNodeSaveError");
            root.Children.Add(_saveError);
            _reload = Button(T("configure.native.preparedFiles.reload"), "PreparedFilesNodeReload");
            _reload.Visibility = Visibility.Collapsed;
            _reload.Click += async (_, _) => { _host.Content = Text(T(LocKey + ".loadingNode"), 14, true); await LoadAsync(); };
            root.Children.Add(_reload);
            _host.Content = root;

            // Handlers attach after the initial values, so showing the saved values never reads as an edit.
            _dir.TextChanged += (_, _) => Validate(stored);
            _gb.TextChanged += (_, _) => Validate(stored);
            _mode.SelectionChanged += (_, _) => Validate(stored);
            Validate(stored);
        }

        private string DirectoryText => (_dir?.Text ?? "").Trim();

        private string? BudgetMode => (_mode?.SelectedItem as RadioButton)?.Tag as string;

        private bool TryBudget(out long? budget, out bool valid)
        {
            budget = null; valid = true;
            switch (BudgetMode)
            {
                case "none": budget = 0; return true;
                case "custom":
                    var text = (_gb?.Text ?? "").Trim();
                    if (text.Length == 0 || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) || !double.IsFinite(gb) || gb <= 0 || gb * BytesPerGb >= long.MaxValue)
                    { valid = false; return false; }
                    budget = (long)Math.Round(gb * BytesPerGb, MidpointRounding.AwayFromZero);
                    return true;
                default: budget = null; return true;
            }
        }

        private void Validate(long stored)
        {
            if (_syncing || _dir == null || _gb == null || _dirError == null || _gbError == null || _dirChange == null) return;
            var trimmed = DirectoryText;
            var dirInvalid = trimmed != _currentDir && trimmed.Length > 0 && (!trimmed.StartsWith('/') || trimmed.Length > 4096);
            _dirError.Text = T(LocKey + ".dirError"); _dirError.Visibility = dirInvalid ? Visibility.Visible : Visibility.Collapsed;
            _gb.Visibility = BudgetMode == "custom" ? Visibility.Visible : Visibility.Collapsed;
            if (_gb.Parent is UIElement row) row.Visibility = _gb.Visibility;
            TryBudget(out _, out var valid);
            var gbInvalid = BudgetMode == "custom" && !valid;
            _gbError.Text = T(LocKey + ".gbError"); _gbError.Visibility = gbInvalid ? Visibility.Visible : Visibility.Collapsed;
            var dirChanged = trimmed != _currentDir;
            if (_scratch != null) _scratch.Visibility = dirChanged ? Visibility.Collapsed : Visibility.Visible;
            _dirChange.Visibility = dirChanged ? Visibility.Visible : Visibility.Collapsed;
            if (dirChanged)
            {
                var values = new
                {
                    stored = Presentation.FormatBytes(stored), in_use = Presentation.FormatBytes(_location.InUseBytes), cached = Presentation.FormatBytes(_location.CachedBytes),
                    name = _location.Name, dir = _location.Dir,
                };
                _dirChange.Child = RichText(AdminPreparedFilesText.Rich(string.IsNullOrEmpty(_location.Dir) ? LocKey + ".dirChangeNoDir" : LocKey + ".dirChangeWithDir", values), 12);
            }
            if (_dialog != null) _dialog.IsPrimaryButtonEnabled = !dirInvalid && !gbInvalid && !_saving;
        }

        private async Task<bool> SaveAsync()
        {
            if (_node == null || _saving || _closed) return false;
            var trimmed = DirectoryText;
            if (!TryBudget(out var budget, out var valid) || !valid) return false;
            var change = new AdminNodeLocationChange(trimmed != _currentDir, trimmed.Length == 0 ? null : trimmed, budget != _currentBudget, budget);
            // Nothing changed: nothing is sent.
            if (!change.HasChanges) return true;
            _saving = true;
            if (_dialog != null) { _dialog.IsPrimaryButtonEnabled = false; _dialog.PrimaryButtonText = T(LocKey + ".saving"); }
            if (_saveError != null) _saveError.Visibility = Visibility.Collapsed;
            try
            {
                var result = await _page.Controller.SaveNodeLocationAsync(_lease, _node, change);
                if (_closed) return false;
                if (result.Outcome == AdminActionOutcome.Applied) return true;
                if (_saveError != null) { _saveError.Text = result.Message; _saveError.Visibility = Visibility.Visible; }
                // A stale validator or an unknown outcome needs the node read again; the draft stays until the administrator chooses.
                if (_reload != null) _reload.Visibility = result.Stale ? Visibility.Visible : Visibility.Collapsed;
                return false;
            }
            finally
            {
                _saving = false;
                if (_dialog != null) { _dialog.PrimaryButtonText = T("common.save"); }
                if (_node != null && !_closed) Validate(_location.Usage?.Bytes ?? Presentation.PreparedBytes(_location));
            }
        }
    }
}
