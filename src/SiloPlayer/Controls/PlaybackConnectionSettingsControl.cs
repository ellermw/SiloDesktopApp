using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Controls;

public sealed class PlaybackConnectionSettingsControl : UserControl
{
    private readonly SettingsViewModel _vm;
    private readonly ComboBox _mode = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _device = new() { Content = "Apply on this device only" };
    private readonly TextBlock _reading = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _limit = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _save = new() { Content = "Save buffering preference" };
    private readonly Button _test = new() { Content = "Test speed" };
    private readonly Button _reset = new() { Content = "Reset learned limit" };
    public PlaybackConnectionSettingsControl()
    {
        _vm = App.Services.GetRequiredService<SettingsViewModel>();
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Connection and buffering", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "When playback keeps buffering, Auto can lower quality to keep playing. A quality you choose always asks first. Learned limits last 24 hours on this device and connection.", TextWrapping = TextWrapping.Wrap });
        foreach (var (label, value) in new[] { ("Lower automatically", "always"), ("Ask before lowering", "ask"), ("Keep quality", "never") }) _mode.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        AutomationProperties.SetName(_mode, "Buffering quality behavior");
        panel.Children.Add(_mode); panel.Children.Add(_device); panel.Children.Add(_save);
        panel.Children.Add(_reading); panel.Children.Add(_limit);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actions.Children.Add(_test); actions.Children.Add(_reset); panel.Children.Add(actions); panel.Children.Add(_message);
        Content = panel;
        _save.Click += async (_, _) => { if (_mode.SelectedItem is ComboBoxItem item && item.Tag is string value) await _vm.SaveBufferingQualityModeAsync(value, _device.IsChecked == true); };
        _test.Click += async (_, _) => await _vm.TestConnectionSpeedAsync();
        _reset.Click += async (_, _) => await _vm.ResetConnectionLimitAsync();
        Loaded += async (_, _) => { _vm.PropertyChanged += Changed; await _vm.LoadConnectionSettingsAsync(); Render(true); };
        Unloaded += (_, _) => _vm.PropertyChanged -= Changed;
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Render(e.PropertyName is nameof(SettingsViewModel.BufferingQualityMode) or nameof(SettingsViewModel.BufferingDeviceOverride));
    private void Render(bool selections = false)
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(() => Render(selections)); return; }
        _mode.IsEnabled = _device.IsEnabled = _save.IsEnabled = _vm.BufferingQualitySupported && !_vm.ConnectionSettingsBusy;
        _test.IsEnabled = _vm.BufferingQualitySupported && !_vm.ConnectionSettingsBusy;
        _reset.IsEnabled = _test.IsEnabled && _vm.CanResetLearnedLimit;
        if (selections)
        {
            _mode.SelectedItem = _mode.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, _vm.BufferingQualityMode));
            _device.IsChecked = _vm.BufferingDeviceOverride;
        }
        _reading.Text = _vm.ConnectionReading; _limit.Text = _vm.LearnedConnectionLimit; _message.Text = _vm.ConnectionSettingsMessage;
    }
}
