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

    public AdminActivityPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminActivityViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.FilteredSessions.CollectionChanged += FilteredSessions_CollectionChanged;
        ViewModel.IPLookupResults.CollectionChanged += IPLookupResults_CollectionChanged;
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

    private void FilteredSessions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildStreamTable();
        UpdateFilterBar();
    }

    private void IPLookupResults_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildIpResults();
    }

    private void RebuildAll()
    {
        UpdateHeader();
        UpdateMethodBar();
        RebuildStreamTable();
        UpdateFilterBar();
    }

    // ===== Header =====

    private void UpdateHeader()
    {
        var total = ViewModel.TotalCount;
        LiveBadgeText.Text = $"{total} live";
        SubtitleText.Text = $"{total} active stream{(total != 1 ? "s" : "")}";
    }

    // ===== Method Distribution Bar =====

    private void UpdateMethodBar()
    {
        MethodBar.Children.Clear();
        MethodBar.ColumnDefinitions.Clear();

        int total = ViewModel.TotalCount;
        BtnDirectText.Text = $"Direct · {ViewModel.DirectCount}";
        BtnRemuxText.Text = $"Remux · {ViewModel.RemuxCount}";
        BtnTranscodeText.Text = $"Transcode · {ViewModel.TranscodeCount}";

        if (total == 0)
        {
            // Empty bar placeholder
            MethodBar.Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
            return;
        }

        MethodBar.Background = new SolidColorBrush(Colors.Transparent);

        double directPct = (double)ViewModel.DirectCount / total;
        double remuxPct = (double)ViewModel.RemuxCount / total;
        double transcodePct = (double)ViewModel.TranscodeCount / total;

        int colIdx = 0;
        if (ViewModel.DirectCount > 0)
        {
            MethodBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(directPct, GridUnitType.Star) });
            var seg = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 63, 185, 80)),
                CornerRadius = ViewModel.RemuxCount == 0 && ViewModel.TranscodeCount == 0
                    ? new CornerRadius(4)
                    : new CornerRadius(4, 0, 0, 4)
            };
            Grid.SetColumn(seg, colIdx++);
            MethodBar.Children.Add(seg);
        }
        if (ViewModel.RemuxCount > 0)
        {
            MethodBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(remuxPct, GridUnitType.Star) });
            var seg = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 56, 139, 253)),
                CornerRadius = ViewModel.DirectCount == 0 && ViewModel.TranscodeCount == 0
                    ? new CornerRadius(4)
                    : ViewModel.DirectCount == 0
                        ? new CornerRadius(4, 0, 0, 4)
                        : ViewModel.TranscodeCount == 0
                            ? new CornerRadius(0, 4, 4, 0)
                            : new CornerRadius(0)
            };
            Grid.SetColumn(seg, colIdx++);
            MethodBar.Children.Add(seg);
        }
        if (ViewModel.TranscodeCount > 0)
        {
            MethodBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(transcodePct, GridUnitType.Star) });
            var seg = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 219, 109, 40)),
                CornerRadius = ViewModel.DirectCount == 0 && ViewModel.RemuxCount == 0
                    ? new CornerRadius(4)
                    : new CornerRadius(0, 4, 4, 0)
            };
            Grid.SetColumn(seg, colIdx++);
            MethodBar.Children.Add(seg);
        }
    }

    // ===== Filter Bar =====

    private void UpdateFilterBar()
    {
        int filtered = ViewModel.FilteredSessions.Count;
        int total = ViewModel.TotalCount;
        FilterCountText.Text = $"Showing {filtered} of {total} stream{(total != 1 ? "s" : "")}";

        bool hasFilters = !string.IsNullOrEmpty(ViewModel.SearchText)
            || ViewModel.MethodFilter != null
            || ViewModel.TypeFilter != null;
        BtnClearFilters.Visibility = hasFilters ? Visibility.Visible : Visibility.Collapsed;

        UpdateTypeButtonStyles();
        UpdateMethodButtonStyles();
    }

    private void UpdateTypeButtonStyles()
    {
        SetToggleActive(BtnTypeMovie, ViewModel.TypeFilter == "movie");
        SetToggleActive(BtnTypeSeries, ViewModel.TypeFilter == "series");
    }

    private void UpdateMethodButtonStyles()
    {
        SetToggleActive(BtnFilterDirect, ViewModel.MethodFilter == "direct");
        SetToggleActive(BtnFilterRemux, ViewModel.MethodFilter == "remux");
        SetToggleActive(BtnFilterTranscode, ViewModel.MethodFilter == "transcode");
    }

    private static void SetToggleActive(Button btn, bool active)
    {
        btn.Background = active
            ? (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"]
            : new SolidColorBrush(Colors.Transparent);
        btn.BorderBrush = active
            ? (SolidColorBrush)Application.Current.Resources["AccentBrush"]
            : (SolidColorBrush)Application.Current.Resources["BorderBrush"];
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

        bool first = true;
        foreach (var session in sessions)
        {
            if (!first)
            {
                StreamsPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            first = false;
            StreamsPanel.Children.Add(BuildStreamRow(session));
        }
    }

    private static FrameworkElement BuildStreamRow(AdminSession session)
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 14, 20, 14),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        // Col 0: User (avatar + username + IP)
        var userCol = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        userCol.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        userCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string initial = session.Username.Length > 0 ? session.Username[0].ToString().ToUpper() : "?";
        var avatar = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        avatar.Child = new TextBlock
        {
            Text = initial,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var userStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        userStack.Children.Add(new TextBlock
        {
            Text = session.Username,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(session.ClientIp))
        {
            userStack.Children.Add(new TextBlock
            {
                Text = session.ClientIp,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Grid.SetColumn(avatar, 0);
        Grid.SetColumn(userStack, 1);
        userCol.Children.Add(avatar);
        userCol.Children.Add(userStack);
        Grid.SetColumn(userCol, 0);

        // Col 1: Stream (title + episode info + container/bitrate)
        string subtitleLine;
        if (session.MediaType?.ToLowerInvariant() == "episode" && session.SeriesName != null)
            subtitleLine = $"{session.SeriesName} · S{session.SeasonNumber:D2}E{session.EpisodeNumber:D2}";
        else
            subtitleLine = "Movie";

        string bitrateStr = session.StreamBitrateKbps.HasValue
            ? $"{session.StreamBitrateKbps / 1000.0:F1} Mbps"
            : "";
        string containerStr = !string.IsNullOrEmpty(session.SourceContainer)
            ? session.SourceContainer.ToUpperInvariant()
            : "";
        string techLine = string.Join(" · ", new[] { containerStr, bitrateStr }.Where(s => !string.IsNullOrEmpty(s)));

        var streamStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        streamStack.Children.Add(new TextBlock
        {
            Text = session.MediaTitle,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        streamStack.Children.Add(new TextBlock
        {
            Text = subtitleLine,
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(techLine))
        {
            streamStack.Children.Add(new TextBlock
            {
                Text = techLine,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        Grid.SetColumn(streamStack, 1);

        // Col 2: Video (decision badge + codec + resolution + transcode target)
        var videoStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        videoStack.Children.Add(BuildDecisionBadge(session.VideoDecision));

        string sourceVideo = string.Join(" ", new[]
        {
            (session.SourceVideoCodec ?? "").ToUpperInvariant(),
            session.SourceVideoResolution ?? ""
        }.Where(s => !string.IsNullOrEmpty(s)));
        if (!string.IsNullOrEmpty(sourceVideo))
        {
            videoStack.Children.Add(new TextBlock
            {
                Text = sourceVideo,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });
        }

        // Transcode target arrow
        bool isVideoTranscode = session.VideoDecision?.ToLowerInvariant() == "transcode";
        if (isVideoTranscode && !string.IsNullOrEmpty(session.TargetVideoCodec))
        {
            string targetVideo = string.Join(" ", new[]
            {
                "\u2192",
                (session.TargetVideoCodec ?? "").ToUpperInvariant(),
                session.TargetResolution ?? ""
            }.Where(s => !string.IsNullOrEmpty(s)));
            videoStack.Children.Add(new TextBlock
            {
                Text = targetVideo,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }
        Grid.SetColumn(videoStack, 2);

        // Col 3: Audio (decision badge + codec + channels + language)
        var audioStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        audioStack.Children.Add(BuildDecisionBadge(session.AudioDecision));

        string sourceAudio = string.Join(" ", new[]
        {
            (session.SourceAudioCodec ?? "").ToUpperInvariant(),
            session.SourceAudioChannels.HasValue ? $"{session.SourceAudioChannels}ch" : "",
            !string.IsNullOrEmpty(session.SourceAudioLanguage) ? $"[{session.SourceAudioLanguage.ToUpperInvariant()}]" : ""
        }.Where(s => !string.IsNullOrEmpty(s)));
        if (!string.IsNullOrEmpty(sourceAudio))
        {
            audioStack.Children.Add(new TextBlock
            {
                Text = sourceAudio,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });
        }

        bool isAudioTranscode = session.AudioDecision?.ToLowerInvariant() == "transcode" || session.TranscodeAudio;
        if (isAudioTranscode && !string.IsNullOrEmpty(session.TargetAudioCodec))
        {
            audioStack.Children.Add(new TextBlock
            {
                Text = $"\u2192 {session.TargetAudioCodec.ToUpperInvariant()}",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }
        Grid.SetColumn(audioStack, 3);

        // Col 4: Node (node name + profile name)
        var nodeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string nodeName = session.NodeDisplayName ?? session.ReportingNode;
        if (!string.IsNullOrEmpty(nodeName))
        {
            nodeStack.Children.Add(new TextBlock
            {
                Text = nodeName,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        if (!string.IsNullOrEmpty(session.ProfileName))
        {
            nodeStack.Children.Add(new TextBlock
            {
                Text = session.ProfileName,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        Grid.SetColumn(nodeStack, 4);

        // Col 5: Time elapsed (right-aligned)
        var timeBlock = new TextBlock
        {
            Text = AdminActivityViewModel.GetElapsed(session.StartedAt),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Courier New")
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

    private static Border BuildDecisionBadge(string? decision)
    {
        bool isDirect = decision?.ToLowerInvariant() is "direct" or "copy";
        bool isTranscode = decision?.ToLowerInvariant() == "transcode";

        Color bg;
        Color fg;
        string label;

        if (isDirect)
        {
            bg = Color.FromArgb(255, 35, 134, 54);
            fg = Colors.White;
            label = "Direct";
        }
        else if (isTranscode)
        {
            bg = Color.FromArgb(255, 219, 109, 40);
            fg = Colors.White;
            label = "Transcode";
        }
        else
        {
            bg = Color.FromArgb(80, 100, 100, 100);
            fg = Color.FromArgb(255, 160, 160, 160);
            label = AdminActivityViewModel.FormatDecision(decision);
        }

        var badge = new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2)
        };
        badge.Child = new TextBlock
        {
            Text = label,
            FontSize = 10,
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

            row.Children.Add(MakeCell(0, entry.Username, true));
            row.Children.Add(MakeCell(1, AdminActivityViewModel.GetTimeAgo(entry.FirstSeen), false));
            row.Children.Add(MakeCell(2, AdminActivityViewModel.GetTimeAgo(entry.LastSeen), false));

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
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = bold
                ? (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
                : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    // ===== Filter button handlers =====

    private void BtnFilterDirect_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MethodFilter = ViewModel.MethodFilter == "direct" ? null : "direct";
        UpdateMethodBar();
        UpdateFilterBar();
    }

    private void BtnFilterRemux_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MethodFilter = ViewModel.MethodFilter == "remux" ? null : "remux";
        UpdateMethodBar();
        UpdateFilterBar();
    }

    private void BtnFilterTranscode_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MethodFilter = ViewModel.MethodFilter == "transcode" ? null : "transcode";
        UpdateMethodBar();
        UpdateFilterBar();
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
        UpdateMethodBar();
        UpdateFilterBar();
    }

    private void IpLookupBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ViewModel.LookupIPCommand.Execute(null);
        }
    }
}
