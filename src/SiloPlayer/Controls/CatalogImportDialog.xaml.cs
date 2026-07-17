using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Controls;

/// <summary>
/// Mirrors the webui AdminCatalogMaintenance import dialog. Lets the admin
/// pick a catalog seed source (local file, completed export job, bucket
/// artifact, or remote URL), set a conflict mode, and add path rewrites.
/// On primary close, <see cref="BuiltRequest"/> holds the assembled request,
/// or null if the form is incomplete.
/// </summary>
public sealed partial class CatalogImportDialog : ContentDialog
{
    private readonly AdminMaintenanceViewModel _vm;
    private readonly List<(TextBox From, TextBox To)> _rewriteRows = [];

    public CatalogSeedImportRequest? BuiltRequest { get; private set; }

    public CatalogImportDialog(AdminMaintenanceViewModel vm)
    {
        _vm = vm;
        this.InitializeComponent();
        this.Loaded += (_, _) => InitializePopulation();
        this.PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private void InitializePopulation()
    {
        // Default source selection
        SourceCombo.SelectedIndex = 0;
        LocalPathBox.Text = "/catalog-seeds/";

        PopulateLocalSources();
        PopulateBucketSources();
        PopulateExportJobs();

        // Seed with one empty rewrite row so users see the input immediately
        AddRewriteRow();
    }

    private void PopulateLocalSources()
    {
        LocalSourcePicker.Items.Clear();
        foreach (var src in _vm.LocalImportSources)
        {
            LocalSourcePicker.Items.Add(new ComboBoxItem
            {
                Content = DescribeImportSource(src),
                Tag = src.Key,
            });
        }
        DetectedFilesHeader.Visibility = _vm.LocalImportSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LocalSourcePicker.Visibility = _vm.LocalImportSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateBucketSources()
    {
        BucketArtifactCombo.Items.Clear();
        foreach (var src in _vm.BucketImportSources)
        {
            BucketArtifactCombo.Items.Add(new ComboBoxItem
            {
                Content = DescribeImportSource(src),
                Tag = src.Key,
            });
        }
        if (_vm.BucketImportSources.Count == 0)
            BucketArtifactCombo.Items.Add(new ComboBoxItem { Content = "No catalog seed objects found", IsEnabled = false });
    }

    private void PopulateExportJobs()
    {
        ExportJobCombo.Items.Clear();
        foreach (var job in _vm.ExportJobs)
        {
            if (job.Status != "completed") continue;
            ExportJobCombo.Items.Add(new ComboBoxItem
            {
                Content = DescribeExportJob(job),
                Tag = job.Id,
            });
        }
        if (ExportJobCombo.Items.Count == 0)
            ExportJobCombo.Items.Add(new ComboBoxItem { Content = "No completed exports yet", IsEnabled = false });
    }

    private async void RefreshLocalSources_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RefreshLocalSourcesAsync();
        PopulateLocalSources();
    }

    private async void RefreshBucketSources_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RefreshBucketSourcesAsync();
        PopulateBucketSources();
    }

    // ─── Source switcher ─────────────────────────────────────────────────

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceCombo.SelectedItem is not ComboBoxItem sel) return;
        var tag = (string)(sel.Tag ?? "local_path");
        LocalPathSection.Visibility = tag == "local_path" ? Visibility.Visible : Visibility.Collapsed;
        ExportJobSection.Visibility = tag == "export_job" ? Visibility.Visible : Visibility.Collapsed;
        BucketArtifactSection.Visibility = tag == "bucket_artifact" ? Visibility.Visible : Visibility.Collapsed;
        RemoteUrlSection.Visibility = tag == "remote_url" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LocalSourcePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocalSourcePicker.SelectedItem is ComboBoxItem sel && sel.Tag is string key)
            LocalPathBox.Text = key;
    }

    // ─── Rewrite rows ────────────────────────────────────────────────────

    private void AddRewrite_Click(object sender, RoutedEventArgs e) => AddRewriteRow();

    private void AddRewriteRow()
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fromBox = new TextBox { PlaceholderText = "/srv/media" };
        var toBox = new TextBox { PlaceholderText = "/media" };
        var removeBtn = new Button
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 13 },
        };

        Grid.SetColumn(fromBox, 0);
        Grid.SetColumn(toBox, 1);
        Grid.SetColumn(removeBtn, 2);
        row.Children.Add(fromBox);
        row.Children.Add(toBox);
        row.Children.Add(removeBtn);

        var rowEntry = (fromBox, toBox);
        _rewriteRows.Add(rowEntry);
        RewritesPanel.Children.Add(row);

        removeBtn.Click += (_, _) =>
        {
            _rewriteRows.Remove(rowEntry);
            RewritesPanel.Children.Remove(row);
        };
    }

    // ─── Submit ──────────────────────────────────────────────────────────

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SourceCombo.SelectedItem is not ComboBoxItem srcSel) { args.Cancel = true; return; }
        var source = (string)(srcSel.Tag ?? "local_path");

        string? localPath = null, exportJobId = null, artifactKey = null, remoteUrl = null;

        switch (source)
        {
            case "local_path":
                localPath = LocalPathBox.Text?.Trim();
                if (string.IsNullOrEmpty(localPath)) { args.Cancel = true; return; }
                break;
            case "export_job":
                if (ExportJobCombo.SelectedItem is not ComboBoxItem ejSel) { args.Cancel = true; return; }
                exportJobId = ejSel.Tag as string;
                if (string.IsNullOrEmpty(exportJobId)) { args.Cancel = true; return; }
                break;
            case "bucket_artifact":
                if (BucketArtifactCombo.SelectedItem is not ComboBoxItem baSel) { args.Cancel = true; return; }
                artifactKey = baSel.Tag as string;
                if (string.IsNullOrEmpty(artifactKey)) { args.Cancel = true; return; }
                break;
            case "remote_url":
                remoteUrl = RemoteUrlBox.Text?.Trim();
                if (string.IsNullOrEmpty(remoteUrl)) { args.Cancel = true; return; }
                break;
        }

        string conflictMode = "skip_existing";
        if (ConflictCombo.SelectedItem is ComboBoxItem cmSel && cmSel.Tag is string cm)
            conflictMode = cm;

        var rewrites = new List<CatalogPathRewrite>();
        foreach (var (from, to) in _rewriteRows)
        {
            var f = from.Text?.Trim();
            var t = to.Text?.Trim();
            if (!string.IsNullOrEmpty(f) && !string.IsNullOrEmpty(t))
                rewrites.Add(new CatalogPathRewrite { From = f, To = t });
        }

        BuiltRequest = new CatalogSeedImportRequest
        {
            Source = source,
            LocalPath = localPath,
            ExportJobId = exportJobId,
            ArtifactKey = artifactKey,
            RemoteUrl = remoteUrl,
            ConflictMode = conflictMode,
            PathRewrites = rewrites,
        };

        var deferral = args.GetDeferral();
        IsPrimaryButtonEnabled = false;
        PrimaryButtonText = "Importing...";
        try
        {
            await _vm.SubmitImportAsync(BuiltRequest);
        }
        catch
        {
            args.Cancel = true;
        }
        finally
        {
            PrimaryButtonText = "Import Catalog";
            IsPrimaryButtonEnabled = true;
            deferral.Complete();
        }
    }

    // ─── Label helpers ───────────────────────────────────────────────────

    private static string DescribeImportSource(CatalogSeedImportSource src)
    {
        var label = !string.IsNullOrEmpty(src.LastModified) ? FormatTime(src.LastModified!) : src.Key;
        return $"{label} • {src.Key}";
    }

    private static string DescribeExportJob(AdminJob job)
    {
        string scope = "All libraries";
        if (job.RequestPayload != null && job.RequestPayload.TryGetValue("library_ids", out var raw) && raw != null)
        {
            int count = 0;
            if (raw is System.Collections.IList list) count = list.Count;
            else if (raw is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
                count = je.GetArrayLength();
            if (count > 0)
                scope = count == 1 ? "1 library" : $"{count} libraries";
        }
        return $"{scope} • {FormatTime(job.RequestedAt)}";
    }

    private static string FormatTime(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("g");
        return iso;
    }
}
