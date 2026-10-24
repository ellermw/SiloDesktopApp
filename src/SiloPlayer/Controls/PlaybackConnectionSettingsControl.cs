using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Controls;

public sealed class PlaybackConnectionSettingsControl : UserControl
{
    private readonly SettingsViewModel _vm;
    private readonly ComboBox _mode = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _reading = Description("Not measured yet");
    private readonly TextBlock _limit = Description("");
    private readonly TextBlock _message = Description("");
    private readonly TextBlock _budget = Description("");
    private readonly Button _test = new() { Content = "Test speed", MinHeight = 36, Padding = new(16, 6, 16, 6) };
    private readonly Button _reset = new() { Content = "Reset", MinHeight = 36 };
    private readonly StackPanel _buffering = new() { Spacing = 16 };
    private readonly StackPanel _connection = new() { Spacing = 16 };
    private readonly Border _bufferingBorder;
    private readonly Border _limitBorder;
    private readonly TextBlock _connectionTitle = new() { Text = "Connection", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly Grid _connectionSection = new() { ColumnSpacing = 48, RowSpacing = 16 };
    private readonly Border _connectionBorder;
    private readonly List<(Grid Row, FrameworkElement Label, FrameworkElement Action)> _rows = [];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _networkDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _rendering;
    private bool _loaded;
    public PlaybackConnectionSettingsControl()
    {
        _vm = App.Services.GetRequiredService<SettingsViewModel>();
        var panel = new StackPanel { Spacing = 24 };
        foreach (var (label, value) in new[] { ("Never", "never"), ("Ask first", "ask"), ("Automatically", "always") })
            _mode.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        AutomationProperties.SetName(_mode, "Lower quality when buffering");
        var bufferingLabel = new StackPanel { Spacing = 4 };
        bufferingLabel.Children.Add(Label("Lower quality when buffering"));
        bufferingLabel.Children.Add(Description("What Silo does on Auto when playback keeps buffering. Silo always asks before lowering a quality you picked."));
        _buffering.Children.Add(Row(bufferingLabel, _mode));
        _limitBorder = new Border { BorderThickness = new(0, 1, 0, 0), Padding = new(0, 16, 0, 0), BorderBrush = Application.Current.Resources["BorderBrush"] as Brush, Child = Row(_limit, _reset) };
        _buffering.Children.Add(_limitBorder);
        _bufferingBorder = new Border { Child = _buffering, BorderBrush = Application.Current.Resources["BorderBrush"] as Brush };
        panel.Children.Add(_bufferingBorder);
        var speedLabel = new StackPanel { Spacing = 4 };
        speedLabel.Children.Add(Label("Connection speed")); speedLabel.Children.Add(_reading);
        _connection.Children.Add(Row(speedLabel, _test));
        _connection.Children.Add(_message); _connection.Children.Add(_budget);
        _connectionSection.ColumnDefinitions.Add(new() { Width = new(256) });
        _connectionSection.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _connectionSection.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _connectionSection.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _connectionSection.Children.Add(_connectionTitle); _connectionSection.Children.Add(_connection);
        _connectionBorder = new Border
        {
            BorderThickness = new(0, 1, 0, 0), Padding = new(0, 28, 0, 0),
            BorderBrush = Application.Current.Resources["BorderBrush"] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(45, 255, 255, 255)),
            Child = _connectionSection,
        };
        panel.Children.Add(_connectionBorder);
        Content = panel;
        AutomationProperties.SetLiveSetting(_message, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _mode.SelectionChanged += async (_, _) =>
        {
            if (!_rendering && _loaded && !_vm.ConnectionSettingsBusy && _mode.SelectedItem is ComboBoxItem { Tag: string value } && value != _vm.BufferingQualityMode)
                await _vm.SaveBufferingQualityModeAsync(value, false);
        };
        _test.Click += async (_, _) => await _vm.TestConnectionSpeedAsync();
        _reset.Click += async (_, _) => await _vm.ResetConnectionLimitAsync();
        _clock.Tick += (_, _) => _vm.RefreshConnectionPresentation();
        _networkDebounce.Tick += async (_, _) => { _networkDebounce.Stop(); if (_loaded && !_vm.ConnectionSettingsBusy) await _vm.LoadConnectionSettingsAsync(); };
        SizeChanged += (_, _) => LayoutRows();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true; _vm.PropertyChanged += Changed;
            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += NetworkChanged;
            _clock.Start(); await _vm.LoadConnectionSettingsAsync(); Render();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false; _vm.PropertyChanged -= Changed; _clock.Stop(); _networkDebounce.Stop();
            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged -= NetworkChanged;
            _vm.CancelConnectionSpeedTest();
        };
    }
    public void PlaceBufferingSettingsIn(Panel qualityRows)
    {
        if (qualityRows.Children.Contains(_bufferingBorder)) return;
        if (Content is Panel contentRows && contentRows.Children.Contains(_bufferingBorder)) contentRows.Children.Remove(_bufferingBorder);
        else if (_bufferingBorder.Parent is Panel oldParent) oldParent.Children.Remove(_bufferingBorder);
        _bufferingBorder.Margin = new(0, 16, 0, 0);
        _bufferingBorder.BorderThickness = new(0, 1, 0, 0);
        _bufferingBorder.Padding = new(0, 16, 0, 0);
        qualityRows.Children.Add(_bufferingBorder);
    }
    private void NetworkChanged(object sender) => DispatcherQueue.TryEnqueue(() => { _networkDebounce.Stop(); _networkDebounce.Start(); });
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Description(string text) => new()
    {
        Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 21.125, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        Foreground = Application.Current.Resources["SecondaryTextBrush"] as Brush,
    };
    private Grid Row(FrameworkElement label, FrameworkElement action)
    {
        var row = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        action.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(action, 1);
        row.Children.Add(label); row.Children.Add(action); _rows.Add((row, label, action));
        return row;
    }
    private void LayoutRows()
    {
        var viewport = XamlRoot?.Size.Width ?? ActualWidth;
        var narrow = viewport < 768;
        var columns = ActualWidth >= 832 && viewport >= 1024;
        _connectionSection.ColumnSpacing = columns ? 48 : 0;
        _connectionSection.ColumnDefinitions[0].Width = columns ? new(256) : new(1, GridUnitType.Star);
        _connectionSection.ColumnDefinitions[1].Width = columns ? new(1, GridUnitType.Star) : new(0);
        Grid.SetColumn(_connection, columns ? 1 : 0); Grid.SetRow(_connection, columns ? 0 : 1);
        _connectionBorder.Padding = new(0, viewport < 640 ? 24 : 28, 0, 0);
        foreach (var (_, label, action) in _rows)
        {
            Grid.SetColumnSpan(label, narrow ? 2 : 1);
            Grid.SetColumnSpan(action, narrow ? 2 : 1);
            Grid.SetColumn(action, narrow ? 0 : 1); Grid.SetRow(action, narrow ? 1 : 0);
            action.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }
        _mode.Width = viewport < 640 ? double.NaN : Math.Min(220, Math.Max(0, _rows[0].Row.ActualWidth > 0 ? _rows[0].Row.ActualWidth : ActualWidth));
        _mode.HorizontalAlignment = viewport < 640 ? HorizontalAlignment.Stretch : narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName?.Contains("Connection", StringComparison.Ordinal) == true || e.PropertyName is nameof(SettingsViewModel.BufferingQualitySupported) or nameof(SettingsViewModel.BufferingQualityMode)) Render();
    }
    private void Render()
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(Render); return; }
        _rendering = true;
        try
        {
            _bufferingBorder.Visibility = _vm.BufferingQualitySupported ? Visibility.Visible : Visibility.Collapsed;
            _connectionBorder.Visibility = _vm.ConnectionSpeedSupported ? Visibility.Visible : Visibility.Collapsed;
            _mode.IsEnabled = _vm.BufferingQualitySupported && !_vm.ConnectionSettingsBusy;
            _mode.SelectedItem = _mode.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, _vm.BufferingQualityMode));
            _test.IsEnabled = _vm.ConnectionSpeedSupported && !_vm.ConnectionSettingsBusy;
            _test.Visibility = _vm.ConnectionMetered ? Visibility.Collapsed : Visibility.Visible;
            _test.Content = _vm.ConnectionTestRunning ? "Testing…" : "Test speed";
            _reset.IsEnabled = !_vm.ConnectionSettingsBusy;
            _limit.Text = _vm.LearnedConnectionLimit;
            _limitBorder.Visibility = _limit.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _reading.Text = _vm.ConnectionReading; _message.Text = _vm.ConnectionSettingsMessage; _budget.Text = _vm.ConnectionBudget;
            _message.Visibility = _message.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _budget.Visibility = !_vm.ConnectionMetered && _budget.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            LayoutRows();
        }
        finally { _rendering = false; }
    }
}
