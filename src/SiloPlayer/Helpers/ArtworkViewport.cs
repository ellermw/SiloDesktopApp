using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

/// <summary>Motion belongs to the real scrolling surface, never to a global card list.</summary>
internal static class ArtworkViewport
{
    private static readonly ConditionalWeakTable<ScrollViewer, ArtworkMotionPolicy> Policies = new();
    private static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "ArtworkActive", typeof(bool), typeof(ArtworkViewport), new PropertyMetadata(true));

    internal static void SetActive(FrameworkElement owner, bool active) => owner.SetValue(ActiveProperty, active);

    // Collapsed retained tabs do not necessarily unload their realized cards.
    // Each loaded card owns and disposes its ancestor subscriptions.
    internal static IDisposable ObserveActivity(FrameworkElement card, Action<bool> changed) => new Activity(card, changed);

    // One watch per observed element, however many cards sit below it: XAML holds two callbacks per element
    // instead of two per card per ancestor. Every callback XAML holds is a managed object the garbage
    // collector's reference-tracker walk visits on each full collection; per card per ancestor that was
    // about 44 per card, 12,800 on a built Home page.
    private static readonly ConditionalWeakTable<FrameworkElement, Watch> Watches = new();

    private sealed class Watch
    {
        private readonly FrameworkElement _element;
        private readonly long _visibility, _active;
        private readonly List<Activity> _activities = new();

        internal Watch(FrameworkElement element)
        {
            _element = element;
            _visibility = element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Changed());
            _active = element.RegisterPropertyChangedCallback(ActiveProperty, (_, _) => Changed());
        }

        internal bool IsActive => _element.Visibility == Visibility.Visible && (bool)_element.GetValue(ActiveProperty);

        internal void Add(Activity activity) => _activities.Add(activity);

        internal void Remove(Activity activity)
        {
            _activities.Remove(activity);
            if (_activities.Count > 0) return;
            _element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, _visibility);
            _element.UnregisterPropertyChangedCallback(ActiveProperty, _active);
            Watches.Remove(_element);
        }

        private void Changed()
        {
            // A notified card can dispose its activity (and so this list) while it is being walked.
            foreach (var activity in _activities.ToArray()) activity.Notify();
        }
    }

    private sealed class Activity : IDisposable
    {
        private readonly List<Watch> _owners = new();
        private readonly Action<bool> _changed;
        internal Activity(FrameworkElement card, Action<bool> changed)
        {
            _changed = changed;
            for (DependencyObject? node = card; node != null; node = VisualTreeHelper.GetParent(node))
                if (node is FrameworkElement element)
                {
                    var watch = Watches.GetValue(element, owner => new Watch(owner));
                    watch.Add(this);
                    _owners.Add(watch);
                }
            Notify();
        }
        internal void Notify() => _changed(_owners.All(owner => owner.IsActive));
        public void Dispose()
        {
            foreach (var owner in _owners) owner.Remove(this);
            _owners.Clear();
        }
    }

    internal static ArtworkMotionPolicy For(ScrollViewer scroll) => For(scroll, ArtworkMotionPolicy.DefaultDeferSpeed);

    /// <summary>The scroller's policy; <paramref name="deferSpeed"/> applies only when this call creates it.</summary>
    internal static ArtworkMotionPolicy For(ScrollViewer scroll, double deferSpeed) => Policies.GetValue(scroll, owner =>
    {
        var policy = new ArtworkMotionPolicy(deferSpeed: deferSpeed);
        policy.Reset(owner.HorizontalOffset, owner.VerticalOffset);
        owner.ViewChanged += (_, _) => policy.Observe(owner.HorizontalOffset, owner.VerticalOffset);
        owner.Loaded += (_, _) => policy.Reset(owner.HorizontalOffset, owner.VerticalOffset);
        owner.Unloaded += (_, _) => { if (!owner.IsLoaded) policy.Reset(owner.HorizontalOffset, owner.VerticalOffset); };
        return policy;
    });

    internal static void ObserveAncestors(FrameworkElement card) => GetPolicies(card, null);

    internal static bool IsDeferred(FrameworkElement card, ArtworkMotionPolicy? owner = null) =>
        GetPolicies(card, owner).Any(policy => policy.IsDeferred);

    private static List<ArtworkMotionPolicy> GetPolicies(FrameworkElement card, ArtworkMotionPolicy? explicitOwner)
    {
        var policies = new List<ArtworkMotionPolicy>();
        if (explicitOwner != null) policies.Add(explicitOwner);
        for (DependencyObject? node = VisualTreeHelper.GetParent(card); node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer scroll && !policies.Contains(For(scroll))) policies.Add(For(scroll));
        return policies;
    }

    internal static async Task WaitAsync(FrameworkElement card, CancellationToken ct, ArtworkMotionPolicy? owner = null)
    {
        // Re-evaluate all ancestors after a wait: one rail settling must not
        // release artwork while its containing page has started moving.
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var blocked = GetPolicies(card, owner).FirstOrDefault(policy => policy.IsDeferred);
            if (blocked == null) return;
            await blocked.WaitAsync(ct);
        }
    }

    internal static async Task EnterDecodeAsync(FrameworkElement card, SemaphoreSlim decoder, CancellationToken ct,
        ArtworkMotionPolicy? owner = null)
    {
        while (true)
        {
            await WaitAsync(card, ct, owner);
            await decoder.WaitAsync(ct);
            if (!GetPolicies(card, owner).Any(policy => policy.IsDeferred)) return;
            // Never keep another viewport behind a decoder lock while paused.
            decoder.Release();
        }
    }
}
