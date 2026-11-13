using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Localization;

/// <summary>
/// Chooses and applies the interface locale for the current owner.
///
/// Before a profile's settings load (launch, sign-in, profile picker) it uses
/// the device languages and the last explicit language a profile applied,
/// cached locally. Once the profile's effective <c>ui.app_language</c> loads it
/// is authoritative, null included, and the cache follows it. A server that
/// does not know the setting has no picker, so a cached language (another
/// owner's choice) is dropped there and the device languages apply. A read is
/// bound to the request owner captured when it started: a result for a profile
/// or account that is no longer current, or one overtaken by a newer refresh,
/// is dropped and never applied.
/// </summary>
public sealed class AppLocaleCoordinator
{
    private readonly AppLanguagePreference _preference;
    private readonly LocaleService _locales;
    private readonly SettingsService _localSettings;
    private readonly Func<IReadOnlyList<string>> _deviceLanguages;
    private readonly string _deviceFormatCulture;
    private readonly Func<Action, bool> _post;
    private readonly object _gate = new();
    private long _refreshGeneration;
    private CancellationTokenSource? _refresh;

    /// <param name="post">Queues an apply on the UI thread (natively the window's dispatcher); false when it cannot run.</param>
    public AppLocaleCoordinator(AppLanguagePreference preference, LocaleService locales, SettingsService localSettings,
        Func<IReadOnlyList<string>> deviceLanguages, string deviceFormatCulture, Func<Action, bool> post)
    {
        _preference = preference;
        _locales = locales;
        _localSettings = localSettings;
        _deviceLanguages = deviceLanguages;
        _deviceFormatCulture = deviceFormatCulture;
        _post = post;
    }

    /// <summary>The owner whose setting the current locale reflects; null before a profile's value has loaded.</summary>
    public ApiRequestContext? LoadedOwner { get; private set; }

    /// <summary>Whether the server knows <c>ui.app_language</c> for the loaded owner; the picker hides otherwise.</summary>
    public bool? ServerSupportsSetting { get; private set; }

    /// <summary>Raised on the UI thread after a profile's setting loaded (whether or not the locale changed).</summary>
    public event EventHandler<AppLanguageRead>? Loaded;

    public AppLocaleResolution Resolve(string? storedTag)
        => AppLocaleResolution.Resolve(storedTag, _deviceLanguages(), _deviceFormatCulture);

    /// <summary>Applies the language to use before a profile's setting loads. Call on the UI thread.</summary>
    public AppLocale ApplyBeforeSettings()
    {
        var locale = _locales.Create(Resolve(_localSettings.Load().LastAppLanguageOverride));
        _locales.Apply(locale);
        return _locales.Current;
    }

    /// <summary>
    /// Reads the current owner's effective setting and applies it on the UI
    /// thread. Supersedes any earlier refresh. Completes after the apply was
    /// posted (or the result dropped); failures leave the current locale.
    /// </summary>
    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        CancellationTokenSource scope;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            _refresh?.Cancel();
            _refresh = scope = CancellationTokenSource.CreateLinkedTokenSource(ct);
            token = scope.Token;
            generation = ++_refreshGeneration;
        }
        try
        {
            var context = _preference.CaptureContext();
            if (context.ProfileId == null) return false;
            AppLanguageRead read;
            try
            {
                read = await _preference.ReadEffectiveAsync(context, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException or OperationCanceledException)
            {
                return false;
            }
            if (!IsCurrent(context, generation, token)) return false;
            // An older server has nowhere to store the choice and hides the picker,
            // so a language cached from another server, account or profile must not
            // stay here: drop it and follow the device, like a null value.
            var tag = read.Supported ? read.Tag : null;
            var locale = _locales.Create(Resolve(tag));
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = token.Register(() => completion.TrySetResult(false));
            var posted = _post(() =>
            {
                try
                {
                    if (!IsCurrent(context, generation, token)) { completion.TrySetResult(false); return; }
                    LoadedOwner = context;
                    ServerSupportsSetting = read.Supported;
                    var settings = _localSettings.Load();
                    // Without a cached language an older server changes nothing.
                    if (read.Supported || settings.LastAppLanguageOverride != null)
                    {
                        if (settings.LastAppLanguageOverride != tag)
                        {
                            settings.LastAppLanguageOverride = tag;
                            _localSettings.Save(settings);
                        }
                        _locales.Apply(locale);
                    }
                    Loaded?.Invoke(this, read);
                    completion.TrySetResult(true);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            if (!posted) return false;
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_refresh, scope)) _refresh = null;
                scope.Dispose();
            }
        }
    }

    /// <summary>
    /// Retires the loaded owner (sign-out, profile or account change) and any
    /// refresh in flight. The applied language stays until the next owner's
    /// setting loads, as the cache would choose it.
    /// </summary>
    public void Retire()
    {
        lock (_gate)
        {
            _refresh?.Cancel();
            _refreshGeneration++;
        }
        LoadedOwner = null;
        ServerSupportsSetting = null;
    }

    private bool IsCurrent(ApiRequestContext context, long generation, CancellationToken token)
    {
        lock (_gate)
            return !token.IsCancellationRequested && generation == _refreshGeneration && _preference.IsCurrentContext(context);
    }
}
