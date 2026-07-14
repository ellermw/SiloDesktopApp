using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminNodesPage : Page
{
    public AdminNodesViewModel ViewModel { get; }
    private bool _rebuildProxyPending;
    private bool _rebuildTranscodePending;

    private sealed record NodeFormResult(string Name, string Url, string Group, int? MaxJobs, int? MaxBandwidthKbps);

    public AdminNodesPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminNodesViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ProxyNodes.CollectionChanged += (_, _) => ScheduleRebuildProxy();
        ViewModel.TranscodeNodes.CollectionChanged += (_, _) => ScheduleRebuildTranscode();

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void ScheduleRebuildProxy()
    {
        if (_rebuildProxyPending) return;
        _rebuildProxyPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildProxyPending = false;
            RebuildProxyRows();
        });
    }

    private void ScheduleRebuildTranscode()
    {
        if (_rebuildTranscodePending) return;
        _rebuildTranscodePending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildTranscodePending = false;
            RebuildTranscodeRows();
        });
    }

    // ===== Row Builders =====

    private void RebuildProxyRows()
    {
        ProxyNodesPanel.Children.Clear();
        ProxyNodeCountText.Text = ViewModel.ProxyNodes.Count.ToString();

        if (ViewModel.ProxyNodes.Count == 0)
        {
            ProxyEmptyState.Visibility = Visibility.Visible;
            return;
        }
        ProxyEmptyState.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var node in ViewModel.ProxyNodes)
        {
            if (!isFirst)
            {
                ProxyNodesPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            ProxyNodesPanel.Children.Add(BuildNodeRow(node, showJobs: false));
        }
    }

    private void RebuildTranscodeRows()
    {
        TranscodeNodesPanel.Children.Clear();
        TranscodeNodeCountText.Text = ViewModel.TranscodeNodes.Count.ToString();

        if (ViewModel.TranscodeNodes.Count == 0)
        {
            TranscodeEmptyState.Visibility = Visibility.Visible;
            return;
        }
        TranscodeEmptyState.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var node in ViewModel.TranscodeNodes)
        {
            if (!isFirst)
            {
                TranscodeNodesPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            TranscodeNodesPanel.Children.Add(BuildNodeRow(node, showJobs: true));
        }
    }

    // ===== Node Row =====

    private FrameworkElement BuildNodeRow(StreamNode node, bool showJobs)
    {
        var row = new Grid
        {
            // Web tables use compact 44px data rows. The WinUI toggle already contributes
            // most of that height, so large vertical padding made every row ~50% too tall.
            Padding = new Thickness(20, 6, 20, 6),
            ColumnSpacing = 12
        };

        if (showJobs)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        }
        else
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        }

        // ---- Name ----
        var nameBlock = new TextBlock
        {
            Text = node.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(nameBlock, 0);
        row.Children.Add(nameBlock);

        // ---- URL ----
        var urlBlock = new TextBlock
        {
            Text = node.Url,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(urlBlock, 1);
        row.Children.Add(urlBlock);

        var groupBadge = new TextBlock { Text = string.IsNullOrWhiteSpace(node.Group) ? "—" : node.Group, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(groupBadge, 2);
        row.Children.Add(groupBadge);

        // ---- Status badge (Enabled/Disabled toggle) ----
        var toggleSwitch = new ToggleSwitch
        {
            IsOn = node.Enabled,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        var capturedNode = node;
        toggleSwitch.Toggled += async (_, _) =>
        {
            await ViewModel.ToggleNodeCommand.ExecuteAsync(capturedNode.Id);
            if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
        };
        Grid.SetColumn(toggleSwitch, 3);
        row.Children.Add(toggleSwitch);

        // ---- Health indicator ----
        Color healthColor;
        string healthText;
        if (!node.Enabled)
        {
            healthColor = Color.FromArgb(255, 161, 161, 161); // gray-400
            healthText = "Disabled";
        }
        else if (node.Healthy)
        {
            healthColor = Color.FromArgb(255, 34, 197, 94);   // green-500
            healthText = "Healthy";
        }
        else
        {
            healthColor = Color.FromArgb(255, 239, 68, 68);   // red-500
            healthText = "Unhealthy";
        }

        var healthPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        healthPanel.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(healthColor),
            VerticalAlignment = VerticalAlignment.Center
        });
        healthPanel.Children.Add(new TextBlock
        {
            Text = healthText,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        Grid.SetColumn(healthPanel, 4);
        row.Children.Add(healthPanel);

        int col = 5;

        // ---- Active Jobs (transcode only) ----
        if (showJobs)
        {
            FrameworkElement jobsEl;
            if (node.ActiveJobs > 0)
            {
                var jobsBadge = new Border
                {
                    Background = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 3, 7, 3),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                jobsBadge.Child = new TextBlock
                {
                    Text = $"{node.ActiveJobs} / {node.MaxJobs ?? 0}",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"]
                };
                jobsEl = jobsBadge;
            }
            else
            {
                jobsEl = new TextBlock
                {
                    Text = $"0 / {node.MaxJobs ?? 0}",
                    FontSize = 12,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            Grid.SetColumn(jobsEl, col);
            row.Children.Add(jobsEl);
            col++;
        }
        else
        {
            var streams = new TextBlock { Text = node.ActiveJobs.ToString(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(streams, col++);
            row.Children.Add(streams);
            var maxMbps = (node.MaxBandwidthKbps ?? 0) / 1000d;
            var egressMbps = node.EgressKbps / 1000d;
            var egress = new TextBlock { Text = $"{egressMbps:0.#} / {maxMbps:0.#} Mbps", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(egress, col++);
            row.Children.Add(egress);
        }

        // ---- Last Check ----
        string lastCheckText = "Never";
        if (!string.IsNullOrEmpty(node.LastHealthCheck) && DateTime.TryParse(node.LastHealthCheck, out var checkDt))
            lastCheckText = checkDt.ToLocalTime().ToString("G");

        var lastCheckBlock = new TextBlock
        {
            Text = lastCheckText,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(lastCheckBlock, col);
        row.Children.Add(lastCheckBlock);
        col++;

        // ---- Actions ----
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        var checkBtn = MakeIconButton("\uE72C", "Check health");
        var editBtn = MakeIconButton("\uE70F", "Edit node");
        var deleteBtn = MakeIconButton("\uE74D", "Delete node");

        checkBtn.Click += async (_, _) =>
        {
            checkBtn.IsEnabled = false;
            // Spin the icon while health check is pending
            var icon = checkBtn.Content as FontIcon;
            Microsoft.UI.Xaml.Media.Animation.Storyboard? spin = null;
            if (icon != null)
            {
                icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                icon.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform();
                spin = new Microsoft.UI.Xaml.Media.Animation.Storyboard { RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever };
                var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { From = 0, To = 360, Duration = new Duration(TimeSpan.FromSeconds(1)) };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, icon.RenderTransform);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Angle");
                spin.Children.Add(anim);
                spin.Begin();
            }
            try
            {
                await ViewModel.CheckHealthCommand.ExecuteAsync(capturedNode.Id);
                if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
            }
            finally
            {
                spin?.Stop();
                if (icon != null) icon.RenderTransform = null;
                checkBtn.IsEnabled = true;
            }
        };

        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedNode);
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedNode);

        actionsPanel.Children.Add(checkBtn);
        actionsPanel.Children.Add(editBtn);
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(actionsPanel, col);
        row.Children.Add(actionsPanel);

        row.PointerEntered += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); };
        row.PointerExited += (s, _) => { if (s is Grid g) g.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); };
        return row;
    }

    // ===== Header Buttons =====

    private async void AddProxyNodeButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync("proxy");
    }

    private async void AddTranscodeNodeButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync("transcode");
    }

    // ===== Create Dialog =====

    private async Task OpenCreateDialogAsync(string nodeType)
    {
        var (formContent, getResult) = BuildNodeForm(null, nodeType);

        var dialog = new ContentDialog
        {
            Title = $"Add {(nodeType == "proxy" ? "Proxy" : "Transcode")} Node",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var form = getResult();
            if (string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.Url)) return;

            try
            {
                await ViewModel.CreateNodeCommand.ExecuteAsync(new CreateNodeRequest
                {
                    Name = form.Name,
                    Url = form.Url,
                    Type = nodeType,
                    Group = form.Group,
                    MaxJobs = form.MaxJobs,
                    MaxBandwidthKbps = form.MaxBandwidthKbps
                });
                if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
            }
            catch { }
        }
    }

    // ===== Edit Dialog =====

    private async Task OpenEditDialogAsync(StreamNode node)
    {
        var (formContent, getResult) = BuildNodeForm(node, node.Type);

        var dialog = new ContentDialog
        {
            Title = "Edit Node",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var form = getResult();
            if (string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.Url)) return;

            try
            {
                await ViewModel.UpdateNodeCommand.ExecuteAsync(new AdminNodesViewModel.NodeUpdateArgs(
                    node.Id,
                    form.Name,
                    form.Url,
                    form.Group,
                    form.MaxJobs,
                    form.MaxBandwidthKbps));
                if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
            }
            catch { }
        }
    }

    // ===== Delete Dialog =====

    private async Task OpenDeleteDialogAsync(StreamNode node)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete node",
            Content = $"Delete stream node \"{node.Name}\"? This action cannot be undone.",
            PrimaryButtonText = "Delete",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.DeleteNodeCommand.ExecuteAsync(node.Id);
                if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
            }
            catch { }
        }
    }

    // ===== Form Builder =====

    private (FrameworkElement Content, Func<NodeFormResult> GetResult) BuildNodeForm(
        StreamNode? existingNode, string nodeType)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = nodeType == "proxy" ? "Proxy Node 1" : "Transcode Node 1",
            Text = existingNode?.Name ?? "",
            CornerRadius = new CornerRadius(6),
            FontSize = 14
        };

        string urlPlaceholder = nodeType == "proxy"
            ? "https://proxy1.example.com"
            : "http://10.0.0.5:8082";

        var urlBox = new TextBox
        {
            PlaceholderText = urlPlaceholder,
            Text = existingNode?.Url ?? "",
            CornerRadius = new CornerRadius(6),
            FontSize = 14
        };

        var groupBox = new TextBox
        {
            PlaceholderText = nodeType == "proxy" ? "edge" : "default",
            Text = existingNode?.Group ?? "",
            CornerRadius = new CornerRadius(6),
            FontSize = 14
        };

        var maxJobsBox = new NumberBox
        {
            Value = existingNode?.MaxJobs is int maxJobs ? maxJobs : double.NaN,
            PlaceholderText = "Unlimited",
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            CornerRadius = new CornerRadius(6),
            FontSize = 14
        };

        var maxBandwidthBox = new NumberBox
        {
            Value = existingNode?.MaxBandwidthKbps is int maxBandwidth ? maxBandwidth : double.NaN,
            PlaceholderText = "Unlimited",
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            CornerRadius = new CornerRadius(6),
            FontSize = 14
        };

        string urlHint = nodeType == "proxy"
            ? "Must be publicly accessible by streaming clients."
            : "Must be reachable from proxy nodes and the backend. A private/internal IP is fine — no public URL needed.";

        var urlHintBlock = new TextBlock
        {
            Text = urlHint,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        // Type badge (read-only)
        var typeBadge = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        typeBadge.Child = new TextBlock
        {
            Text = nodeType == "proxy" ? "Proxy" : "Transcode",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };

        var form = new StackPanel { Width = 512, Spacing = 16 };

        void AddField(string label, FrameworkElement control, FrameworkElement? hint = null)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
            });
            group.Children.Add(control);
            if (hint != null) group.Children.Add(hint);
            form.Children.Add(group);
        }

        AddField("Name", nameBox);
        AddField("Type", typeBadge);
        AddField("URL", urlBox, urlHintBlock);
        AddField("Group", groupBox);
        AddField("Max jobs", maxJobsBox);
        AddField("Max bandwidth (Kbps)", maxBandwidthBox);

        static int? ReadLimit(NumberBox box)
        {
            if (double.IsNaN(box.Value) || box.Value <= 0)
                return null;
            return (int)Math.Round(box.Value);
        }

        return (form, () => new NodeFormResult(
            nameBox.Text.Trim(),
            urlBox.Text.Trim(),
            groupBox.Text.Trim(),
            ReadLimit(maxJobsBox),
            ReadLimit(maxBandwidthBox)));
    }

    // ===== Helpers =====

    private static Button MakeIconButton(string glyph, string tooltip, Color? fgColor = null)
    {
        var fg = fgColor.HasValue
            ? new SolidColorBrush(fgColor.Value)
            : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        var btn = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = fg
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }
}
