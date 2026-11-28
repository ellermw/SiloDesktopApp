using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>Measures synchronous Frame work and publishes the attempt's start time only if it commits.</summary>
internal sealed class UiStallNavigation
{
    private (NavigationMode Mode, long StartedAt, bool PublishAction)? _attempt;

    public Attempt Begin(NavigationMode mode, UiStallLog.Location source, UiStallLog.Location target, bool publishAction = true)
    {
        var log = UiStallLog.Shared;
        var previous = _attempt;
        _attempt = (mode, log.Now, publishAction);
        var operation = mode == NavigationMode.New ? "navigation" : "navigation-" + ActionFor(mode);
        return new(this, previous, log.Measure(operation, source, target));
    }

    public void Committed(NavigationMode mode, string? destination)
    {
        if (_attempt?.PublishAction == false) return;
        UiStallLog.Shared.NoteNavigation(ActionFor(_attempt?.Mode ?? mode), destination, _attempt?.StartedAt);
    }

    private static string ActionFor(NavigationMode mode) => mode switch
    {
        NavigationMode.Back => "back",
        NavigationMode.Forward => "forward",
        NavigationMode.Refresh => "refresh",
        _ => "navigate",
    };

    public readonly struct Attempt(UiStallNavigation navigation, (NavigationMode Mode, long StartedAt, bool PublishAction)? previous, UiStallLog.Operation operation) : IDisposable
    {
        public void Dispose()
        {
            navigation._attempt = previous;
            operation.Dispose();
        }
    }
}
