namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Declares the native page for one admin route. A domain registers its page
/// only once the whole route (states, dialogs, narrow layout) is finished:
/// <c>services.AddSingleton(new AdminPageRegistration(AdminRoute.Libraries, typeof(AdminLibrariesPage)))</c>.
/// The shell shows a Manage leaf, its topic and its search entry, and opens
/// its route, only when it is registered. <see cref="PageType"/> must be an
/// activatable WinUI <c>Page</c> that accepts an <see cref="AdminRouteRequest"/>
/// parameter; the shell verifies the base type.
/// </summary>
public sealed record AdminPageRegistration(AdminRoute Route, Type PageType);

/// <summary>
/// The admin pages that exist: the shell's built-in Monitor pages plus every
/// registration. Duplicate or unknown routes fail at construction, never by
/// silently showing another page.
/// </summary>
public sealed class AdminPageRegistry
{
    private readonly Dictionary<AdminRoute, Type> _pages = [];

    public AdminPageRegistry(IEnumerable<AdminPageRegistration> builtIn, IEnumerable<AdminPageRegistration>? registrations = null)
    {
        foreach (var registration in builtIn.Concat(registrations ?? []))
        {
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentNullException.ThrowIfNull(registration.PageType);
            if (!Enum.IsDefined(registration.Route))
                throw new ArgumentException($"Unknown admin route {registration.Route}.", nameof(registrations));
            if (!_pages.TryAdd(registration.Route, registration.PageType))
                throw new ArgumentException($"Admin route {registration.Route} is registered more than once.", nameof(registrations));
        }
    }

    public bool IsRegistered(AdminRoute route) => _pages.ContainsKey(route);

    /// <summary>The page type of a registered route; null when the route has no native page.</summary>
    public Type? PageTypeOf(AdminRoute route) => _pages.TryGetValue(route, out var type) ? type : null;

    /// <summary>Whether a location can be opened: its route is registered and its descendant path is valid.</summary>
    public bool CanOpen(AdminRouteRequest? request) => request != null && IsRegistered(request.Route) && AdminRouteCatalog.IsValid(request);

    /// <summary>Whether a sidebar topic has at least one page to show.</summary>
    public bool HasAny(AdminNavTopic topic) => topic.Leaves.Any(leaf => IsRegistered(leaf.Route));

    public IReadOnlyCollection<AdminRoute> Routes => _pages.Keys;
}

/// <summary>
/// A sidebar badge provider for one route (Requests › Queue pending count,
/// a settings page's attention dot). The owning domain reads its data under
/// its own authority lease, clears <see cref="Current"/> when that authority
/// retires, and raises <see cref="Changed"/> on any thread; the shell
/// marshals, renders the badge on the leaf, and on a closed topic shows the
/// first child badge. A source for an unregistered route is never shown.
/// </summary>
public interface IAdminNavBadgeSource
{
    AdminRoute Route { get; }
    AdminNavBadge? Current { get; }
    event Action? Changed;
}
