using SiloPlayer.Core.Models;

namespace SiloPlayer.Views;

// MainWindow owns restore execution; the login page preserves this retry context.
public sealed record LoginNavigationRequest(
    ServerEntry Server,
    bool SessionRestoreUnavailable = false,
    string? SessionRestoreErrorCode = null,
    Func<CancellationToken, Task<bool>>? RetryRestoreAsync = null,
    bool SignedOut = false,
    bool SwitchAccount = false,
    bool LocalLogin = false,
    bool SessionEnded = false);
