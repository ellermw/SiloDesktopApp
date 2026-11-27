using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>Measures synchronous Frame work and publishes the attempt's start time only if it commits.</summary>
internal sealed class UiStallNavigation
{
    private long? _startedAt;

    public Attempt Begin(string operation, UiStallLog.Location source, UiStallLog.Location target)
    {
        var log = UiStallLog.Shared;
        var previous = _startedAt;
        _startedAt = log.Now;
        return new(this, previous, log.Measure(operation, source, target));
    }

    public void Committed(NavigationMode mode, string? destination) => UiStallLog.Shared.NoteNavigation(mode switch
    {
        NavigationMode.Back => "back",
        NavigationMode.Forward => "forward",
        NavigationMode.Refresh => "refresh",
        _ => "navigate",
    }, destination, _startedAt);

    public readonly struct Attempt(UiStallNavigation navigation, long? previous, UiStallLog.Operation operation) : IDisposable
    {
        public void Dispose()
        {
            navigation._startedAt = previous;
            operation.Dispose();
        }
    }
}
