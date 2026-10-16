using SiloPlayer.Helpers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Services;
using Windows.UI;

namespace SiloPlayer.Controls;

/// <summary>
/// In-player subtitle appearance panel (webui parity, commit 1adbcd1 —
/// <c>web/src/player/components/SubtitleAppearancePanel.tsx</c>).
///
/// Opened from the subtitles utility-rail button's "Appearance…" menu item.
/// Each change writes the same <c>playback.subtitle_appearance</c> user setting as the
/// settings page and pushes live to mpv via <see cref="PlayerService.ApplySubtitleAppearance"/>,
/// so the rendered subtitles update without leaving the player.
/// </summary>
public sealed partial class SubtitleAppearanceDialog : ContentDialog
{
    private readonly SettingsApi _settingsApi;
    private readonly PlayerService _playerService;
    private readonly PlaybackAuthorityScope? _authority;
    private readonly ApiRequestContext _context;
    private SubtitlePreferenceSnapshot? _preferences;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private bool _loaded, _saving, _closed;
    private long _editVersion;
    private bool IsOwnerCurrent => !_closed && _settingsApi.IsCurrentContext(_context)
        && (_authority == null || ReferenceEquals(_authority, _playerService.ActiveAuthority));
    private bool CanApplyToPlayer => ApplyToLocalPlayer && IsOwnerCurrent
        && (_authority != null || !_playerService.HasRemoteAuthority);


    private SubtitleAppearance _state = new();
    private bool _loading;

    /// <summary>Optional initial JSON for an administrator editing another device.</summary>
    public string? InitialValue { get; set; }
    public Func<Task<string?>>? LoadOverrideAsync { get; set; }
    public Func<string, Task>? SaveOverrideAsync { get; set; }
    public Func<Task>? ResetOverrideAsync { get; set; }
    public bool ApplyToLocalPlayer { get; set; } = true;
    public bool CanReset { get; set; } = true;

    // Debounce server writes so dragging the opacity slider doesn't hammer the API.
    private DispatcherTimer? _saveDebounce;
    private bool _savePending;
    private bool _resetPending;

    // Option metadata — mirrors web's FONT_SIZE_OPTIONS / FONT_FAMILY_OPTIONS etc.
    private static readonly (string Value, string Label)[] FontSizeOptions =
    [
        ("small", "Small"),
        ("medium", "Medium"),
        ("large", "Large"),
        ("xlarge", "X-Large"),
        ("xxlarge", "XX-Large"),
    ];

    private static readonly (string Value, string Label)[] FontFamilyOptions =
    [
        ("sans-serif", "Sans-serif"),
        ("serif", "Serif"),
        ("monospace", "Monospace"),
    ];

    private static readonly (string Value, string Label)[] BackgroundStyleOptions =
    [
        ("box", "Box"),
        ("shadow", "Drop Shadow"),
        ("outline", "Outline"),
        ("none", "None"),
    ];

    private static readonly (string Value, string Label)[] PositionOptions =
    [
        ("bottom", "Bottom"),
        ("lower-third", "Lower Third"),
        ("top", "Top"),
    ];

    private static readonly (string Hex, string Label)[] FontColorPalette =
    [
        ("#ffffff", "White"),
        ("#facc15", "Yellow"),
        ("#22c55e", "Green"),
        ("#06b6d4", "Cyan"),
        ("#d946ef", "Magenta"),
        ("#ef4444", "Red"),
        ("#3b82f6", "Blue"),
        ("#9ca3af", "Gray"),
        ("#000000", "Black"),
    ];

    private static readonly (string Hex, string Label)[] BgColorPalette =
    [
        ("#000000", "Black"),
        ("#374151", "Dark Gray"),
        ("#1e3a5f", "Navy"),
        ("#7f1d1d", "Dark Red"),
        ("#14532d", "Dark Green"),
    ];

    public SubtitleAppearanceDialog() : this(null) { }

    public SubtitleAppearanceDialog(PlaybackAuthorityScope? authority)
    {
        _authority = authority;
        _settingsApi = authority?.SettingsApi ?? App.Services.GetRequiredService<SettingsApi>();
        _context = _settingsApi.CaptureContext();
        _playerService = App.Services.GetRequiredService<PlayerService>();
        this.InitializeComponent();

        BuildPills(SizePillsHost, FontSizeOptions, v => _state.FontSize = v, () => _state.FontSize);
        BuildPills(FontPillsHost, FontFamilyOptions, v => _state.FontFamily = v, () => _state.FontFamily);
        BuildPills(BgStylePillsHost, BackgroundStyleOptions, v => _state.BackgroundStyle = v, () => _state.BackgroundStyle);
        BuildPills(PositionPillsHost, PositionOptions, v => _state.Position = v, () => _state.Position);
        BuildColorSwatches(FontColorSwatchHost, FontColorPalette, hex => _state.FontColor = hex, () => _state.FontColor);
        BuildColorSwatches(BgColorSwatchHost, BgColorPalette, hex => _state.BackgroundColor = hex, () => _state.BackgroundColor);

        this.Opened += async (_, _) =>
        {
            await LoadAsync();
            SyncAllFromState();
            ResetButton.Visibility = CanReset ? Visibility.Visible : Visibility.Collapsed;
        };
        this.Closing += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            _saveDebounce?.Stop();
            try { if (!await SaveNowAsync() && IsOwnerCurrent) args.Cancel = true; }
            finally { deferral.Complete(); }
        };
        Closed += (_, _) => { _closed = true; _saveDebounce?.Stop(); _settingsApi.LocalProfileSettingChanged -= OnPreferenceChanged; };
        _settingsApi.LocalProfileSettingChanged += OnPreferenceChanged;
    }

    private void OnPreferenceChanged(string key)
    {
        if (key != SubtitlePreferences.AppearanceKey || _saving || _savePending) return;
        DispatcherQueue.TryEnqueue(async () => { if (IsOwnerCurrent) { await LoadAsync(); SyncAllFromState(); } });
    }

    private void UpdateSaveStatus(string text)
    {
        SaveStatusText.Text = text;
        AppearanceBody.IsEnabled = _loaded && !_saving && IsOwnerCurrent;
        ResetButton.IsEnabled = _loaded && !_saving && IsOwnerCurrent;
        RetryButton.Visibility = text.StartsWith("Failed") ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Retry_Click(object sender, RoutedEventArgs args)
    {
        if (!_loaded) { await LoadAsync(); SyncAllFromState(); }
        else if (_resetPending) await ResetAsync();
        else await SaveNowAsync();
    }

    // ── Load / Save ──────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            if (!IsOwnerCurrent) return;
            if (LoadOverrideAsync is not null) _state = SubtitleAppearance.Parse(await LoadOverrideAsync());
            else if (InitialValue is not null) _state = SubtitleAppearance.Parse(InitialValue);
            else
            {
                _preferences = await _settingsApi.Subtitles.LoadAsync(_context);
                if (!IsOwnerCurrent) return;
                _state = _preferences.Appearance.Clone();
                CanReset = _preferences.IsAppearanceCustomized;
            }
            if (!IsOwnerCurrent) return;
            _loaded = true;
            ResetButton.Visibility = CanReset ? Visibility.Visible : Visibility.Collapsed;
            UpdateSaveStatus(SaveOverrideAsync != null ? "Changes save to this device" : "Changes save to this profile");
        }
        catch (OperationCanceledException) when (!IsOwnerCurrent) { }
        catch { if (IsOwnerCurrent) { _loaded = false; UpdateSaveStatus("Failed to load. Retry to edit."); } }
        finally { _loading = false; }
    }

    private void ScheduleSave()
    {
        if (_loading || !_loaded || !IsOwnerCurrent) return;
        UpdatePreview();
        if (CanApplyToPlayer) _playerService.ApplySubtitleAppearance(_state, _authority ?? _playerService.ActiveAuthority, _context);
        _editVersion++;
        _resetPending = false;
        _savePending = true;
        if (_saveDebounce == null)
        {
            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveDebounce.Tick += async (_, _) => { _saveDebounce.Stop(); await SaveNowAsync(); };
        }
        _saveDebounce.Stop(); _saveDebounce.Start();
    }

    private async Task<bool> SaveNowAsync()
    {
        await _writes.WaitAsync();
        try
        {
            if (_resetPending && IsOwnerCurrent) return false;
            if (!_savePending || !IsOwnerCurrent) return true;
            var version = _editVersion;
            var value = _state.Clone();
            _saving = true; UpdateSaveStatus("Saving…");
            if (SaveOverrideAsync is not null) await SaveOverrideAsync(value.ToJson());
            else if (_preferences is { } preferences) await _settingsApi.Subtitles.SaveAppearanceAsync(preferences, value);
            if (!IsOwnerCurrent) return true;
            if (version == _editVersion) _savePending = false;
            CanReset = true; ResetButton.Visibility = Visibility.Visible;
            UpdateSaveStatus("Saved");
            return true;
        }
        catch (OperationCanceledException) when (!IsOwnerCurrent) { return true; }
        catch { if (IsOwnerCurrent) UpdateSaveStatus("Failed to save. Retry or close to retry."); return false; }
        finally { _saving = false; AppearanceBody.IsEnabled = _loaded && IsOwnerCurrent; ResetButton.IsEnabled = _loaded && IsOwnerCurrent; _writes.Release(); }
    }

    // ── Pills / swatches ─────────────────────────────────────────────────

    private void BuildPills(Panel host, (string Value, string Label)[] options, Action<string> setter, Func<string> getter)
    {
        host.Children.Clear();
        foreach (var opt in options)
        {
            var btn = new Button
            {
                Content = opt.Label,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Padding = new Thickness(12, 5, 12, 5),
                BorderThickness = new Thickness(0),
                Tag = opt.Value,
            };
            RoundedBadge.SetRadius(btn, 999);
            btn.Click += (_, _) =>
            {
                setter(opt.Value);
                SyncPillGroup(host, getter);
                // BgStyle pill change toggles the dependent Opacity / Color rows.
                if (ReferenceEquals(host, BgStylePillsHost))
                {
                    UpdateBgRowsVisibility();
                }
                ScheduleSave();
            };
            host.Children.Add(btn);
        }
    }

    private static void SyncPillGroup(Panel host, Func<string> getter)
    {
        var active = getter();
        foreach (var child in host.Children)
        {
            if (child is Button btn && btn.Tag is string tag)
            {
                var isActive = tag == active;
                btn.Background = new SolidColorBrush(isActive ? Colors.White : Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
                btn.Foreground = new SolidColorBrush(isActive ? Colors.Black : Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF));
            }
        }
    }

    private void BuildColorSwatches(Panel host, (string Hex, string Label)[] palette, Action<string> setter, Func<string> getter)
    {
        host.Children.Clear();
        foreach (var sw in palette)
        {
            var ellipse = new Ellipse
            {
                Width = 26,
                Height = 26,
                Fill = new SolidColorBrush(ColorFromHex(sw.Hex)),
                StrokeThickness = 1,
                Stroke = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            };
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(2),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(15),
                Tag = sw.Hex,
                Content = ellipse,
            };
            AutomationProperties.SetName(button, sw.Label);
            ToolTipService.SetToolTip(button, sw.Label);
            button.Click += (_, _) =>
            {
                setter(sw.Hex);
                SyncSwatches(host, getter);
                ScheduleSave();
            };
            host.Children.Add(button);
        }
    }

    private static void SyncSwatches(Panel host, Func<string> getter)
    {
        var active = getter().ToLowerInvariant();
        foreach (var child in host.Children)
        {
            if (child is Button { Tag: string hex, Content: Ellipse e } button)
            {
                var isActive = string.Equals(hex, active, StringComparison.OrdinalIgnoreCase);
                e.StrokeThickness = isActive ? 3 : 1;
                e.Stroke = new SolidColorBrush(isActive
                    ? Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
                AutomationProperties.SetHelpText(button, isActive ? "Selected" : "Not selected");
            }
        }
    }

    // ── State sync (full redraw) ─────────────────────────────────────────

    private void SyncAllFromState()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            SyncPillGroup(SizePillsHost, () => _state.FontSize);
            SyncPillGroup(FontPillsHost, () => _state.FontFamily);
            SyncPillGroup(BgStylePillsHost, () => _state.BackgroundStyle);
            SyncPillGroup(PositionPillsHost, () => _state.Position);
            SyncSwatches(FontColorSwatchHost, () => _state.FontColor);
            SyncSwatches(BgColorSwatchHost, () => _state.BackgroundColor);

            OutlineToggle.IsOn = _state.TextOutline;
            OpacitySlider.Value = _state.BackgroundOpacity;
            TextOpacitySlider.Value = _state.TextOpacity;
            OpacityValueLabel.Text = $"{_state.BackgroundOpacity}%";

            UpdateBgRowsVisibility();
            UpdatePreview();
        }
        finally { _loading = wasLoading; }
    }

    private void UpdateBgRowsVisibility()
    {
        // Webui shows Opacity + Color rows only when backgroundStyle == "box".
        var showBoxRows = _state.BackgroundStyle == "box";
        BgOpacityRow.Visibility = showBoxRows ? Visibility.Visible : Visibility.Collapsed;
        BgColorRow.Visibility = showBoxRows ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Live preview ─────────────────────────────────────────────────────

    private void UpdatePreview()
    {
        // Font size — matches SettingsPage.UpdateSubtitlePreview scale so the
        // two surfaces render identically.
        double fontSize = _state.FontSize switch
        {
            "small" => 13,
            "large" => 20,
            "xlarge" => 26,
            "xxlarge" => 31,
            _ => 16,
        };
        PreviewLine1.FontSize = fontSize;
        PreviewLine2.FontSize = fontSize;

        var textColor = ColorFromHex(_state.FontColor);
        textColor.A = (byte)Math.Round(_state.TextOpacity * 2.55);
        var fontBrush = new SolidColorBrush(textColor);
        PreviewLine1.Foreground = fontBrush;
        PreviewLine2.Foreground = fontBrush;

        var ff = _state.FontFamily switch
        {
            "serif" => new FontFamily("Times New Roman"),
            "monospace" => new FontFamily("Consolas"),
            _ => new FontFamily("Segoe UI"),
        };
        PreviewLine1.FontFamily = ff;
        PreviewLine2.FontFamily = ff;

        if (_state.BackgroundStyle == "box")
        {
            var bg = ColorFromHex(_state.BackgroundColor);
            byte alpha = (byte)Math.Round(_state.BackgroundOpacity * 2.55);
            var bgBrush = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
            PreviewBg1.Background = bgBrush;
            PreviewBg2.Background = bgBrush;
        }
        else if (_state.BackgroundStyle == "shadow")
        {
            var dim = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0));
            PreviewBg1.Background = dim;
            PreviewBg2.Background = dim;
        }
        else
        {
            var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            PreviewBg1.Background = transparent;
            PreviewBg2.Background = transparent;
        }

        // Position: map to VerticalAlignment inside the preview box.
        PreviewStack.VerticalAlignment = _state.Position switch
        {
            "top" => VerticalAlignment.Top,
            "lower-third" => VerticalAlignment.Center,
            _ => VerticalAlignment.Bottom,
        };
    }

    // ── Event handlers ───────────────────────────────────────────────────

    private void OutlineToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _state.TextOutline = OutlineToggle.IsOn;
        ScheduleSave();
    }

    private void OpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        _state.BackgroundOpacity = (int)Math.Round(e.NewValue);
        OpacityValueLabel.Text = $"{_state.BackgroundOpacity}%";
        ScheduleSave();
    }

    private void TextOpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        _state.TextOpacity = Math.Clamp((int)Math.Round(e.NewValue), 1, 100);
        ScheduleSave();
    }

    private async void Reset_Click(object sender, RoutedEventArgs e) => await ResetAsync();

    private async Task ResetAsync()
    {
        if (!_loaded || !IsOwnerCurrent) return;
        _saveDebounce?.Stop();
        // Explicit Reset supersedes an unsaved edit. Retry and close must never
        // turn that old draft into a PUT after a failed DELETE.
        _resetPending = true;
        _savePending = false;
        await _writes.WaitAsync();
        _saving = true; UpdateSaveStatus("Resetting…");
        try
        {
            if (!IsOwnerCurrent) return;
            if (ResetOverrideAsync is not null) await ResetOverrideAsync();
            else if (_preferences is { } state) await _settingsApi.Subtitles.ResetAppearanceAsync(state);
            if (!IsOwnerCurrent) return;
            _resetPending = false;
            _savePending = false;
            await LoadAsync(); SyncAllFromState();
            if (CanApplyToPlayer) _playerService.ApplySubtitleAppearance(_state, _authority ?? _playerService.ActiveAuthority, _context);
        }
        catch (OperationCanceledException) when (!IsOwnerCurrent) { }
        catch { if (IsOwnerCurrent) UpdateSaveStatus("Failed to reset. Retry or try Reset again."); }
        finally { _saving = false; AppearanceBody.IsEnabled = _loaded && IsOwnerCurrent; ResetButton.IsEnabled = _loaded && IsOwnerCurrent; _writes.Release(); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    // ── Helpers ──────────────────────────────────────────────────────────

    private static Color ColorFromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Colors.White;
        var clean = hex.TrimStart('#');
        if (clean.Length == 6
            && byte.TryParse(clean.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(clean.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(clean.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return Color.FromArgb(0xFF, r, g, b);
        }
        return Colors.White;
    }
}
