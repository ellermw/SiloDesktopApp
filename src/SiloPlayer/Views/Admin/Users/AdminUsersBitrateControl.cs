using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The stream bitrate cap input (official <c>StreamBitrateLimitInput</c>): Unlimited, a preset or a custom number of Mbps, with the low-bitrate warning
/// and the rule for a custom number. The Create user form and the Access cards both use it.
/// </summary>
internal sealed class AdminUsersBitrateControl
{
    private const string Unlimited = "0", Custom = "custom";
    private readonly ComboBox _choice = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _custom;
    private readonly TextBlock _unit;
    private readonly TextBlock _warning = Text("", 12, true);
    private bool _syncing;
    private long? _value;

    internal FrameworkElement Content { get; }

    /// <summary>The cap in kbps (0 is unlimited); null while nothing valid is chosen.</summary>
    internal long? Value => _value;

    /// <summary>False while a custom number is empty or cannot be stored exactly.</summary>
    internal bool IsValid { get; private set; }

    /// <summary>What the custom box holds, for a draft that cannot be saved.</summary>
    internal string DraftText => _custom.Text;

    internal event Action? Changed;

    internal AdminUsersBitrateControl(string label, string id, long? initial)
    {
        AutomationProperties.SetAutomationId(_choice, id); AutomationProperties.SetName(_choice, label);
        _choice.Items.Add(new ComboBoxItem { Content = S("web.player.bitrateLimit.unlimited", "Unlimited"), Tag = Unlimited });
        foreach (var kbps in AdminUserCreatePolicy.StreamBitratePresetsKbps)
            _choice.Items.Add(new ComboBoxItem { Content = Format(kbps), Tag = kbps.ToString(CultureInfo.InvariantCulture) });
        _choice.Items.Add(new ComboBoxItem { Content = S("web.player.bitrateLimit.custom", "Custom"), Tag = Custom });
        _custom = new TextBox { PlaceholderText = S("web.player.bitrateLimit.customPlaceholder", "e.g. 12.5"), MinWidth = 100, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(_custom, S("web.player.bitrateLimit.customAria", $"{label}, custom Mbps", ("label", label)));
        AutomationProperties.SetAutomationId(_custom, id + "Custom");
        _unit = Text(S("web.player.bitrateLimit.unit", "Mbps"), 14, true); _unit.VerticalAlignment = VerticalAlignment.Center; _unit.Visibility = Visibility.Collapsed;
        _warning.Visibility = Visibility.Collapsed;
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(_choice); Grid.SetColumn(_custom, 1); row.Children.Add(_custom); Grid.SetColumn(_unit, 2); row.Children.Add(_unit);
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(row); body.Children.Add(_warning);
        Content = body;
        _choice.SelectionChanged += (_, _) =>
        {
            if (_syncing || _choice.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
            if (tag != Custom)
            {
                _custom.Visibility = _unit.Visibility = Visibility.Collapsed;
                _value = long.Parse(tag, CultureInfo.InvariantCulture); IsValid = true; ShowWarning(null);
                Changed?.Invoke();
                return;
            }
            _custom.Visibility = _unit.Visibility = Visibility.Visible;
            _custom.Text = _value is > 0 and var current ? AdminUserCreatePolicy.StreamBitrateMbpsText(current) : "";
            CommitCustom();
        };
        _custom.TextChanged += (_, _) => { if (!_syncing) CommitCustom(); };
        Set(initial);
    }

    /// <summary>Shows a value without raising <see cref="Changed"/>; null clears the choice.</summary>
    internal void Set(long? kbps)
    {
        _syncing = true;
        if (kbps == null) _choice.SelectedIndex = -1;
        else _choice.SelectedItem = _choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == kbps.Value.ToString(CultureInfo.InvariantCulture)) ?? _choice.Items[^1];
        var isCustom = (_choice.SelectedItem as ComboBoxItem)?.Tag as string == Custom;
        _custom.Visibility = _unit.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        _custom.Text = isCustom && kbps != null ? AdminUserCreatePolicy.StreamBitrateMbpsText(kbps.Value) : "";
        _syncing = false;
        _value = kbps; IsValid = kbps != null;
        ShowWarning(kbps);
    }

    internal void Reset() => Set(null);

    internal void FocusInput() => (_custom.Visibility == Visibility.Visible ? (Control)_custom : _choice).Focus(FocusState.Programmatic);

    private void CommitCustom()
    {
        var parsed = AdminUserCreatePolicy.ParseStreamBitrateMbps(_custom.Text);
        IsValid = parsed != null;
        if (parsed != null) _value = parsed;
        ShowWarning(parsed);
        Changed?.Invoke();
    }

    private void ShowWarning(long? kbps)
    {
        _warning.Text = kbps is { } value && AdminUserCreatePolicy.IsLowStreamBitrate(value)
            ? S("web.player.bitrateLimit.lowWarning", $"Below {AdminUserCreatePolicy.LowStreamBitrateKbps / 1000} Mbps forces very low-quality transcodes.", ("mbps", AdminUserCreatePolicy.LowStreamBitrateKbps / 1000))
            : _custom.Visibility == Visibility.Visible && AdminUserCreatePolicy.ParseStreamBitrateMbps(_custom.Text) == null
                ? S("web.player.bitrateLimit.invalid", "Enter a positive number of Mbps with up to three decimals.", ("places", 3), ("min", 0)) : "";
        _warning.Visibility = _warning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    internal static string Format(long kbps) => kbps == 0 ? S("web.player.bitrateLimit.unlimited", "Unlimited")
        : S("web.player.bitrateLimit.value", $"{AdminUserCreatePolicy.StreamBitrateMbpsText(kbps)} Mbps", ("mbps", AdminUserCreatePolicy.StreamBitrateMbpsText(kbps)));

    private static string S(string key, string fallback, params (string Name, object? Value)[] args) =>
        AppStrings.Get(key, args.Length == 0 ? null : args.ToDictionary(pair => pair.Name, pair => pair.Value), fallback);
}
