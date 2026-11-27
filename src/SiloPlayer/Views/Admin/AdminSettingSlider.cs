using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The official <c>SettingSlider</c>: a slider with its value beside it, bound to a saved setting. While a gesture is in flight (a pointer
/// held on the slider) the thumb and the value follow the draft and nothing is sent; on release the draft is committed once. A key press
/// is a gesture of its own, so each step commits once. A commit that is not taken puts the thumb back on the saved value.
/// </summary>
public sealed class AdminSettingSlider : Grid
{
    private readonly Func<double, string> _format;
    private readonly Func<double, bool> _commit;
    private readonly double _step;
    private double _saved;
    private bool _gesture, _quiet;

    public AdminSettingSlider(double minimum, double maximum, double step, double saved, Func<double, string> format, Func<double, bool> commit, string label, string automationId, bool enabled)
    {
        _format = format; _commit = commit; _step = step; _saved = saved;
        ColumnSpacing = 12; MaxWidth = 260; HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Center;
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        // The official thumb has no tooltip: the value is the read-out beside the slider.
        Slider = new Slider { Minimum = minimum, Maximum = maximum, StepFrequency = step, SmallChange = step, LargeChange = step * 10, IsThumbToolTipEnabled = false,
            MinWidth = 96, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center, Value = Math.Clamp(saved, minimum, maximum) };
        AutomationProperties.SetName(Slider, label); AutomationProperties.SetAutomationId(Slider, automationId);
        Readout = new TextBlock { Text = format(saved), FontSize = 12, FontWeight = FontWeights.Medium, MinWidth = 64, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] };
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(Readout, FontNumeralAlignment.Tabular);
        Grid.SetColumn(Readout, 1); Children.Add(Slider); Children.Add(Readout);

        Slider.ValueChanged += (_, e) => { if (_quiet) return; Readout.Text = _format(e.NewValue); if (!_gesture) Commit(); };
        // The slider's own template marks pointer events handled, so the gesture is read from the handled ones too.
        Slider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => BeginGesture()), true);
        Slider.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => EndGesture()), true);
        Slider.AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) => EndGesture()), true);
        // The pointer can hand its capture to the slider's own thumb while the button is still down, so only a release or a cancel ends the gesture, or losing the focus.
        Slider.LostFocus += (_, _) => EndGesture();
        Slider.PreviewKeyDown += OnPreviewKeyDown;
        Unloaded += (_, _) => _gesture = false;
    }

    public Slider Slider { get; }
    public TextBlock Readout { get; }
    /// <summary>The last value taken as saved; a commit is only made for a different one.</summary>
    public double Saved => _saved;
    public bool InGesture => _gesture;

    /// <summary>A pointer went down on the slider: the value is a draft until <see cref="EndGesture"/>.</summary>
    public void BeginGesture() { if (Slider.IsEnabled) _gesture = true; }
    /// <summary>The pointer was released or cancelled, or the slider lost the focus: the draft is committed once.</summary>
    public void EndGesture() { if (!_gesture) return; _gesture = false; Commit(); }

    private void Commit()
    {
        var next = Slider.Value;
        if (Math.Abs(next - _saved) <= _step * 1e-6) { Readout.Text = _format(_saved); return; }
        if (_commit(next)) { _saved = next; return; }
        Revert();
    }

    private void Revert()
    {
        _quiet = true;
        try { Slider.Value = Math.Clamp(_saved, Slider.Minimum, Slider.Maximum); Readout.Text = _format(_saved); }
        finally { _quiet = false; }
    }

    /// <summary>Radix moves ten steps on Page Up, Page Down and an arrow with Shift; the template covers the arrows, the pages, Home and End.</summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)) return;
        if (!InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down)) return;
        var rtl = FlowDirection == FlowDirection.RightToLeft;
        var up = e.Key == VirtualKey.Up || e.Key == (rtl ? VirtualKey.Left : VirtualKey.Right);
        e.Handled = true;
        Slider.Value = Math.Clamp(Slider.Value + (up ? 10 : -10) * _step, Slider.Minimum, Slider.Maximum);
    }
}
