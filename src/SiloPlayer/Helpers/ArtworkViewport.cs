using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

/// <summary>Motion belongs to the real scrolling surface, never to a global card list.</summary>
internal static class ArtworkViewport
{
    private static readonly ConditionalWeakTable<ScrollViewer, ArtworkMotionPolicy> Policies = new();

    internal static ArtworkMotionPolicy For(ScrollViewer scroll) => Policies.GetValue(scroll, owner =>
    {
        var policy = new ArtworkMotionPolicy();
        policy.Reset(owner.HorizontalOffset, owner.VerticalOffset);
        owner.ViewChanged += (_, _) => policy.Observe(owner.HorizontalOffset, owner.VerticalOffset);
        owner.Loaded += (_, _) => policy.Reset(owner.HorizontalOffset, owner.VerticalOffset);
        owner.Unloaded += (_, _) => { if (!owner.IsLoaded) policy.Reset(owner.HorizontalOffset, owner.VerticalOffset); };
        return policy;
    });

    internal static void ObserveAncestors(FrameworkElement card) => GetPolicies(card, null);

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
