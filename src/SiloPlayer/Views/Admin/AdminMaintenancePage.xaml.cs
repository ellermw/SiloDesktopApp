using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Mirror of the webui AdminMaintenance page. Shows:
///   • Catalog Import &amp; Export header card with Start Export + Import Catalog actions
///   • Recent Catalog Imports list
///   • Recent Catalog Exports list (with Publish / Copy URL / Download actions)
///   • Global Job History list
/// Uses the existing AdminApi catalog seed and job endpoints; ViewModel
/// collections are rebuilt into StackPanel children on change.
/// </summary>
public sealed partial class AdminMaintenancePage : Page
{
    public AdminMaintenanceViewModel ViewModel { get; }
    private bool _rebuildImportsPending;
    private bool _rebuildExportsPending;
    private bool _rebuildAllPending;

    // Event channel subscription for realtime refresh
    private IDisposable? _eventSubscription;
    private EventChannelClient? _eventChannel;
    private DateTime _lastEventRefresh = DateTime.MinValue;

    public AdminMaintenancePage()
    {
        ViewModel = App.Services.GetRequiredService<AdminMaintenanceViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ImportJobs.CollectionChanged += (_, _) => ScheduleRebuildImports();
        ViewModel.ExportJobs.CollectionChanged += (_, _) => ScheduleRebuildExports();
        ViewModel.AllJobs.CollectionChanged += (_, _) => ScheduleRebuildAll();
        try { await ViewModel.LoadCommand.ExecuteAsync(null); }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Error: {ex.Message}"; }

        // Subscribe to realtime job events for live refresh
        try
        {
            _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
            _eventChannel.EventReceived += OnEventReceived;
            _eventSubscription = _eventChannel.Subscribe("jobs");
        }
        catch { }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_eventChannel != null)
            _eventChannel.EventReceived -= OnEventReceived;
        _eventSubscription?.Dispose();
        _eventSubscription = null;
    }

    private void OnEventReceived(string channel, string eventName, System.Text.Json.JsonElement data)
    {
        if (channel != "jobs") return;
        if ((DateTime.UtcNow - _lastEventRefresh).TotalMilliseconds < 1000) return;
        _lastEventRefresh = DateTime.UtcNow;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await Task.WhenAll(
                    ViewModel.RefreshImportJobsAsync(),
                    ViewModel.RefreshExportJobsAsync(),
                    ViewModel.RefreshAllJobsAsync());
            }
            catch { }
        });
    }

    private void ScheduleRebuildImports()
    {
        if (_rebuildImportsPending) return;
        _rebuildImportsPending = true;
        DispatcherQueue.TryEnqueue(() => { _rebuildImportsPending = false; RebuildImportJobs(); });
    }
    private void ScheduleRebuildExports()
    {
        if (_rebuildExportsPending) return;
        _rebuildExportsPending = true;
        DispatcherQueue.TryEnqueue(() => { _rebuildExportsPending = false; RebuildExportJobs(); });
    }
    private void ScheduleRebuildAll()
    {
        if (_rebuildAllPending) return;
        _rebuildAllPending = true;
        DispatcherQueue.TryEnqueue(() => { _rebuildAllPending = false; RebuildAllJobs(); });
    }

    // ─── Action handlers ─────────────────────────────────────────────────

    private async void StartExport_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartExportCommand.ExecuteAsync(null);
    }

    private async void ImportCatalog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CatalogImportDialog(ViewModel) { XamlRoot = this.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.BuiltRequest != null)
        {
            try { await ViewModel.SubmitImportAsync(dialog.BuiltRequest); }
            catch { /* VM handles error surface */ }
        }
    }

    private async void RefreshImports_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshImportJobsAsync();
    }
    private async void RefreshExports_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshExportJobsAsync();
    }
    private async void RefreshAllJobs_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAllJobsAsync();
    }

    // ─── Rebuilds ────────────────────────────────────────────────────────

    private void RebuildImportJobs()
    {
        ImportJobsPanel.Children.Clear();
        ImportJobsCountText.Text = ViewModel.ImportJobs.Count.ToString();
        if (ViewModel.ImportJobs.Count == 0)
        {
            ImportJobsPanel.Children.Add(EmptyRow("No catalog import jobs yet."));
            return;
        }
        foreach (var job in ViewModel.ImportJobs)
            ImportJobsPanel.Children.Add(BuildImportJobRow(job));
    }

    private void RebuildExportJobs()
    {
        ExportJobsPanel.Children.Clear();
        ExportJobsCountText.Text = ViewModel.ExportJobs.Count.ToString();
        if (ViewModel.ExportJobs.Count == 0)
        {
            ExportJobsPanel.Children.Add(EmptyRow("No catalog export jobs yet."));
            return;
        }
        foreach (var job in ViewModel.ExportJobs)
            ExportJobsPanel.Children.Add(BuildExportJobRow(job));
    }

    private void RebuildAllJobs()
    {
        AllJobsPanel.Children.Clear();
        AllJobsCountText.Text = ViewModel.AllJobs.Count.ToString();
        if (ViewModel.AllJobs.Count == 0)
        {
            AllJobsPanel.Children.Add(EmptyRow("No jobs yet."));
            return;
        }
        foreach (var job in ViewModel.AllJobs)
            AllJobsPanel.Children.Add(BuildAllJobRow(job));
    }

    // ─── Job rows ────────────────────────────────────────────────────────

    private FrameworkElement BuildImportJobRow(AdminJob job)
    {
        var container = new StackPanel
        {
            Spacing = 6,
            Padding = new Thickness(20, 14, 20, 14),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
        };

        // Badge row: status + description + requested timestamp
        var headRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headRow.Children.Add(BuildStatusBadge(job.Status));
        headRow.Children.Add(new TextBlock
        {
            Text = DescribeImportJob(job),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        headRow.Children.Add(new TextBlock
        {
            Text = $"requested {FormatLocalTime(job.RequestedAt)}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        container.Children.Add(headRow);

        // Message line
        if (!string.IsNullOrEmpty(job.Message))
        {
            container.Children.Add(new TextBlock
            {
                Text = job.Message,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // Progress bar
        container.Children.Add(BuildProgressBar(AdminMaintenanceViewModel.GetJobProgressPercent(job)));

        // Meta line: progress + finished + counts
        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        metaRow.Children.Add(MetaText($"Progress: {FormatJobProgress(job)}"));
        if (!string.IsNullOrEmpty(job.CompletedAt))
            metaRow.Children.Add(MetaText($"Finished: {FormatLocalTime(job.CompletedAt!)}"));
        if (job.Status == "completed" && job.ResultPayload != null)
        {
            var items = ExtractInt(job.ResultPayload, "items_created");
            var files = ExtractInt(job.ResultPayload, "files_created");
            metaRow.Children.Add(MetaText($"Imported {items} items and {files} files"));
        }
        container.Children.Add(metaRow);

        if (!string.IsNullOrEmpty(job.ErrorMessage))
        {
            container.Children.Add(new TextBlock
            {
                Text = job.ErrorMessage,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return container;
    }

    private FrameworkElement BuildExportJobRow(AdminJob job)
    {
        var container = new Grid
        {
            Padding = new Thickness(20, 14, 20, 14),
            ColumnSpacing = 12,
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Spacing = 6 };

        // Header row
        var headRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headRow.Children.Add(BuildStatusBadge(job.Status));
        headRow.Children.Add(new TextBlock
        {
            Text = DescribeExportScope(job),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        headRow.Children.Add(new TextBlock
        {
            Text = $"requested {FormatLocalTime(job.RequestedAt)}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        info.Children.Add(headRow);

        if (!string.IsNullOrEmpty(job.Message))
        {
            info.Children.Add(new TextBlock
            {
                Text = job.Message,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        metaRow.Children.Add(MetaText($"Progress: {FormatJobProgress(job)}"));
        if (!string.IsNullOrEmpty(job.CompletedAt))
            metaRow.Children.Add(MetaText($"Finished: {FormatLocalTime(job.CompletedAt!)}"));
        if (job.Status == "completed" && job.ResultPayload != null)
        {
            var items = ExtractInt(job.ResultPayload, "items_exported");
            var files = ExtractInt(job.ResultPayload, "files_exported");
            if (items > 0)
                metaRow.Children.Add(MetaText($"Exported {items} items and {files} files"));
        }
        info.Children.Add(metaRow);

        // Progress bar (matching import row pattern)
        info.Children.Add(BuildProgressBar(AdminMaintenanceViewModel.GetJobProgressPercent(job)));

        if (!string.IsNullOrEmpty(job.ErrorMessage))
        {
            info.Children.Add(new TextBlock
            {
                Text = job.ErrorMessage,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(info, 0);
        container.Children.Add(info);

        // Actions (right column): Download / Publish / Copy URL
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (!string.IsNullOrEmpty(job.DownloadUrl))
        {
            var dlBtn = new Button { Padding = new Thickness(8, 4, 8, 4), Height = 28, FontSize = 12 };
            var dlContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            dlContent.Children.Add(new FontIcon { Glyph = "\uE896", FontSize = 12 });
            dlContent.Children.Add(new TextBlock { Text = "Download" });
            dlBtn.Content = dlContent;
            dlBtn.Click += (_, _) => OpenUrl(job.DownloadUrl!);
            actions.Children.Add(dlBtn);
        }

        if (job.Status == "completed" && string.IsNullOrEmpty(job.PublicUrl))
        {
            var publishBtn = new Button { Content = "Publish", Padding = new Thickness(8, 4, 8, 4), Height = 28, FontSize = 12 };
            var capturedId = job.Id;
            publishBtn.Click += async (_, _) => await ViewModel.PublishExportAsync(capturedId);
            actions.Children.Add(publishBtn);
        }

        if (!string.IsNullOrEmpty(job.PublicUrl))
        {
            var copyBtn = new Button { Content = "Copy URL", Padding = new Thickness(8, 4, 8, 4), Height = 28, FontSize = 12 };
            var capturedUrl = job.PublicUrl!;
            copyBtn.Click += (_, _) => CopyToClipboard(capturedUrl);
            actions.Children.Add(copyBtn);
        }

        Grid.SetColumn(actions, 1);
        container.Children.Add(actions);

        return container;
    }

    private FrameworkElement BuildAllJobRow(AdminJob job)
    {
        var container = new StackPanel
        {
            Spacing = 4,
            Padding = new Thickness(20, 12, 20, 12),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
        };

        var headRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headRow.Children.Add(BuildStatusBadge(job.Status));
        headRow.Children.Add(BuildTypeBadge(JobTypeLabel(job.JobType)));
        var desc = JobDescription(job);
        if (!string.IsNullOrEmpty(desc))
        {
            // Try to make description a clickable link to the library if library_id is present
            int? jobLibraryId = null;
            if (job.RequestPayload != null && job.RequestPayload.TryGetValue("library_id", out var libIdObj))
            {
                if (libIdObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
                    jobLibraryId = je.GetInt32();
                else if (libIdObj is int intId) jobLibraryId = intId;
            }

            if (jobLibraryId.HasValue)
            {
                var capturedLibId = jobLibraryId.Value;
                var descLink = new HyperlinkButton
                {
                    Content = desc,
                    Padding = new Thickness(0),
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                descLink.Click += (_, _) => Frame.Navigate(typeof(AdminLibrariesPage));
                headRow.Children.Add(descLink);
            }
            else
            {
                headRow.Children.Add(new TextBlock
                {
                    Text = desc,
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
        }
        headRow.Children.Add(new TextBlock
        {
            Text = FormatLocalTime(job.RequestedAt),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        container.Children.Add(headRow);

        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        if (!string.IsNullOrEmpty(job.Message))
            metaRow.Children.Add(MetaText(job.Message));
        if (job.Status is "running" or "queued")
            metaRow.Children.Add(MetaText($"Progress: {FormatJobProgress(job)}"));
        var result = JobResult(job);
        if (!string.IsNullOrEmpty(result))
            metaRow.Children.Add(MetaText(result));
        if (!string.IsNullOrEmpty(job.CompletedAt))
            metaRow.Children.Add(MetaText($"Finished: {FormatLocalTime(job.CompletedAt!)}"));
        if (metaRow.Children.Count > 0)
            container.Children.Add(metaRow);

        if (!string.IsNullOrEmpty(job.ErrorMessage))
        {
            container.Children.Add(new TextBlock
            {
                Text = job.ErrorMessage,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return container;
    }

    // ─── Small UI helpers ────────────────────────────────────────────────

    private FrameworkElement EmptyRow(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12,
        Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        Margin = new Thickness(20, 18, 20, 18),
    };

    private FrameworkElement BuildStatusBadge(string status)
    {
        var (bg, fg) = status switch
        {
            "completed" => (Color.FromArgb(0x33, 0x4A, 0xDE, 0x80), Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80)),
            "running"   => (Color.FromArgb(0x33, 0x60, 0xA5, 0xFA), Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA)),
            "queued"    => (Color.FromArgb(0x33, 0xFB, 0xBF, 0x24), Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
            "failed"    => (Color.FromArgb(0x33, 0xEF, 0x6B, 0x73), Color.FromArgb(0xFF, 0xEF, 0x6B, 0x73)),
            _           => (Color.FromArgb(0x33, 0x9C, 0xA3, 0xAF), Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF)),
        };
        var badgeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        // Show a small spinner for running status (matches webui Loader2 animation)
        if (status == "running")
        {
            badgeContent.Children.Add(new ProgressRing
            {
                IsActive = true,
                Width = 12,
                Height = 12,
                Foreground = new SolidColorBrush(fg),
            });
        }
        badgeContent.Children.Add(new TextBlock
        {
            Text = status,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Background = new SolidColorBrush(bg),
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = badgeContent,
        };
    }

    private FrameworkElement BuildTypeBadge(string label)
    {
        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private FrameworkElement BuildProgressBar(double percent)
    {
        var track = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            Height = 6,
            CornerRadius = new CornerRadius(3),
        };
        var grid = new Grid();
        double pct = Math.Clamp(percent, 2.0, 100.0);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
        var fill = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentBrush"],
            CornerRadius = new CornerRadius(3),
        };
        Grid.SetColumn(fill, 0);
        grid.Children.Add(fill);
        track.Child = grid;
        return track;
    }

    private TextBlock MetaText(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
    };

    // ─── Formatters ──────────────────────────────────────────────────────

    private static string FormatLocalTime(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("g");
        return iso;
    }

    private static string FormatJobProgress(AdminJob job)
    {
        if (job.ProgressTotal > 0)
            return $"{job.ProgressCurrent:N0} / {job.ProgressTotal:N0}";
        return job.Status switch
        {
            "completed" => "Done",
            "running" => "In progress",
            _ => job.Status,
        };
    }

    private static string DescribeImportJob(AdminJob job)
    {
        if (job.RequestPayload == null) return "Catalog seed";
        if (job.RequestPayload.TryGetValue("source_label", out var label) && label is string s1 && !string.IsNullOrEmpty(s1))
            return s1;
        if (job.RequestPayload.TryGetValue("source_key", out var key) && key is string s2 && !string.IsNullOrEmpty(s2))
            return s2;
        return "Catalog seed";
    }

    private static string DescribeExportScope(AdminJob job)
    {
        if (job.RequestPayload == null) return "All libraries";
        if (job.RequestPayload.TryGetValue("library_ids", out var raw) && raw != null)
        {
            int count = CountJsonArray(raw);
            if (count > 0)
                return count == 1 ? "1 library" : $"{count} libraries";
        }
        return "All libraries";
    }

    private static string JobTypeLabel(string jobType) => jobType switch
    {
        "delete_library"   => "Library Delete",
        "image_cache_cleanup" => "Image Cache Cleanup",
        "catalog_export"   => "Catalog Export",
        "catalog_import"   => "Catalog Import",
        "item_refresh"     => "Item Refresh",
        "library_refresh"  => "Library Refresh",
        _ => jobType,
    };

    private static string JobDescription(AdminJob job)
    {
        if (job.RequestPayload == null) return "";
        switch (job.JobType)
        {
            case "delete_library":
            case "image_cache_cleanup":
                if (job.RequestPayload.TryGetValue("library_name", out var ln) && ln is string lns && !string.IsNullOrEmpty(lns))
                    return $"\"{lns}\"";
                if (job.RequestPayload.TryGetValue("library_id", out var li) && li != null)
                    return $"Library #{li}";
                return "";
            case "item_refresh":
            case "library_refresh":
                if (job.RequestPayload.TryGetValue("library_name", out var rn) && rn is string rns && !string.IsNullOrEmpty(rns))
                    return $"\"{rns}\"";
                if (job.RequestPayload.TryGetValue("library_id", out var ri) && ri != null)
                    return $"Library #{ri}";
                return "All libraries";
            case "catalog_export":
                return DescribeExportScope(job);
            case "catalog_import":
                return DescribeImportJob(job);
            default:
                return "";
        }
    }

    private static string JobResult(AdminJob job)
    {
        if (job.Status != "completed" || job.ResultPayload == null) return "";
        switch (job.JobType)
        {
            case "library_refresh":
                {
                    var total = ExtractInt(job.ResultPayload, "total_items");
                    if (total == 0) return "No library items to refresh";
                    var withIds = ExtractInt(job.ResultPayload, "items_with_ids");
                    var without = ExtractInt(job.ResultPayload, "items_without_ids");
                    var refOk = ExtractInt(job.ResultPayload, "refreshed_ok");
                    var refFail = ExtractInt(job.ResultPayload, "refreshed_failed");
                    var pipelineOk = ExtractInt(job.ResultPayload, "pipeline_ok");
                    var pipelineFail = ExtractInt(job.ResultPayload, "pipeline_failed");
                    return $"Total {total}, {withIds} direct, {without} unmatched, direct {refOk} ok/{refFail} failed, pipeline {pipelineOk} ok/{pipelineFail} failed";
                }
            case "delete_library":
                {
                    var files = ExtractInt(job.ResultPayload, "deleted_media_files");
                    var items = ExtractInt(job.ResultPayload, "deleted_orphaned_items");
                    var parts = new List<string>();
                    if (files > 0) parts.Add($"{files} files");
                    if (items > 0) parts.Add($"{items} items");
                    if (ExtractBool(job.ResultPayload, "image_cleanup_queued"))
                    {
                        var directories = ExtractInt(job.ResultPayload, "image_cleanup_dirs");
                        if (directories > 0)
                            parts.Add($"queued cache cleanup for {directories} director{(directories == 1 ? "y" : "ies")}");
                    }
                    return parts.Count > 0 ? $"Deleted {string.Join(", ", parts)}" : "Deleted (empty)";
                }
            case "image_cache_cleanup":
                {
                    var prefixes = ExtractInt(job.ResultPayload, "deleted_prefixes");
                    var objects = ExtractInt(job.ResultPayload, "deleted_s3_objects");
                    return $"Deleted {objects} cached object{(objects == 1 ? "" : "s")} across {prefixes} prefix{(prefixes == 1 ? "" : "es")}";
                }
            case "catalog_export":
                {
                    var items = ExtractInt(job.ResultPayload, "items_exported");
                    if (items == 0) return "";
                    var files = ExtractInt(job.ResultPayload, "files_exported");
                    return $"Exported {items} items, {files} files";
                }
            case "catalog_import":
                {
                    var items = ExtractInt(job.ResultPayload, "items_created");
                    if (items == 0) return "";
                    var files = ExtractInt(job.ResultPayload, "files_created");
                    return $"Imported {items} items, {files} files";
                }
            default:
                return "";
        }
    }

    private static int ExtractInt(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var raw) || raw == null) return 0;
        if (raw is int i) return i;
        if (raw is long l) return (int)l;
        if (raw is double d) return (int)d;
        if (raw is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.Number && je.TryGetInt32(out var ji)) return ji;
        }
        if (int.TryParse(raw.ToString(), out var parsed)) return parsed;
        return 0;
    }

    private static bool ExtractBool(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var raw) || raw == null) return false;
        if (raw is bool value) return value;
        if (raw is System.Text.Json.JsonElement element &&
            element.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
            return element.GetBoolean();
        return bool.TryParse(raw.ToString(), out var parsed) && parsed;
    }

    private static int CountJsonArray(object raw)
    {
        if (raw is System.Collections.IList list) return list.Count;
        if (raw is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
            return je.GetArrayLength();
        return 0;
    }

    // ─── Clipboard / open URL ────────────────────────────────────────────

    private static void CopyToClipboard(string text)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
        }
        catch { }
    }

    private static async void OpenUrl(string url)
    {
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); }
        catch { }
    }
}
