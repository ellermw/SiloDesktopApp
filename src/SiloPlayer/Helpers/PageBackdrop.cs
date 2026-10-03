using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace SiloPlayer.Helpers;

/// <summary>Paints the WebUI body ambient glow on standard app page backgrounds.</summary>
public static class PageBackdrop
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(PageBackdrop), new PropertyMetadata(false, EnabledChanged));
    private static readonly ConditionalWeakTable<Page, BackdropState> States = new();
    private static readonly List<WeakReference<Page>> MountedPages = [];

    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

    /// <summary>Refreshes mounted pages after the shared theme brushes change on the UI thread.</summary>
    public static void RefreshAll()
    {
        for (var index = MountedPages.Count - 1; index >= 0; index--)
        {
            if (MountedPages[index].TryGetTarget(out var page) && States.TryGetValue(page, out var state)) state.Refresh();
            else MountedPages.RemoveAt(index);
        }
        AuthBackdrop.RefreshAll();
    }

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Page page || IsAuthenticationPage(page)) return;
        if ((bool)args.NewValue)
        {
            if (!States.TryGetValue(page, out _)) States.Add(page, new BackdropState(page));
        }
        else if (States.TryGetValue(page, out var state))
        {
            state.Detach();
            States.Remove(page);
        }
    }

    // These screens own their authentication-specific backdrop rather than the body glow.
    private static bool IsAuthenticationPage(Page page) => page is Views.LoginPage or Views.SignupPage
        or Views.ChoosePasswordPage or Views.PasswordRecoveryPage or Views.ProfileSelectPage or Views.ServerSetupRequiredPage;

    private sealed class BackdropState
    {
        private readonly Page _page;
        private readonly RadialGradientBrush _gradient = new()
        {
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Pad,
            GradientStops = { new GradientStop { Offset = 0 }, new GradientStop { Offset = .32 } }
        };
        private Brush? _original;
        private long _backgroundSubscription;
        private bool _mounted;
        private bool _updating;

        internal BackdropState(Page page)
        {
            _page = page;
            page.Loaded += Loaded;
            page.Unloaded += Unloaded;
            page.SizeChanged += Resized;
            if (page.IsLoaded) Mount();
        }

        private void Loaded(object sender, RoutedEventArgs args) => Mount();
        private void Unloaded(object sender, RoutedEventArgs args) => Unmount();
        private void Resized(object sender, SizeChangedEventArgs args) { if (_mounted) Refresh(); }
        private void BackgroundChanged(DependencyObject sender, DependencyProperty property) { if (!_updating) Refresh(); }

        private void Mount()
        {
            if (_mounted) return;
            _mounted = true;
            _backgroundSubscription = _page.RegisterPropertyChangedCallback(Control.BackgroundProperty, BackgroundChanged);
            MountedPages.Add(new WeakReference<Page>(_page));
            Refresh();
        }

        private void Unmount()
        {
            if (!_mounted) return;
            _mounted = false;
            _page.UnregisterPropertyChangedCallback(Control.BackgroundProperty, _backgroundSubscription);
            for (var index = MountedPages.Count - 1; index >= 0; index--)
                if (!MountedPages[index].TryGetTarget(out var page) || ReferenceEquals(page, _page)) MountedPages.RemoveAt(index);
        }

        internal void Refresh()
        {
            var resources = Application.Current.Resources;
            if (resources["AppBackgroundBrush"] is not SolidColorBrush background) return;
            if (!ReferenceEquals(_page.Background, background) && !ReferenceEquals(_page.Background, _gradient)) return;
            var ambient = resources.ContainsKey("AmbientBrush") && resources["AmbientBrush"] is SolidColorBrush ambientBrush
                ? ambientBrush : resources["AccentBrush"] as SolidColorBrush;
            if (ambient == null) return;
            var width = _page.ActualWidth;
            var height = _page.ActualHeight;
            if (width <= 0 || height <= 0) return;
            // CSS circle at top defaults to farthest-corner, with a transparent stop at32%.
            var radius = Math.Sqrt(width * width / 4 + height * height);
            _gradient.Center = new Point(width / 2, 0);
            _gradient.GradientOrigin = _gradient.Center;
            _gradient.RadiusX = radius;
            _gradient.RadiusY = radius;
            _gradient.GradientStops[0].Color = Blend(background.Color, ambient.Color);
            _gradient.GradientStops[1].Color = Color.FromArgb(255, background.Color.R, background.Color.G, background.Color.B);
            _original = background;
            if (!ReferenceEquals(_page.Background, _gradient))
            {
                _updating = true;
                try { _page.Background = _gradient; }
                finally { _updating = false; }
            }
        }

        private static Color Blend(Color background, Color ambient)
        {
            var amount = .1 * ambient.A / 255;
            byte Channel(byte baseValue, byte ambientValue) => (byte)Math.Round(baseValue + (ambientValue - baseValue) * amount, MidpointRounding.AwayFromZero);
            return Color.FromArgb(255, Channel(background.R, ambient.R), Channel(background.G, ambient.G), Channel(background.B, ambient.B));
        }

        internal void Detach()
        {
            Unmount();
            _page.Loaded -= Loaded;
            _page.Unloaded -= Unloaded;
            _page.SizeChanged -= Resized;
            if (ReferenceEquals(_page.Background, _gradient)) _page.Background = _original;
        }
    }
}
