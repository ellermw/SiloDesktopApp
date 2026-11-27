namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The Insights refresh protocol (official <c>markAdminInsightsRefresh</c> and <c>withRefreshFlag</c>, c59798116). A user Refresh marks
/// a short window; every Insights read started inside it, on the Insights page or in a dashboard Insights widget, sends
/// <c>refresh=true</c>, which makes the server drop its cached aggregates and read fresh data. The flag belongs to the request, not to
/// a cache key. Title detail never sends it (the server does not cache that read), and the dashboard's own 60-second loop and
/// page-return catch-up never mark it.
/// <para>
/// One gate is shared by everything that reads Insights for one authority, so a Refresh on the page also freshens widgets that are
/// polled in the next seconds. It is safe to call from any thread.
/// </para>
/// </summary>
public sealed class AdminInsightsRefreshGate(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _untilTicks = long.MinValue;

    /// <summary>Marks the next <see cref="AdminInsightsIntervals.RefreshWindow"/> as a user-triggered refresh.</summary>
    public void Mark() => Interlocked.Exchange(ref _untilTicks, _time.GetUtcNow().UtcTicks + AdminInsightsIntervals.RefreshWindow.Ticks);

    /// <summary>True while a marked window is open (inclusive of its last instant, as the official <c>Date.now() &lt;= until</c>).</summary>
    public bool ShouldRefresh => _time.GetUtcNow().UtcTicks <= Interlocked.Read(ref _untilTicks);
}
