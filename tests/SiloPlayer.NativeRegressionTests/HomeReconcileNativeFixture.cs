using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

/// <summary>
/// Applies refreshed Home responses to a mounted SectionRow through the Home
/// reconciler in each row state the app reaches. ItemsRepeater rejects a
/// change that reaches it during its own layout pass with E_FAIL
/// (COMException 0x80004005, "Unspecified error" in home_error.txt). That
/// rejection must not unwind into the reconciler, and the row must converge
/// on its data afterwards.
/// </summary>
internal static class HomeReconcileNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        await MountedSequenceAsync(parent);
        await BackToBackAsync(parent);
        await DetachedRowAsync(parent);
        await CollapsedRowAsync(parent);
        await RebuiltRowAsync(parent);
        await TemplateSwapAsync(parent);
        await FocusedCardAsync(parent);
        Program.Log("PASS: Home rows apply refreshed responses in every mounted state without ItemsRepeater rejection.");
        await DuringLayoutAsync(parent, reloaded: false);
        // A reloaded row re-subscribes to its items after the guard did.
        await DuringLayoutAsync(parent, reloaded: true);
        Program.Log("PASS: a Home response applied during the row's layout pass completes and the row resyncs.");
        // Let removed cards run their Unloaded cleanup while app services exist.
        for (var turn = 0; turn < 6; turn++) { await Task.Delay(50); await DrainAsync(); }
    }

    private static HomeSectionWithItems Section(string type, IEnumerable<(string Id, int Version, string Kind)> items) => new()
    {
        Id = "reconcile-row", SectionType = type, Title = "Reconcile", LoadCompleted = true, ItemLimit = 40,
        Items = new(items.Select(item => new MediaItem
        {
            ContentId = item.Id, Type = item.Kind, Title = $"{item.Id} v{item.Version}",
            DurationSeconds = 1800, PositionSeconds = 60,
        })),
    };

    private static HomeSectionWithItems Movies(IEnumerable<int> ids, int version = 0, Func<int, int>? versionOf = null) =>
        Section("recently_added", ids.Select(id => ($"m{id}", versionOf?.Invoke(id) ?? version, "movie")));

    private static async Task<SectionRow> MountAsync(StackPanel parent, HomeSectionWithItems section)
    {
        var row = new SectionRow { Section = section, Width = 1200 };
        parent.Children.Add(row);
        row.UpdateLayout();
        await Task.Delay(150);
        row.UpdateLayout();
        return row;
    }

    private static ItemsRepeater Repeater(SectionRow row) => (ItemsRepeater)row.FindName("CardsRepeater");

    private static RepeaterItemsSource<MediaItem> Guard(SectionRow row) =>
        Repeater(row).ItemsSource as RepeaterItemsSource<MediaItem>
        ?? throw new InvalidOperationException("SectionRow must bind its repeater through RepeaterItemsSource.");

    private static void Apply(HomeSectionWithItems mounted, HomeSectionWithItems incoming)
    {
        var expected = incoming.Items.Select(Key).ToArray();
        HomeSectionReconciler.Apply(mounted, incoming);
        if (!expected.SequenceEqual(mounted.Items.Select(Key)))
            throw new InvalidOperationException("Reconciled row does not match its response.");
    }

    private static string Key(MediaItem item) => item.ContentId + "|" + item.Title;

    /// <summary>Every realized card shows the item at its index, after layout settles.</summary>
    private static async Task RequireCardsMatchAsync(SectionRow row, HomeSectionWithItems mounted, string scenario, bool allowRejection = false)
    {
        await DrainAsync();
        row.UpdateLayout();
        await DrainAsync();
        row.UpdateLayout();
        var repeater = Repeater(row);
        var guard = Guard(row);
        if (!allowRejection && guard.RejectedChanges != 0)
            throw new InvalidOperationException($"{scenario}: ItemsRepeater rejected {guard.RejectedChanges} change(s).");
        if (!ReferenceEquals(guard.Source, mounted.Items))
            throw new InvalidOperationException($"{scenario}: repeater is not bound to the mounted row.");
        var realized = 0;
        for (var index = 0; index < mounted.Items.Count; index++)
        {
            var shown = repeater.TryGetElement(index) switch
            {
                PosterCard poster => poster.MediaItem,
                LandscapeCard landscape => landscape.MediaItem,
                null => null,
                var other => throw new InvalidOperationException($"{scenario}: unexpected card {other.GetType().Name}."),
            };
            if (shown == null) continue;
            realized++;
            if (!ReferenceEquals(shown, mounted.Items[index]))
                throw new InvalidOperationException($"{scenario}: card {index} shows {Key(shown)}, data has {Key(mounted.Items[index])}.");
        }
        if (mounted.Items.Count > 0 && realized == 0)
            throw new InvalidOperationException($"{scenario}: no cards were realized.");
    }

    private static async Task MountedSequenceAsync(StackPanel parent)
    {
        var mounted = Movies(Enumerable.Range(0, 20));
        var row = await MountAsync(parent, mounted);
        try
        {
            var random = new Random(117);
            for (var step = 0; step < 40; step++)
            {
                var ids = Enumerable.Range(0, 30).OrderBy(_ => random.Next()).Take(random.Next(5, 25)).ToArray();
                Apply(mounted, Movies(ids, versionOf: _ => random.Next(3)));
                await DrainAsync();
                row.UpdateLayout();
            }
            await RequireCardsMatchAsync(row, mounted, "mounted-sequence");
        }
        finally { parent.Children.Remove(row); }
    }

    private static async Task BackToBackAsync(StackPanel parent)
    {
        var mounted = Movies(Enumerable.Range(0, 20));
        var row = await MountAsync(parent, mounted);
        try
        {
            var random = new Random(118);
            for (var step = 0; step < 40; step++)
                Apply(mounted, Movies(Enumerable.Range(0, 30).OrderBy(_ => random.Next()).Take(random.Next(1, 25)), versionOf: _ => random.Next(3)));
            await RequireCardsMatchAsync(row, mounted, "back-to-back");
        }
        finally { parent.Children.Remove(row); }
    }

    private static async Task DetachedRowAsync(StackPanel parent)
    {
        var mounted = Movies(Enumerable.Range(0, 20));
        var row = await MountAsync(parent, mounted);
        try
        {
            parent.Children.Remove(row);
            await DrainAsync();
            Apply(mounted, Movies(new[] { 5, 1, 0, 30, 31 }, version: 1));
            Apply(mounted, Movies(Enumerable.Range(0, 20)));
            parent.Children.Add(row);
            Apply(mounted, Movies(Enumerable.Range(3, 10), version: 2));
            await RequireCardsMatchAsync(row, mounted, "detached-row");
        }
        finally { parent.Children.Remove(row); }
    }

    private static async Task CollapsedRowAsync(StackPanel parent)
    {
        var mounted = Movies(Enumerable.Range(0, 20));
        var row = await MountAsync(parent, mounted);
        try
        {
            row.Visibility = Visibility.Collapsed;
            row.UpdateLayout();
            Apply(mounted, Movies(new[] { 7, 3, 40 }, version: 1));
            row.Visibility = Visibility.Visible;
            Apply(mounted, Movies(Enumerable.Range(0, 20), version: 2));
            await RequireCardsMatchAsync(row, mounted, "collapsed-row");
        }
        finally { parent.Children.Remove(row); }
    }

    private static async Task RebuiltRowAsync(StackPanel parent)
    {
        var mounted = Movies(Enumerable.Range(0, 20));
        var first = await MountAsync(parent, mounted);
        parent.Children.Remove(first);
        var second = await MountAsync(parent, mounted);
        try
        {
            Apply(mounted, Movies(new[] { 4, 2, 0, 50 }, version: 1));
            await DrainAsync();
            Apply(mounted, Movies(Enumerable.Range(0, 25)));
            await RequireCardsMatchAsync(second, mounted, "rebuilt-row");
            if (Guard(first).RejectedChanges != 0)
                throw new InvalidOperationException("rebuilt-row: the retired row's repeater rejected a change.");
        }
        finally { parent.Children.Remove(second); }
    }

    private static async Task TemplateSwapAsync(StackPanel parent)
    {
        HomeSectionWithItems Continue(bool mixed, int version) => Section("continue_watching",
            Enumerable.Range(0, 12).Select(i => ($"c{i}", version, mixed && i % 3 == 0 ? "episode" : "movie")));
        var mounted = Continue(mixed: false, version: 0);
        var row = await MountAsync(parent, mounted);
        try
        {
            for (var step = 1; step <= 6; step++)
            {
                Apply(mounted, Continue(mixed: step % 2 == 1, version: step));
                // SectionRow's queued UpdateSection swaps the template; the
                // next response lands before the following layout pass.
                await DrainAsync();
                Apply(mounted, Continue(mixed: step % 2 == 1, version: step + 100));
                row.UpdateLayout();
            }
            await RequireCardsMatchAsync(row, mounted, "template-swap");
        }
        finally { parent.Children.Remove(row); }
    }

    private static async Task FocusedCardAsync(StackPanel parent)
    {
        var mounted = Movies(Enumerable.Range(0, 20));
        var row = await MountAsync(parent, mounted);
        try
        {
            var repeater = Repeater(row);
            for (var step = 0; step < 3; step++)
            {
                if (repeater.TryGetElement(0) is UIElement card && FindFocusable(card) is Control focusable)
                    focusable.Focus(FocusState.Keyboard);
                await DrainAsync();
                // Drop the focused first card and reverse the rest.
                Apply(mounted, Movies(mounted.Items.Skip(1).Select(item => int.Parse(item.ContentId[1..])).Reverse(), version: step + 1));
                await DrainAsync();
                row.UpdateLayout();
            }
            await RequireCardsMatchAsync(row, mounted, "focused-card");
        }
        finally { parent.Children.Remove(row); }
    }

    private static async Task DuringLayoutAsync(StackPanel parent, bool reloaded)
    {
        // The field failure: a Home response reaches the row while its
        // ItemsRepeater is measuring. ElementPrepared runs inside that pass.
        var scenario = reloaded ? "during-layout-reloaded" : "during-layout";
        var mounted = Movies(Enumerable.Range(0, 3));
        var row = await MountAsync(parent, mounted);
        if (reloaded)
        {
            // A cached Home page's rows unload and reload on navigation.
            parent.Children.Remove(row);
            await DrainAsync();
            parent.Children.Add(row);
            row.UpdateLayout();
            await DrainAsync();
            row.UpdateLayout();
        }
        var repeater = Repeater(row);
        var response = Movies(new[] { 9, 2, 0, 7, 8, 1, 6 }, versionOf: id => id == 0 ? 1 : 0);
        var applied = false;
        Exception? thrown = null;
        void Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (applied) return;
            applied = true;
            try { Apply(mounted, response); }
            catch (Exception ex) { thrown = ex; }
        }
        try
        {
            // m2 survives the response as the same object; the recovery Reset
            // recycles its card and must hand keyboard focus back to it.
            var focusedItem = FocusCard(repeater, 2, scenario);
            repeater.ElementPrepared += Prepared;
            // Growing the row realizes a new card, which raises ElementPrepared mid-measure.
            Apply(mounted, Movies(Enumerable.Range(0, 8)));
            row.UpdateLayout();
            repeater.ElementPrepared -= Prepared;
            if (!applied)
                throw new InvalidOperationException($"{scenario}: the response was never applied inside the repeater's layout pass.");
            if (thrown != null)
                throw new InvalidOperationException($"{scenario}: applying the response threw {thrown.GetType().Name} 0x{thrown.HResult:X8}.", thrown);
            var guard = Guard(row);
            if (guard.RejectedChanges == 0)
                throw new InvalidOperationException($"{scenario}: ItemsRepeater accepted a mid-layout change; the fixture no longer reaches the rejection.");
            if (!mounted.Items.Select(Key).SequenceEqual(response.Items.Select(Key)))
                throw new InvalidOperationException($"{scenario}: the rejected update did not finish applying the response.");
            // The recovery Reset is still queued. Whether WinUI's cached count
            // matches the guard depends on its internal ordering, so record it
            // rather than gate on it; the Reset's outcome is asserted below.
            if (repeater.ItemsSourceView.Count != guard.Count)
                Program.Log($"home-reconcile {scenario}: repeater count {repeater.ItemsSourceView.Count} differs from the guard snapshot {guard.Count} after the rejection.");
            // A model change in this window must not move the snapshot under
            // the stale repeater; the Reset delivers it instead.
            var held = guard.Cast<MediaItem>().ToArray();
            mounted.Items.RemoveAt(mounted.Items.Count - 1);
            if (!held.SequenceEqual(guard.Cast<MediaItem>()))
                throw new InvalidOperationException($"{scenario}: a change while the resync was pending moved the guard snapshot.");
            await RequireCardsMatchAsync(row, mounted, scenario, allowRejection: true);
            if (!mounted.Items.SequenceEqual(guard.Cast<MediaItem>()))
                throw new InvalidOperationException($"{scenario}: the recovery Reset did not publish the model as it is now.");
            if (focusedItem == null)
                Program.Log($"home-reconcile {scenario}: host could not take keyboard focus; focus restore not checked.");
            else if (!ReferenceEquals(FocusedCardItem(repeater), focusedItem))
                throw new InvalidOperationException($"{scenario}: keyboard focus did not return to {Key(focusedItem)} after the recovery Reset.");
            Program.Log($"home-reconcile {scenario}: {Guard(row).RejectedChanges} rejected change(s) resynced.");
        }
        finally { repeater.ElementPrepared -= Prepared; parent.Children.Remove(row); }
    }

    /// <summary>Keyboard-focuses the card at <paramref name="index"/>; null when the host cannot take focus.</summary>
    private static MediaItem? FocusCard(ItemsRepeater repeater, int index, string scenario)
    {
        if (repeater.TryGetElement(index) is not UIElement card || FindFocusable(card) is not Control focusable) return null;
        if (!focusable.Focus(FocusState.Keyboard) || !IsAncestor(card, FocusedElement(repeater))) return null;
        // The card holds focus now, so a miss is a broken traversal, not the host.
        return FocusedCardItem(repeater)
            ?? throw new InvalidOperationException($"{scenario}: card {index} holds keyboard focus but the focus traversal found no card item.");
    }

    private static DependencyObject? FocusedElement(ItemsRepeater repeater) =>
        repeater.XamlRoot == null ? null : Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(repeater.XamlRoot) as DependencyObject;

    private static bool IsAncestor(DependencyObject ancestor, DependencyObject? node)
    {
        for (; node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }

    /// <summary>The nearest card around the focused element, when the repeater is its ancestor.</summary>
    private static MediaItem? FocusedCardItem(ItemsRepeater repeater)
    {
        MediaItem? item = null;
        for (var node = FocusedElement(repeater); node != null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, repeater)) return item;
            item ??= node switch
            {
                PosterCard poster => poster.MediaItem,
                LandscapeCard landscape => landscape.MediaItem,
                _ => null,
            };
        }
        return null;
    }

    private static Control? FindFocusable(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Control { IsTabStop: true } control) return control;
            if (FindFocusable(child) is { } nested) return nested;
        }
        return null;
    }

    private static Task DrainAsync()
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low, () => drained.SetResult());
        return drained.Task;
    }
}
