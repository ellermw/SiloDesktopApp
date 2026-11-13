using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Services;

/// <summary>
/// Native application of the interface locale (Core owns the choice; see
/// <see cref="AppLocaleCoordinator"/>). Text comes from
/// <see cref="AppStrings.Get"/>; this class applies direction and language to
/// XAML roots and refreshes retained views when the locale changes.
///
/// Retained-view contract for call sites: build text with <c>AppStrings.Get</c>
/// in a method that can run again, and register it once:
/// <code>
/// AppLocalization.Register(this, () => SaveButton.Content = AppStrings.Get("common.save"));
/// </code>
/// The refresh runs immediately when registered on a loaded element, on the
/// element's dispatcher after every locale change while it is loaded, and on
/// the next Loaded when the locale changed while it was detached.
/// </summary>
public static class AppLocalization
{
    /// <summary>The process culture the device chose, captured before any interface language applies.</summary>
    public static string DeviceFormatCulture { get; } = CultureInfo.CurrentCulture.Name;

    /// <summary>
    /// The person's Windows language list, most preferred first. The app never
    /// changes Windows' own language settings.
    /// </summary>
    public static IReadOnlyList<string> DeviceLanguages()
    {
        try
        {
            var languages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
            if (languages.Count > 0) return [.. languages];
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
        return [CultureInfo.CurrentUICulture.Name];
    }

    /// <summary>Queues work on <paramref name="dispatcher"/>; false when the dispatcher no longer runs.</summary>
    public static Func<Action, bool> PostTo(DispatcherQueue dispatcher)
        => action => dispatcher.HasThreadAccess ? Run(action) : dispatcher.TryEnqueue(() => action());

    private static bool Run(Action action)
    {
        action();
        return true;
    }

    /// <summary>Applies the current direction and language tag to one element (a page root, a dialog, a popup child).</summary>
    public static void ApplyDirection(FrameworkElement element)
    {
        var locale = AppStrings.Current;
        element.FlowDirection = locale.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        element.Language = locale.Language;
    }

    /// <summary>
    /// Keeps the shell current: <paramref name="languageRoot"/> carries the
    /// language tag (font fallback for Japanese, Chinese and Arabic), and each
    /// direction root mirrors for a right-to-left language. The video surface
    /// and playback layer are not direction roots.
    /// </summary>
    public static IDisposable AttachShell(FrameworkElement languageRoot, params FrameworkElement[] directionRoots)
        => Register(languageRoot, () =>
        {
            var locale = AppStrings.Current;
            languageRoot.Language = locale.Language;
            foreach (var root in directionRoots) ApplyDirection(root);
        });

    /// <summary>Registers a retained view's text refresh; dispose to stop.</summary>
    public static IDisposable Register(FrameworkElement element, Action refresh)
        => new Registration(element, refresh, LocaleService.Shared);

    internal sealed class Registration : IDisposable
    {
        private readonly WeakReference<FrameworkElement> _element;
        private readonly Action _refresh;
        private readonly LocaleService _locales;
        private readonly DispatcherQueue _dispatcher;
        private long _appliedGeneration = -1;
        private bool _subscribed;
        private bool _disposed;
        private bool _dialogOpen;

        public Registration(FrameworkElement element, Action refresh, LocaleService locales)
        {
            _element = new(element);
            _refresh = refresh;
            _locales = locales;
            _dispatcher = element.DispatcherQueue;
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            // ContentDialog renders its template in a popup; the dialog object itself
            // need not raise Loaded. Its public open/close lifetime is authoritative.
            if (element is ContentDialog dialog)
            {
                dialog.Opened += OnDialogOpened;
                dialog.Closed += OnDialogClosed;
            }
            if (element.IsLoaded) Attach();
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => Attach();
        private void OnDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            _dialogOpen = true;
            Attach();
        }
        private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            _dialogOpen = false;
            Detach();
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            // Dialog template relocation can raise Unloaded after Opened.
            // Remain subscribed for the public open lifetime.
            if (!_dialogOpen)
            {
                Detach();
                // Keys and other presentation state can change while detached,
                // even when the interface language stays the same.
                _appliedGeneration = -1;
            }
        }

        private void Detach()
        {
            if (!_subscribed) return;
            _locales.Changed -= OnChanged;
            _subscribed = false;
        }

        private void Attach()
        {
            if (_disposed) return;
            if (!_subscribed)
            {
                _locales.Changed += OnChanged;
                _subscribed = true;
            }
            RefreshIfStale();
        }

        private void OnChanged(object? sender, AppLocaleChangedEventArgs args)
        {
            if (_dispatcher.HasThreadAccess) RefreshIfStale();
            else _dispatcher.TryEnqueue(RefreshIfStale);
        }

        private void RefreshIfStale()
        {
            if (_disposed || !_element.TryGetTarget(out _)) return;
            var generation = _locales.Current.Generation;
            if (generation == _appliedGeneration) return;
            _appliedGeneration = generation;
            _refresh();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_subscribed) _locales.Changed -= OnChanged;
            _subscribed = false;
            if (_element.TryGetTarget(out var element))
            {
                element.Loaded -= OnLoaded;
                element.Unloaded -= OnUnloaded;
                if (element is ContentDialog dialog)
                {
                    dialog.Opened -= OnDialogOpened;
                    dialog.Closed -= OnDialogClosed;
                }
            }
        }
    }
}
