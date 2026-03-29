using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;
using System.Collections.Specialized;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminActivityPage : Page
{
    public AdminActivityViewModel ViewModel { get; }
    private bool _rebuildStreamPending;
    private bool _rebuildIpPending;

    // Web UI colors
    private static readonly Color Green400 = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color Green500 = Color.FromArgb(255, 34, 197, 94);
    private static readonly Color Blue400 = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color Blue500 = Color.FromArgb(255, 59, 130, 246);
    private static readonly Color Amber400 = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color Amber500 = Color.FromArgb(255, 245, 158, 11);

    public AdminActivityPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminActivityViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.FilteredSessions.CollectionChanged += (_, _) => ScheduleRebuildStream();
        ViewModel.IPLookupResults.CollectionChanged += (_, _) => ScheduleRebuildIp();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            RebuildAll();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SearchText))
        {
            SearchClearButton.Visibility = string.IsNullOrEmpty(ViewModel.SearchText)
                ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void ScheduleRebuildStream()
    {
        if (_rebuildStreamPending) return;
        _rebuildStreamPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildStreamPending = false;
            RebuildStreamTable();
            UpdateFilterBar();
        });
    }

    private void ScheduleRebuildIp()
    {
        if (_rebuildIpPending) return;
        _rebuildIpPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildIpPending = false;
            RebuildIpResults();
        });
    }

    private void RebuildAll()
    {
        UpdateHeader();
        UpdateSummaryStrip();
        RebuildStreamTable();
        UpdateFilterBar();
    }

    // ===== Header =====

    private void UpdateHeader()
    {
        var total = ViewModel.TotalCount;
        LiveBadgeText.Text = $"{total} live";
        LiveBadge.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Subtitle: "{count} active stream(s) across {nodeCount} node(s)" or "No active streams"
        if (total == 0)
        {
            SubtitleText.Text = "No active streams";
        }
        else
        {
            var nodes = ViewModel.GetNodeCounts();
            int nodeCount = nodes.Count;
            SubtitleText.Text = $"{total} active stream{(total != 1 ? "s" : "")} across {nodeCount} node{(nodeCount != 1 ? "s" : "")}";
        }
    }

    // ===== Summary Strip (Method Distribution + Node Breakdown) =====

    private void UpdateSummaryStrip()
    {
        var total = ViewModel.TotalCount;
        SummaryStrip.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (total == 0) return;

        UpdateMethodBar();
        UpdateMethodButtons();
        UpdateNodeBreakdown();
    }

    private void UpdateMethodBar()
    {
        MethodBar.Children.Clear();
        MethodBar.ColumnDefinitions.Clear();

        int total = ViewModel.TotalCount;
        if (total == 0)
        {
            MethodBar.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
            return;
        }

        MethodBar.Background = new SolidColorBrush(Colors.Transparent);
        var methods = ViewModel.GetMethodCounts();
        // Sort alphabetically like web
        var sorted = methods.OrderBy(kv => kv.Key).ToList();

        int colIdx = 0;
        foreach (var (method, count) in sorted)
        {
            double pct = (double)count / total;
            MethodBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });

            var seg = new Border
            {
                Background = new SolidColorBrush(GetMethodBarColor(method)),
                // h-1.5 rounded-full: each segment fills the 6px bar, rounded is handled by parent
            };
            Grid.SetColumn(seg, colIdx++);
            MethodBar.Children.Add(seg);
        }
    }

    private void UpdateMethodButtons()
    {
        MethodButtonsPanel.Children.Clear();

        var methods = ViewModel.GetMethodCounts();
        var sorted = methods.OrderBy(kv => kv.Key).ToList();

        foreach (var (method, count) in sorted)
        {
            var btn = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Tag = method
            };
            btn.Click += MethodButton_Click;

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            // Colored dot: h-2 w-2 rounded-full = 8px circle
            sp.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(GetMethodDotColor(method)),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Method name: capitalize, font-medium, text-[11px]
            sp.Children.Add(new TextBlock
            {
                Text = char.ToUpper(method[0]) + method[1..],
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });

            // Count: text-muted-foreground tabular-nums, text-[11px]
            sp.Children.Add(new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });

            btn.Content = sp;

            // Apply opacity-30 if a filter is active and this method doesn't match
            if (ViewModel.MethodFilter != null && ViewModel.MethodFilter != method)
                btn.Opacity = 0.3;

            MethodButtonsPanel.Children.Add(btn);
        }
    }

    private void UpdateNodeBreakdown()
    {
        var nodes = ViewModel.GetNodeCounts();
        if (nodes.Count <= 1)
        {
            NodeBreakdownPanel.Visibility = Visibility.Collapsed;
            return;
        }

        NodeBreakdownPanel.Visibility = Visibility.Visible;
        NodeButtonsPanel.Children.Clear();

        // Sort by count descending
        var sorted = nodes.OrderByDescending(kv => kv.Value).ToList();

        foreach (var (node, count) in sorted)
        {
            bool isActive = ViewModel.NodeFilter == node;
            bool hasFilter = ViewModel.NodeFilter != null;

            var btn = new Button
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                Tag = node,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
            };

            if (isActive)
            {
                // border-primary/40 bg-primary/10 text-primary
                btn.Background = new SolidColorBrush(Color.FromArgb(26, 120, 174, 252));
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(102, 120, 174, 252));
                btn.Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            }
            else
            {
                btn.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
                btn.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
                btn.Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
                if (hasFilter) btn.Opacity = 0.3;
            }

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            sp.Children.Add(new TextBlock
            {
                Text = node,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
            btn.Content = sp;
            btn.Click += NodeButton_Click;

            NodeButtonsPanel.Children.Add(btn);
        }
    }

    // ===== Filter Bar =====

    private void UpdateFilterBar()
    {
        int filtered = ViewModel.FilteredSessions.Count;
        int total = ViewModel.TotalCount;

        bool hasFilters = ViewModel.HasActiveFilters;
        BtnClearFilters.Visibility = ViewModel.ActiveFilterCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        // "Showing X of Y": only visible when filters active (search or any filter)
        if (!string.IsNullOrEmpty(ViewModel.SearchText) || ViewModel.ActiveFilterCount > 0)
        {
            FilterCountText.Text = $"Showing {filtered} of {total} streams";
            FilterCountText.Visibility = Visibility.Visible;
        }
        else
        {
            FilterCountText.Visibility = Visibility.Collapsed;
        }

        UpdateTypeButtonStyles();
        // Update method buttons opacity
        UpdateMethodButtons();
        // Update node buttons
        UpdateNodeBreakdown();
    }

    private void UpdateTypeButtonStyles()
    {
        SetTypeToggleActive(BtnTypeMovie, ViewModel.TypeFilter == "movie");
        SetTypeToggleActive(BtnTypeSeries, ViewModel.TypeFilter == "series");
    }

    private static void SetTypeToggleActive(Button btn, bool active)
    {
        if (active)
        {
            // border-primary/40 bg-primary/10 text-primary
            btn.Background = new SolidColorBrush(Color.FromArgb(26, 120, 174, 252));
            btn.BorderBrush = new SolidColorBrush(Color.FromArgb(102, 120, 174, 252));
            btn.Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        }
        else
        {
            // border-border bg-surface text-muted-foreground
            btn.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
            btn.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
            btn.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        }
    }

    // ===== Stream Table =====

    private void RebuildStreamTable()
    {
        StreamsPanel.Children.Clear();

        var sessions = ViewModel.FilteredSessions;
        if (sessions.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        for (int i = 0; i < sessions.Count; i++)
        {
            StreamsPanel.Children.Add(BuildStreamRow(sessions[i], i));
        }
    }

    private static FrameworkElement BuildStreamRow(AdminSession session, int index)
    {
        // Even rows transparent, odd rows bg-surface/20. Hover state via background.
        // Web: border-border/30 border-b, even="" odd="bg-surface/20"
        bool isOdd = index % 2 != 0;

        var row = new Grid
        {
            Padding = new Thickness(16, 10, 16, 10),
            ColumnSpacing = 12,
            Background = isOdd
                ? new SolidColorBrush(Color.FromArgb(51, 21, 30, 43)) // bg-surface/20
                : new SolidColorBrush(Colors.Transparent),
            // Bottom border for row separation
            BorderBrush = new SolidColorBrush(Color.FromArgb(77, 40, 56, 77)), // border-border/30
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        // Col 0: User — avatar 24px (h-6 w-6), text-[9px] initial, username text-[13px] font-medium, IP text-[10px]
        var userCol = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        userCol.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        userCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string username = session.Username ?? $"User #{session.UserId}";
        string initial = username.Length > 0 ? username[0].ToString().ToUpper() : "?";
        var avatar = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        avatar.Child = new TextBlock
        {
            Text = initial,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var userStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        userStack.Children.Add(new TextBlock
        {
            Text = username,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        string userMeta = session.ClientIp?.Trim() ?? "\u2014";
        userStack.Children.Add(new TextBlock
        {
            Text = userMeta,
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        Grid.SetColumn(avatar, 0);
        Grid.SetColumn(userStack, 1);
        userCol.Children.Add(avatar);
        userCol.Children.Add(userStack);
        Grid.SetColumn(userCol, 0);

        // Col 1: Stream — title text-[13px] font-medium, subtitle text-[10px], meta text-[10px]
        // Episode handling: check SeriesName != null && SeasonNumber != null && EpisodeNumber != null
        string title = AdminActivityViewModel.GetDisplayTitle(session);
        string? subtitle = AdminActivityViewModel.GetDisplaySubtitle(session);

        string sourceContainer = session.SourceContainer?.Trim().ToUpperInvariant() ?? "";
        string streamBitrate = AdminActivityViewModel.FormatSessionBitrate(session.StreamBitrateKbps);
        string streamMeta = string.Join(" \u00b7 ", new[] { sourceContainer, streamBitrate }.Where(s => !string.IsNullOrEmpty(s)));

        var streamStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        streamStack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(subtitle))
        {
            streamStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        if (!string.IsNullOrEmpty(streamMeta))
        {
            streamStack.Children.Add(new TextBlock
            {
                Text = streamMeta,
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        Grid.SetColumn(streamStack, 1);

        // Col 2: Video — decision badge text-[9px] with border, summary text-[12px] font-medium, detail text-[10px]
        string videoDecision = session.VideoDecision ?? session.PlayMethod;
        var videoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        videoStack.Children.Add(BuildDecisionBadge(videoDecision));
        videoStack.Children.Add(new TextBlock
        {
            Text = AdminActivityViewModel.FormatVideoSummary(session),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        videoStack.Children.Add(new TextBlock
        {
            Text = AdminActivityViewModel.FormatVideoDetail(session),
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(videoStack, 2);

        // Col 3: Audio — same pattern as video
        string audioDecision = session.AudioDecision ?? (session.TranscodeAudio ? "transcode" : session.PlayMethod);
        var audioStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        audioStack.Children.Add(BuildDecisionBadge(audioDecision));
        audioStack.Children.Add(new TextBlock
        {
            Text = AdminActivityViewModel.FormatAudioSummary(session),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        audioStack.Children.Add(new TextBlock
        {
            Text = AdminActivityViewModel.FormatAudioDetail(session),
            FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(audioStack, 3);

        // Col 4: Node — text-[12px] muted (not bold), profile text-[10px]
        var nodeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string nodeName = session.NodeDisplayName ?? session.ReportingNode ?? "\u2014";
        nodeStack.Children.Add(new TextBlock
        {
            Text = nodeName,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(session.ProfileName) || !string.IsNullOrEmpty(session.ProfileId))
        {
            nodeStack.Children.Add(new TextBlock
            {
                Text = session.ProfileName ?? session.ProfileId ?? "",
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        Grid.SetColumn(nodeStack, 4);

        // Col 5: Time — monospace text-[12px] right-aligned, muted
        var timeBlock = new TextBlock
        {
            Text = AdminActivityViewModel.GetElapsed(session.StartedAt),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas")
        };
        Grid.SetColumn(timeBlock, 5);

        row.Children.Add(userCol);
        row.Children.Add(streamStack);
        row.Children.Add(videoStack);
        row.Children.Add(audioStack);
        row.Children.Add(nodeStack);
        row.Children.Add(timeBlock);

        return row;
    }

    /// <summary>
    /// Decision badge: text-[9px] with border, semi-transparent bg + colored text.
    /// Direct: bg-green-500/10 text-green-400 border-green-500/15
    /// Remux: bg-blue-500/10 text-blue-400 border-blue-500/15
    /// Transcode: bg-amber-500/10 text-amber-400 border-amber-500/15
    /// </summary>
    private static Border BuildDecisionBadge(string? decision)
    {
        Color bg, fg, border;
        string label;

        switch (decision?.ToLowerInvariant())
        {
            case "direct":
            case "copy":
                // bg-green-500/10 text-green-400 border-green-500/15
                bg = Color.FromArgb(26, 34, 197, 94);       // green-500 at 10%
                fg = Green400;
                border = Color.FromArgb(38, 34, 197, 94);   // green-500 at 15%
                label = "Direct";
                break;
            case "transcode":
                // bg-amber-500/10 text-amber-400 border-amber-500/15
                bg = Color.FromArgb(26, 245, 158, 11);      // amber-500 at 10%
                fg = Amber400;
                border = Color.FromArgb(38, 245, 158, 11);  // amber-500 at 15%
                label = "Transcode";
                break;
            case "remux":
                // bg-blue-500/10 text-blue-400 border-blue-500/15
                bg = Color.FromArgb(26, 59, 130, 246);      // blue-500 at 10%
                fg = Blue400;
                border = Color.FromArgb(38, 59, 130, 246);  // blue-500 at 15%
                label = "Remux";
                break;
            default:
                bg = Color.FromArgb(26, 100, 100, 100);
                fg = Color.FromArgb(255, 160, 160, 160);
                border = Color.FromArgb(38, 100, 100, 100);
                label = AdminActivityViewModel.FormatDecision(decision);
                break;
        }

        var badge = new Border
        {
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 2)
        };
        badge.Child = new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg)
        };
        return badge;
    }

    // ===== IP Results =====

    private void RebuildIpResults()
    {
        var results = ViewModel.IPLookupResults;
        if (results.Count == 0)
        {
            IpResultsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        IpResultsPanel.Visibility = Visibility.Visible;
        IpResultsRows.Children.Clear();

        foreach (var entry in results)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            // User column (font-medium, primary link color)
            row.Children.Add(MakeCell(0, entry.Username ?? $"User #{entry.UserId}", true));
            // First Seen / Last Seen: full locale datetime (not relative time)
            row.Children.Add(MakeCell(1, AdminActivityViewModel.FormatLocaleDateTime(entry.FirstSeen), false));
            row.Children.Add(MakeCell(2, AdminActivityViewModel.FormatLocaleDateTime(entry.LastSeen), false));

            var reqBlock = new TextBlock
            {
                Text = entry.RequestCount.ToString("N0"),
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(reqBlock, 3);
            row.Children.Add(reqBlock);

            IpResultsRows.Children.Add(row);
        }
    }

    private static TextBlock MakeCell(int col, string text, bool bold)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = bold ? FontWeights.Medium : FontWeights.Normal,
            Foreground = bold
                ? (SolidColorBrush)Application.Current.Resources["AccentBrush"]
                : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    // ===== Color Helpers =====

    private static Color GetMethodBarColor(string method) => method switch
    {
        "direct" => Green500,
        "remux" => Blue500,
        "transcode" => Amber500,
        _ => Color.FromArgb(255, 100, 100, 100)
    };

    private static Color GetMethodDotColor(string method) => method switch
    {
        "direct" => Green400,
        "remux" => Blue400,
        "transcode" => Amber400,
        _ => Color.FromArgb(255, 100, 100, 100)
    };

    // ===== Filter button handlers =====

    private void MethodButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string method)
        {
            ViewModel.MethodFilter = ViewModel.MethodFilter == method ? null : method;
            UpdateSummaryStrip();
            UpdateFilterBar();
        }
    }

    private void NodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string node)
        {
            ViewModel.NodeFilter = ViewModel.NodeFilter == node ? null : node;
            UpdateSummaryStrip();
            UpdateFilterBar();
        }
    }

    private void BtnTypeMovie_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TypeFilter = ViewModel.TypeFilter == "movie" ? null : "movie";
        UpdateFilterBar();
    }

    private void BtnTypeSeries_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TypeFilter = ViewModel.TypeFilter == "series" ? null : "series";
        UpdateFilterBar();
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearFiltersCommand.Execute(null);
        UpdateSummaryStrip();
        UpdateFilterBar();
    }

    private void SearchClearButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SearchText = "";
        SearchBox.Text = "";
    }

    private void IpLookupBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ViewModel.LookupIPCommand.Execute(null);
        }
    }
}
