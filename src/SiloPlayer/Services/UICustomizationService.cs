using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Settings;

namespace SiloPlayer.Services;

/// <summary>
/// Resolves Silo's revision-5 navigation/card contract for the native desktop
/// family. The service fails closed on older servers and keeps the server value
/// authoritative; callers only use native defaults when the contract says the
/// effective value is null.
/// </summary>
public sealed class UICustomizationService(SettingsApi settingsApi)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _loaded;

    public bool IsSupported { get; private set; }
    public bool IsUnavailable { get; private set; }
    public CardPresentation CardPresentation { get; private set; } = CardPresentation.Default;
    public string CardPresentationSource { get; private set; } = "default";
    public PrimaryMenuDocument? PrimaryMenu { get; private set; }
    public string PrimaryMenuSource { get; private set; } = "default";
    public NavigationShortcutDocument Shortcuts { get; private set; } = new();

    public event EventHandler? Changed;

    public void Invalidate() => _loaded = false;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded) return;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SettingsContractCapabilities capability;
            try
            {
                capability = await settingsApi.GetContractCapabilitiesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ApiException ex) when (ex.StatusCode is 404 or 405)
            {
                Reset(supported: false, unavailable: false);
                return;
            }

            if (!capability.SupportsRevision5Customization)
            {
                Reset(supported: false, unavailable: false);
                return;
            }

            var response = await settingsApi.GetContractEffectiveSettingsAsync(
                    [
                        UICustomizationSettingKeys.PrimaryMenu,
                        UICustomizationSettingKeys.Shortcuts,
                        UICustomizationSettingKeys.CardPresentation,
                    ],
                    ct: cancellationToken)
                .ConfigureAwait(false);
            var values = response.Settings.ToDictionary(entry => entry.Key, StringComparer.Ordinal);

            CardPresentation = values.TryGetValue(UICustomizationSettingKeys.CardPresentation, out var cards)
                ? ParseCardPresentation(cards.Value)
                : CardPresentation.Default;
            CardPresentationSource = cards?.Source ?? "default";

            PrimaryMenu = values.TryGetValue(UICustomizationSettingKeys.PrimaryMenu, out var menu)
                ? ParsePrimaryMenu(menu.Value)
                : null;
            PrimaryMenuSource = menu?.Source ?? "default";

            Shortcuts = values.TryGetValue(UICustomizationSettingKeys.Shortcuts, out var shortcuts)
                ? ParseShortcuts(shortcuts.Value)
                : new NavigationShortcutDocument();

            IsSupported = true;
            IsUnavailable = false;
            _loaded = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Reset(supported: false, unavailable: true);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public async Task SaveCardPresentationAsync(
        CardPresentation presentation,
        CancellationToken cancellationToken = default)
    {
        var normalized = presentation.Normalize();
        await settingsApi.SetContractSettingValueAsync(
            UICustomizationSettingKeys.CardPresentation,
            "profile_client",
            normalized,
            ct: cancellationToken).ConfigureAwait(false);
        CardPresentation = normalized;
        CardPresentationSource = "profile_client";
        _loaded = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ResetCardPresentationAsync(CancellationToken cancellationToken = default)
    {
        await settingsApi.DeleteContractSettingValueAsync(
            UICustomizationSettingKeys.CardPresentation,
            "profile_client",
            ct: cancellationToken).ConfigureAwait(false);
        _loaded = false;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearDeviceOverrideAsync(string key, CancellationToken cancellationToken = default)
    {
        if (key is not (UICustomizationSettingKeys.CardPresentation or UICustomizationSettingKeys.PrimaryMenu))
            throw new ArgumentOutOfRangeException(nameof(key));
        await settingsApi.DeleteContractSettingValueAsync(
            key,
            "profile_device",
            ct: cancellationToken).ConfigureAwait(false);
        _loaded = false;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SavePrimaryMenuAsync(
        IEnumerable<PrimaryMenuItem> items,
        CancellationToken cancellationToken = default)
    {
        var validated = ValidatePrimaryMenu(items);
        var document = new PrimaryMenuDocument { Items = validated };
        await settingsApi.SetContractSettingValueAsync(
            UICustomizationSettingKeys.PrimaryMenu,
            "profile_client",
            document,
            ct: cancellationToken).ConfigureAwait(false);
        PrimaryMenu = document;
        PrimaryMenuSource = "profile_client";
        _loaded = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ResetPrimaryMenuAsync(CancellationToken cancellationToken = default)
    {
        await settingsApi.DeleteContractSettingValueAsync(
            UICustomizationSettingKeys.PrimaryMenu,
            "profile_client",
            ct: cancellationToken).ConfigureAwait(false);
        _loaded = false;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public static List<PrimaryMenuItem> DefaultDesktopMenu() =>
    [
        PrimaryMenuItem.Builtin("home"),
        PrimaryMenuItem.Builtin("for_you"),
        PrimaryMenuItem.Builtin("calendar"),
    ];

    public int GetPosterColumnCount(double contentWidth) => CardPresentation.PosterSize switch
    {
        "compact" => contentWidth >= 1280 ? 10 : contentWidth >= 1024 ? 8 : contentWidth >= 768 ? 6 : contentWidth >= 640 ? 5 : 3,
        "large" => contentWidth >= 1280 ? 6 : contentWidth >= 1024 ? 5 : contentWidth >= 768 ? 4 : contentWidth >= 640 ? 3 : 2,
        _ => contentWidth >= 1280 ? 8 : contentWidth >= 1024 ? 7 : contentWidth >= 768 ? 5 : contentWidth >= 640 ? 4 : 3,
    };

    public double CardCaptionHeight => CardPresentation.Caption switch
    {
        "artwork" => 0,
        "title" => 28,
        _ => 44,
    };

    private void Reset(bool supported, bool unavailable)
    {
        IsSupported = supported;
        IsUnavailable = unavailable;
        CardPresentation = CardPresentation.Default;
        CardPresentationSource = "default";
        PrimaryMenu = null;
        PrimaryMenuSource = "default";
        Shortcuts = new NavigationShortcutDocument();
        _loaded = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static CardPresentation ParseCardPresentation(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return CardPresentation.Default;
        try
        {
            return (JsonSerializer.Deserialize<CardPresentation>(value.GetRawText(), JsonOptions)
                    ?? CardPresentation.Default)
                .Normalize();
        }
        catch
        {
            return CardPresentation.Default;
        }
    }

    private static PrimaryMenuDocument? ParsePrimaryMenu(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        try
        {
            var document = JsonSerializer.Deserialize<PrimaryMenuDocument>(value.GetRawText(), JsonOptions);
            return document is null ? null : new PrimaryMenuDocument
            {
                Items = ValidatePrimaryMenu(document.Items),
            };
        }
        catch
        {
            return null;
        }
    }

    private static NavigationShortcutDocument ParseShortcuts(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return new NavigationShortcutDocument();
        try
        {
            var document = JsonSerializer.Deserialize<NavigationShortcutDocument>(value.GetRawText(), JsonOptions)
                ?? new NavigationShortcutDocument();
            document.Items = document.Items
                .Where(IsValidShortcut)
                .DistinctBy(item => item.SemanticKey, StringComparer.Ordinal)
                .Take(256)
                .Select(item => item.Clone())
                .ToList();
            return document;
        }
        catch
        {
            return new NavigationShortcutDocument();
        }
    }

    private static List<PrimaryMenuItem> ValidatePrimaryMenu(IEnumerable<PrimaryMenuItem> source)
    {
        var items = source
            .Where(IsValidPrimaryMenuItem)
            .DistinctBy(item => item.SemanticKey, StringComparer.Ordinal)
            .Take(64)
            .Select(item => item.Clone())
            .ToList();
        if (items.Count == 0 || items.Count(item => item.Type == "builtin" && item.Destination == "home") != 1)
            throw new InvalidOperationException("The primary menu must contain Home exactly once.");
        return items;
    }

    private static bool IsValidPrimaryMenuItem(PrimaryMenuItem item) => item.Type switch
    {
        "builtin" => item.Destination is "home" or "movies" or "series" or "music" or "audiobooks" or "for_you" or "calendar",
        _ => IsValidShortcut(item),
    };

    private static bool IsValidShortcut(PrimaryMenuItem item) => item.Type switch
    {
        "library" => item.LibraryId > 0 && HasLabel(item.Label),
        "section" => item.LibraryId > 0 && HasId(item.SectionId) && HasLabel(item.Label),
        "collection" => HasId(item.CollectionId) && HasLabel(item.Label) && (item.LibraryId is null or > 0),
        _ => false,
    };

    private static bool HasLabel(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
    private static bool HasId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
}
