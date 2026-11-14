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
    internal static async Task ContinueWatchingDuplicateAsync(StackPanel parent)
    {
        HomeSectionWithItems Episodes(bool refreshed) => new()
        {
            Id = "continue", SectionType = "continue_watching", Title = "Continue Watching", LoadCompleted = true,
            Items = new((refreshed ? new[] { "goldbergs", "nocturne", "other" } : new[] { "nocturne", "goldbergs", "other" })
                .Select(id => new MediaItem { ContentId = id, Type = "episode", Title = id + " episode",
                    SeriesId = id + "-series", SeriesTitle = id, SeasonNumber = 6, EpisodeNumber = 20,
                    PositionSeconds = refreshed && id == "goldbergs" ? 1500 : 900, DurationSeconds = 1800 })),
        };
        var mounted = Episodes(false);
        var row = await MountAsync(parent, mounted);
        try
        {
            for (var turn = 0; turn < 12; turn++)
            {
                Apply(mounted, Episodes(turn % 2 == 0));
                await RequireCardsMatchAsync(row, mounted, "continue-watching-reorder");
                for (var index = 0; index < mounted.Items.Count; index++)
                {
                    if (Repeater(row).TryGetElement(index) is not LandscapeCard card)
                        throw new InvalidOperationException($"Continue Watching card {index} was not realized.");
                    if (((TextBlock)card.FindName("TitleText")).Text != mounted.Items[index].SeriesTitle)
                        throw new InvalidOperationException($"Continue Watching card {index} displays a stale title.");
                }
            }
            // An accepted insertion before the Move must also join the final
            // snapshot rather than leaving the guard one delta behind.
            var inserted = Episodes(true);
            inserted.Items.Insert(0, new MediaItem { ContentId = "inserted", Title = "Inserted", Type = "episode" });
            Apply(mounted, inserted);
            await RequireCardsMatchAsync(row, mounted, "continue-watching-insert-then-move");

            // Make a real scrollable carousel, then reorder while away from
            // its start. A data refresh must not jump back to the first card.
            var longRow = Episodes(false);
            foreach (var id in Enumerable.Range(0, 20))
                longRow.Items.Add(new MediaItem { ContentId = $"tail-{id}", Title = $"Tail {id}", Type = "episode" });
            Apply(mounted, longRow);
            await RequireCardsMatchAsync(row, mounted, "continue-watching-long-row");
            var scroll = (ScrollViewer)row.FindName("CardsScrollViewer");
            scroll.ChangeView(500, null, null, disableAnimation: true);
            await Task.Delay(150);
            var offset = scroll.HorizontalOffset;
            if (offset < 100) throw new InvalidOperationException("Continue Watching scroll test did not move the viewport.");
            var reordered = Episodes(true);
            foreach (var item in longRow.Items.Skip(3)) reordered.Items.Add(item);
            Apply(mounted, reordered);
            await RequireCardsMatchAsync(row, mounted, "continue-watching-scrolled-reorder");
            if (Math.Abs(scroll.HorizontalOffset - offset) > 1)
                throw new InvalidOperationException("Continue Watching reorder reset the horizontal scroll position.");
            Program.Log("PASS: Continue Watching reorders a changed episode without duplicating its card or title.");
            Program.Log("PASS: accepted insertion followed by reorder converges, and a scrolled carousel retains its offset.");
        }
        finally { parent.Children.Remove(row); }
    }

    /// <param name="strict">
    /// Requires keyboard focus and an actual mid-layout rejection. The default
    /// suite runs unactivated and against whatever WinUI build is installed,
    /// so it logs those prerequisites as skipped instead of failing.
    /// </param>
    internal static async Task RunAsync(StackPanel parent, bool strict = false)
    {
        await ContinueWatchingDuplicateAsync(parent);
        await MountedSequenceAsync(parent);
        await BackToBackAsync(parent);
        await DetachedRowAsync(parent);
        await CollapsedRowAsync(parent);
        await RebuiltRowAsync(parent);
        await TemplateSwapAsync(parent);
        await FocusedCardAsync(parent);
        Program.Log("PASS: Home rows apply refreshed responses in every mounted state without ItemsRepeater rejection.");
        var exercised = 0;
        if (await DuringLayoutAsync(parent, strict, reloaded: false)) exercised++;
        // A reloaded row re-subscribes to its items after the guard did.
        if (await DuringLayoutAsync(parent, strict, reloaded: true)) exercised++;
        // A response that shrinks a full row leaves realized indices past the new snapshot.
        if (await DuringLayoutAsync(parent, strict, reloaded: false, shrink: true)) exercised++;
        // Only report the rejection group as passed when it actually ran; a
        // run where every scenario skipped must not read as a verified fix.
        if (exercised > 0)
            Program.Log($"PASS: a Home response applied during the row's layout pass completes and the row resyncs ({exercised} of 3 scenarios reached the rejection).");
        else
            Program.Log("SKIPPED: home-reconcile mid-layout rejection not exercised in this run; run with SILO_NATIVE_TEST_ONLY=home-reconcile (run.ps1 -Only home-reconcile) to verify it.");
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
        // Reattachment is completed by WinUI's Loaded event, not a dispatcher
        // drain. Wait for the real lifecycle before judging its resumed guard.
        if (!row.IsLoaded)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
            row.Loaded += Loaded;
            try { if (!row.IsLoaded) await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { row.Loaded -= Loaded; }
        }
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
        // The retired row has unloaded, so its guard must have released the
        // shared model: its snapshot stays where the row left it.
        var retired = Guard(first);
        var retiredSnapshot = retired.Cast<MediaItem>().ToArray();
        try
        {
            Apply(mounted, Movies(new[] { 4, 2, 0, 50 }, version: 1));
            await DrainAsync();
            Apply(mounted, Movies(Enumerable.Range(0, 25)));
            await RequireCardsMatchAsync(second, mounted, "rebuilt-row");
            if (retired.RejectedChanges != 0)
                throw new InvalidOperationException("rebuilt-row: the retired row's repeater rejected a change.");
            if (!retiredSnapshot.SequenceEqual(retired.Cast<MediaItem>()))
                throw new InvalidOperationException("rebuilt-row: the retired row's guard still observes the model.");
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

    /// <returns>Whether the scenario reached ItemsRepeater's rejection and verified the resync.</returns>
    private static async Task<bool> DuringLayoutAsync(StackPanel parent, bool strict, bool reloaded, bool shrink = false)
    {
        // The field failure: a Home response reaches the row while its
        // ItemsRepeater is measuring. ElementPrepared runs inside that pass.
        var scenario = (reloaded ? "during-layout-reloaded" : "during-layout") + (shrink ? "-shrink" : "");
        var mounted = Movies(Enumerable.Range(0, shrink ? 20 : 3));
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
        var response = shrink
            // Start with Replace, not Move: Move intentionally defers without
            // reaching WinUI. This still exercises rejection followed by shrink
            // and preserves m2 for the focus-restoration assertion.
            ? Movies(new[] { 99, 0, 1, 2 }, versionOf: id => id == 0 ? 1 : 0)
            : Movies(new[] { 9, 2, 0, 7, 8, 1, 6 }, versionOf: id => id == 0 ? 1 : 0);
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
            // The strict group activates its window, so there a host that
            // cannot take focus is a failure; the unactivated default suite
            // skips the focus check instead.
            var focusedItem = FocusCard(repeater, 2, scenario);
            if (focusedItem == null)
            {
                if (strict)
                    throw new InvalidOperationException($"{scenario}: the host could not take keyboard focus, so focus restore cannot be checked.");
                Program.Log($"SKIP: home-reconcile {scenario}: the host could not take keyboard focus; focus restore not checked.");
            }
            repeater.ElementPrepared += Prepared;
            // Growing the row, or inserting at its front, realizes a new card,
            // which raises ElementPrepared mid-measure.
            Apply(mounted, shrink ? Movies(new[] { 99 }.Concat(Enumerable.Range(0, 20))) : Movies(Enumerable.Range(0, 8)));
            row.UpdateLayout();
            repeater.ElementPrepared -= Prepared;
            if (!applied)
            {
                if (strict)
                    throw new InvalidOperationException($"{scenario}: the response was never applied inside the repeater's layout pass.");
                // This WinUI build did not realize a card mid-layout. Apply the
                // response normally and require only that the row converges.
                Program.Log($"SKIP: home-reconcile {scenario}: ElementPrepared did not run inside the layout pass; rejection path not reached.");
                Apply(mounted, response);
                await RequireCardsMatchAsync(row, mounted, scenario, allowRejection: true);
                return false;
            }
            if (thrown != null)
                throw new InvalidOperationException($"{scenario}: applying the response threw {thrown.GetType().Name} 0x{thrown.HResult:X8}.", thrown);
            var guard = Guard(row);
            if (guard.RejectedChanges == 0)
            {
                if (strict)
                    throw new InvalidOperationException($"{scenario}: ItemsRepeater accepted a mid-layout change; the fixture no longer reaches the rejection.");
                // The product behaviour is still correct when WinUI accepts the
                // change; only the resync path goes unexercised.
                Program.Log($"SKIP: home-reconcile {scenario}: ItemsRepeater accepted the mid-layout change; rejection path not reached.");
                if (!mounted.Items.Select(Key).SequenceEqual(response.Items.Select(Key)))
                    throw new InvalidOperationException($"{scenario}: the accepted update did not finish applying the response.");
                await RequireCardsMatchAsync(row, mounted, scenario);
                return false;
            }
            if (!mounted.Items.Select(Key).SequenceEqual(response.Items.Select(Key)))
                throw new InvalidOperationException($"{scenario}: the rejected update did not finish applying the response.");
            // The recovery Reset is still queued. Whatever Count WinUI cached,
            // every index the repeater can ask for must resolve through the guard.
            if (repeater.ItemsSourceView.Count != guard.Count)
                Program.Log($"home-reconcile {scenario}: repeater count {repeater.ItemsSourceView.Count} differs from the guard snapshot {guard.Count} after the rejection.");
            for (var index = 0; index < repeater.ItemsSourceView.Count; index++)
                if (repeater.ItemsSourceView.GetAt(index) is not MediaItem)
                    throw new InvalidOperationException($"{scenario}: repeater index {index} of {repeater.ItemsSourceView.Count} did not resolve through the guard ({guard.Count}) after the rejection.");
            // A model change in this window must not move the snapshot under
            // the stale repeater; the Reset delivers it instead.
            var held = guard.Cast<MediaItem>().ToArray();
            mounted.Items.RemoveAt(mounted.Items.Count - 1);
            if (!held.SequenceEqual(guard.Cast<MediaItem>()))
                throw new InvalidOperationException($"{scenario}: a change while the resync was pending moved the guard snapshot.");
            await RequireCardsMatchAsync(row, mounted, scenario, allowRejection: true);
            if (!mounted.Items.SequenceEqual(guard.Cast<MediaItem>()))
                throw new InvalidOperationException($"{scenario}: the recovery Reset did not publish the model as it is now.");
            if (repeater.ItemsSourceView.Count != guard.Count)
                throw new InvalidOperationException($"{scenario}: repeater count {repeater.ItemsSourceView.Count} still differs from the guard {guard.Count} after the recovery Reset.");
            if (focusedItem != null && !ReferenceEquals(FocusedCardItem(repeater), focusedItem))
                throw new InvalidOperationException($"{scenario}: keyboard focus did not return to {Key(focusedItem)} after the recovery Reset.");
            Program.Log($"home-reconcile {scenario}: {Guard(row).RejectedChanges} rejected change(s) resynced.");
            return true;
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
