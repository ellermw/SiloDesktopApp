using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Services;

/// <summary>Explicit catalog keys for native controls; original English property values are fallbacks.</summary>
public static class Localized
{
    /// <summary>Preserves native uppercase label styling after resolving a catalog message.</summary>
    public static readonly DependencyProperty UppercaseProperty = DependencyProperty.RegisterAttached(
        "Uppercase", typeof(bool), typeof(Localized), new PropertyMetadata(false, OnKeyChanged));
    public static bool GetUppercase(DependencyObject target) => (bool)target.GetValue(UppercaseProperty);
    public static void SetUppercase(DependencyObject target, bool value) => target.SetValue(UppercaseProperty, value);

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetText(DependencyObject target) => (string?)target.GetValue(TextProperty);
    public static void SetText(DependencyObject target, string? value) => target.SetValue(TextProperty, value);

    public static readonly DependencyProperty ContentProperty = DependencyProperty.RegisterAttached(
        "Content", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetContent(DependencyObject target) => (string?)target.GetValue(ContentProperty);
    public static void SetContent(DependencyObject target, string? value) => target.SetValue(ContentProperty, value);

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.RegisterAttached(
        "Header", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetHeader(DependencyObject target) => (string?)target.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject target, string? value) => target.SetValue(HeaderProperty, value);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetPlaceholder(DependencyObject target) => (string?)target.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject target, string? value) => target.SetValue(PlaceholderProperty, value);

    public static readonly DependencyProperty ToolTipProperty = DependencyProperty.RegisterAttached(
        "ToolTip", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetToolTip(DependencyObject target) => (string?)target.GetValue(ToolTipProperty);
    public static void SetToolTip(DependencyObject target, string? value) => target.SetValue(ToolTipProperty, value);

    public static readonly DependencyProperty NameProperty = DependencyProperty.RegisterAttached(
        "Name", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetName(DependencyObject target) => (string?)target.GetValue(NameProperty);
    public static void SetName(DependencyObject target, string? value) => target.SetValue(NameProperty, value);

    public static readonly DependencyProperty HelpProperty = DependencyProperty.RegisterAttached(
        "Help", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetHelp(DependencyObject target) => (string?)target.GetValue(HelpProperty);
    public static void SetHelp(DependencyObject target, string? value) => target.SetValue(HelpProperty, value);

    public static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached(
        "Title", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetTitle(DependencyObject target) => (string?)target.GetValue(TitleProperty);
    public static void SetTitle(DependencyObject target, string? value) => target.SetValue(TitleProperty, value);

    public static readonly DependencyProperty PrimaryButtonProperty = DependencyProperty.RegisterAttached(
        "PrimaryButton", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetPrimaryButton(DependencyObject target) => (string?)target.GetValue(PrimaryButtonProperty);
    public static void SetPrimaryButton(DependencyObject target, string? value) => target.SetValue(PrimaryButtonProperty, value);

    public static readonly DependencyProperty SecondaryButtonProperty = DependencyProperty.RegisterAttached(
        "SecondaryButton", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetSecondaryButton(DependencyObject target) => (string?)target.GetValue(SecondaryButtonProperty);
    public static void SetSecondaryButton(DependencyObject target, string? value) => target.SetValue(SecondaryButtonProperty, value);

    public static readonly DependencyProperty CloseButtonProperty = DependencyProperty.RegisterAttached(
        "CloseButton", typeof(string), typeof(Localized), new PropertyMetadata(null, OnKeyChanged));
    public static string? GetCloseButton(DependencyObject target) => (string?)target.GetValue(CloseButtonProperty);
    public static void SetCloseButton(DependencyObject target, string? value) => target.SetValue(CloseButtonProperty, value);

    public static readonly DependencyProperty DirectionProperty = DependencyProperty.RegisterAttached(
        "Direction", typeof(bool), typeof(Localized), new PropertyMetadata(false, OnKeyChanged));
    public static bool GetDirection(DependencyObject target) => (bool)target.GetValue(DirectionProperty);
    public static void SetDirection(DependencyObject target, bool value) => target.SetValue(DirectionProperty, value);

    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new();

    private static void OnKeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element)
            throw new ArgumentException("Localized properties require a FrameworkElement.", nameof(target));
        var state = States.GetValue(element, value => new State(value));
        state.Change(args.Property);
        if (state.IsEmpty) { state.Dispose(); States.Remove(element); }
    }

    private sealed class State(FrameworkElement element) : IDisposable
    {
        private readonly Dictionary<DependencyProperty, Binding> _bindings = new();
        private IDisposable? _registration;
        private (FlowDirection Direction, string Language)? _originalDirection;
        public bool IsEmpty => _bindings.Count == 0 && !GetDirection(element);

        public void Change(DependencyProperty key)
        {
            if (key != DirectionProperty && key != UppercaseProperty)
            {
                if (string.IsNullOrWhiteSpace((string?)element.GetValue(key)))
                {
                    if (_bindings.Remove(key, out var removed) && removed.Captured)
                        element.SetValue(removed.Target, removed.Original);
                }
                else if (!_bindings.ContainsKey(key))
                    _bindings.Add(key, new Binding(TargetProperty(element, key)));
            }
            else if (key == DirectionProperty && !GetDirection(element) && _originalDirection is { } original)
            {
                element.FlowDirection = original.Direction;
                element.Language = original.Language;
                _originalDirection = null;
            }
            if (IsEmpty) return;
            _registration ??= AppLocalization.Register(element, Refresh);
            if (element.IsLoaded) Refresh();
        }

        private void Refresh()
        {
            foreach (var (key, binding) in _bindings)
            {
                // Capture after XAML has assigned all literal attributes, on first load.
                if (!binding.Captured)
                {
                    binding.Original = element.GetValue(binding.Target);
                    binding.Captured = true;
                }
                var textKey = (string)element.GetValue(key);
                var text = AppStrings.Get(textKey, fallback: binding.Original as string);
                element.SetValue(binding.Target, GetUppercase(element) ? AppStrings.Uppercase(text) : text);
            }
            if (GetDirection(element))
            {
                _originalDirection ??= (element.FlowDirection, element.Language);
                AppLocalization.ApplyDirection(element);
            }
        }

        public void Dispose() => _registration?.Dispose();
    }

    private sealed class Binding(DependencyProperty target)
    {
        public DependencyProperty Target { get; } = target;
        public object? Original { get; set; }
        public bool Captured { get; set; }
    }

    private static DependencyProperty TargetProperty(FrameworkElement element, DependencyProperty key)
    {
        if (key == NameProperty) return AutomationProperties.NameProperty;
        if (key == HelpProperty) return AutomationProperties.HelpTextProperty;
        if (key == ToolTipProperty) return ToolTipService.ToolTipProperty;
        if (key == ContentProperty && element is ContentControl) return ContentControl.ContentProperty;
        if (key == TextProperty) return element switch
        {
            TextBlock => TextBlock.TextProperty,
            TextBox => TextBox.TextProperty,
            MenuFlyoutItem => MenuFlyoutItem.TextProperty,
            MenuFlyoutSubItem => MenuFlyoutSubItem.TextProperty,
            _ => Unsupported(element, "Text")
        };
        if (key == HeaderProperty) return element switch
        {
            TextBox => TextBox.HeaderProperty,
            PasswordBox => PasswordBox.HeaderProperty,
            AutoSuggestBox => AutoSuggestBox.HeaderProperty,
            ComboBox => ComboBox.HeaderProperty,
            Expander => Expander.HeaderProperty,
            ToggleSwitch => ToggleSwitch.HeaderProperty,
            Slider => Slider.HeaderProperty,
            ListViewBase => ListViewBase.HeaderProperty,
            _ => Unsupported(element, "Header")
        };
        if (key == PlaceholderProperty) return element switch
        {
            TextBox => TextBox.PlaceholderTextProperty,
            PasswordBox => PasswordBox.PlaceholderTextProperty,
            AutoSuggestBox => AutoSuggestBox.PlaceholderTextProperty,
            ComboBox => ComboBox.PlaceholderTextProperty,
            _ => Unsupported(element, "Placeholder")
        };
        if (key == TitleProperty) return element switch
        {
            ContentDialog => ContentDialog.TitleProperty,
            TeachingTip => TeachingTip.TitleProperty,
            _ => Unsupported(element, "Title")
        };
        if (element is ContentDialog)
        {
            if (key == PrimaryButtonProperty) return ContentDialog.PrimaryButtonTextProperty;
            if (key == SecondaryButtonProperty) return ContentDialog.SecondaryButtonTextProperty;
            if (key == CloseButtonProperty) return ContentDialog.CloseButtonTextProperty;
        }
        return Unsupported(element, "dialog button");
    }

    private static DependencyProperty Unsupported(FrameworkElement element, string property)
        => throw new ArgumentException($"Localized.{property} is not supported on {element.GetType().Name}.");
}
