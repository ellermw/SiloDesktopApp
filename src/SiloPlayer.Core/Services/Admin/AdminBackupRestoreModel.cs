using System.Text;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>An indexed choice: a section item, or a profile under its account (official IndexEntry).</summary>
public sealed record AdminRestoreEntry(string Key, string Section, AdminBackupRestoreItem Item, AdminBackupRestoreProfile? Profile, string? AccountKey)
{
    public bool IsProfile => Profile != null;
}

public sealed record AdminRestoreSelectionChange(IReadOnlySet<string> Selection, string? Notice);
public sealed record AdminRestorePlanSection(string Section, string Title, IReadOnlyList<string> Sentences);
public sealed record AdminRestoreCaveat(string Key, string Label, string Text);
public sealed record AdminRestoreBlockedGroup(string Key, string Title, IReadOnlyList<AdminBackupRestoreItem> Items);
public sealed record AdminRestoreSectionGroups(IReadOnlyList<AdminBackupRestoreItem> Restorable, IReadOnlyList<AdminRestoreBlockedGroup> Blocked, IReadOnlyList<AdminBackupRestoreItem> Same);
public sealed record AdminRestoreLibraryNames(IReadOnlyDictionary<string, string> Local, IReadOnlyDictionary<string, string> Backup);
public sealed record AdminRestoreLibraryGroup(string Key, string Name, IReadOnlyList<AdminBackupRestoreItem> Items);

/// <summary>same, differs, new or blocked (official ItemState).</summary>
public enum AdminRestoreItemState { Differs, New, Blocked, Same }

/// <summary>
/// Ports of the official restore model (web/src/pages/admin-backups/restoreModel.ts,
/// restoreGroups.ts and lib/profileDataTypes.ts at 8d253adbf). Selection keys are
/// <c>section:id</c> for items and <c>users:account/profile:id</c> for profiles.
/// </summary>
public static class AdminBackupRestoreModel
{
    public static readonly IReadOnlyList<string> DataTypeIds = ["watch_history", "ratings", "favorites", "collections", "dismissals", "preferences", "notifications"];
    /// <summary>On unless turned off: data a person would miss. Preferences overwrite, so they start off.</summary>
    public static readonly IReadOnlySet<string> DefaultDataTypes = new HashSet<string>(StringComparer.Ordinal) { "watch_history", "ratings", "favorites", "collections", "dismissals" };
    public const int FilterThreshold = 20;
    public static readonly IReadOnlyList<AdminRestoreItemState> StateOrder = [AdminRestoreItemState.Differs, AdminRestoreItemState.New, AdminRestoreItemState.Blocked, AdminRestoreItemState.Same];

    private static string T(string key, object? values = null) => AdminText.T(key, values);
    private static string Counted(string key, long count, object? extra = null) => T(key, AdminBackupText.Counted(count, extra));

    /// <summary>snake_case contract ids to the catalog's camelCase key segments.</summary>
    public static string Camel(string id)
    {
        var builder = new StringBuilder(id.Length);
        var upper = false;
        foreach (var c in id)
        {
            if (c == '_') { upper = true; continue; }
            builder.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return builder.ToString();
    }

    public static string ReasonSentence(string reason) => T("web.admin.backups.restore.reason." + Camel(reason));

    /// <summary>The sentence for a reason code, or null for a code this client has no sentence for (reasons are additive; a key is never shown in its place).</summary>
    public static string? KnownReasonSentence(string reason) => AdminText.Has("web.admin.backups.restore.reason." + Camel(reason)) ? ReasonSentence(reason) : null;
    public static string SectionTitle(string section) => T("web.admin.backups.restore.section." + Camel(section));
    public static string ReportErrorSentence(string code) => T("web.admin.backups.restore.reportError." + Camel(code));
    public static string DataTypeLabel(string id) => T("web.profiles.dataTypes." + Camel(id));
    public static string DataTypePhrase(string id) => T("web.admin.backups.dataTypes.phrase." + Camel(id));

    public static string PolicyDifferenceSentence(string? group, AdminBackupRestorePolicyDifference difference) => group is { Length: > 0 }
        ? T("web.admin.backups.restore.policy.group", new { field = difference.Field, here = difference.Here, backup = difference.Backup, group })
        : T("web.admin.backups.restore.policy.default", new { field = difference.Field, here = difference.Here, backup = difference.Backup });

    public static IReadOnlyList<string> NeverRestoredSettings(IEnumerable<string> entries) =>
        entries.Select(entry => AdminBackupText.Plain(entry, 400)).Select(entry => entry.EndsWith('.') || entry.EndsWith('_') ? entry + "*" : entry).ToArray();

    public static string ListPhrase(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "", 1 => parts[0], 2 => parts[0] + " and " + parts[1],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + ", and " + parts[^1],
    };

    /// <summary>English names without the serial comma (official joinNames).</summary>
    public static string JoinNames(IReadOnlyList<string> names) => names.Count <= 1 ? names.FirstOrDefault() ?? ""
        : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    public static string ItemKey(string section, string id) => section + ":" + id;
    public static string ProfileKey(string accountId, string profileId) => "users:" + accountId + "/profile:" + profileId;

    public static IReadOnlyDictionary<string, AdminRestoreEntry> Index(AdminBackupRestoreSet set)
    {
        var index = new Dictionary<string, AdminRestoreEntry>(StringComparer.Ordinal);
        foreach (var section in set.Sections ?? [])
            foreach (var item in section.Items ?? [])
            {
                var key = ItemKey(section.Id!, item.Id!);
                index[key] = new(key, section.Id!, item, null, null);
                foreach (var profile in item.Account?.Profiles ?? [])
                {
                    var profileKey = ProfileKey(item.Id!, profile.Id!);
                    index[profileKey] = new(profileKey, "users", item, profile, key);
                }
            }
        return index;
    }

    /// <summary>The restore order of every key, for stable iteration.</summary>
    public static IReadOnlyList<string> Order(AdminBackupRestoreSet set) => Index(set).Keys.ToArray();

    public static bool Selectable(AdminRestoreEntry? entry) => entry != null &&
        (entry.Profile is { } profile ? profile.Selectable == true && entry.Item.Selectable == true : entry.Item.Selectable == true);

    private static IReadOnlyList<string> Requires(AdminRestoreEntry entry) => entry.AccountKey != null ? [entry.AccountKey] : entry.Item.Requires ?? [];

    private static readonly Dictionary<string, string> BrandingNames = new(StringComparer.Ordinal)
    {
        ["branding.wordmark_ref"] = "web.admin.backups.restore.branding.wordmark", ["branding.wordmark_light_ref"] = "web.admin.backups.restore.branding.wordmarkLight",
        ["branding.mark_ref"] = "web.admin.backups.restore.branding.mark", ["branding.mark_light_ref"] = "web.admin.backups.restore.branding.markLight",
        ["branding.favicon_ref"] = "web.admin.backups.restore.branding.favicon", ["branding.login_bg_ref"] = "web.admin.backups.restore.branding.loginBackground",
    };

    private static string BrandingName(string key)
    {
        if (BrandingNames.TryGetValue(key, out var known)) return T(known);
        var name = key.StartsWith("branding.", StringComparison.Ordinal) ? key["branding.".Length..] : key;
        if (name.EndsWith("_ref", StringComparison.Ordinal)) name = name[..^4];
        return name.Replace('_', ' ');
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], System.Globalization.CultureInfo.CurrentCulture) + text[1..];

    /// <summary>An item's display name: branding items carry a setting key as their label.</summary>
    /// <summary>An item's name as plain display text; third-party names never carry control or direction characters.</summary>
    public static string DisplayLabel(string? kind, string? label) => AdminBackupText.Plain(kind == "branding" ? Capitalize(BrandingName(label ?? "")) : label);

    /// <summary>A profile or account name from the backup as plain display text.</summary>
    public static string PlainName(string? name) => AdminBackupText.Plain(name);

    private static string ItemName(AdminRestoreEntry entry)
    {
        if (entry.Profile is { } profile) return T("web.admin.backups.restore.itemName.profile", new { name = PlainName(profile.Name) });
        var name = PlainName(entry.Item.Label);
        return entry.Item.Kind switch
        {
            "library" => T("web.admin.backups.restore.itemName.library", new { name }),
            "settings_page" => T("web.admin.backups.restore.itemName.page", new { name }),
            "collection" => T("web.admin.backups.restore.itemName.collection", new { name }),
            "template_collection" => T("web.admin.backups.restore.itemName.template", new { name }),
            "home_section" => T("web.admin.backups.restore.itemName.section", new { name }),
            "branding" => T("web.admin.backups.restore.itemName.branding", new { name = BrandingName(name ?? "") }),
            "account" => T("web.admin.backups.restore.itemName.account", new { name }),
            _ => name ?? "",
        };
    }

    /// <summary>Chooses items and everything they require, transitively; an item whose requirement can't be chosen stays out.</summary>
    public static AdminRestoreSelectionChange Choose(IReadOnlyDictionary<string, AdminRestoreEntry> index, IReadOnlySet<string> selection, IReadOnlyList<string> keys)
    {
        var next = new HashSet<string>(selection, StringComparer.Ordinal);
        var added = new List<AdminRestoreEntry>();
        var blocked = new List<string>();
        foreach (var key in keys)
        {
            if (!index.TryGetValue(key, out var entry) || !Selectable(entry) || next.Contains(key)) continue;
            var (required, problem) = RequirementsOf(index, entry);
            if (problem != null) { blocked.Add(problem); continue; }
            next.Add(key);
            foreach (var dependency in required)
                if (next.Add(dependency.Key) && !keys.Contains(dependency.Key)) added.Add(dependency);
        }
        var notices = new List<string>(blocked);
        if (added.Count > 0) notices.Add(T("web.admin.backups.restore.notice.alsoChose", new { count = added.Count, names = JoinNames(added.Select(ItemName).ToArray()) }));
        return new(next, notices.Count > 0 ? string.Join(" ", notices) : null);
    }

    private static (List<AdminRestoreEntry> Required, string? Problem) RequirementsOf(IReadOnlyDictionary<string, AdminRestoreEntry> index, AdminRestoreEntry entry)
    {
        var required = new List<AdminRestoreEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { entry.Key };
        var pending = new List<string>(Requires(entry));
        for (var position = 0; position < pending.Count; position++)
        {
            var requiredKey = pending[position];
            if (!seen.Add(requiredKey)) continue;
            if (!index.TryGetValue(requiredKey, out var dependency))
                return (required, T("web.admin.backups.restore.notice.needsMissingLibrary", new { item = Capitalize(ItemName(entry)) }));
            if (!Selectable(dependency))
                return (required, T("web.admin.backups.restore.notice.needsBlocked", new { item = Capitalize(ItemName(entry)), dependency = ItemName(dependency) }));
            required.Add(dependency);
            pending.AddRange(Requires(dependency));
        }
        return (required, null);
    }

    /// <summary>Unchooses items and every chosen item that requires one of them, until nothing changes.</summary>
    public static AdminRestoreSelectionChange Unchoose(IReadOnlyDictionary<string, AdminRestoreEntry> index, IReadOnlySet<string> selection, IReadOnlyList<string> keys)
    {
        var next = new HashSet<string>(selection, StringComparer.Ordinal);
        foreach (var key in keys) next.Remove(key);
        var dependents = new List<AdminRestoreEntry>();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var key in next.ToArray())
                if (index.TryGetValue(key, out var entry) && Requires(entry).Any(requiredKey => !next.Contains(requiredKey)))
                {
                    next.Remove(key); changed = true;
                    if (!keys.Contains(key)) dependents.Add(entry);
                }
        }
        if (dependents.Count == 0) return new(next, null);
        var names = dependents.Count <= 3 ? JoinNames(dependents.Select(ItemName).ToArray()) : Counted("web.admin.backups.restore.notice.itemCount", dependents.Count);
        return new(next, T("web.admin.backups.restore.notice.alsoUnchose", new { count = dependents.Count, names }));
    }

    /// <summary>Retains only offered choices whose requirements are also retained, transitively.</summary>
    public static IReadOnlySet<string> Effective(IReadOnlyDictionary<string, AdminRestoreEntry> index, IReadOnlySet<string> chosen)
    {
        var offered = chosen.Where(key => index.TryGetValue(key, out var entry) && Selectable(entry)).ToHashSet(StringComparer.Ordinal);
        // Reload must not silently add newly required items, or keep dependents of unavailable choices.
        return Unchoose(index, offered, []).Selection;
    }

    /// <summary>The keys an item's checkbox toggles: the item and, for an account, its choosable profiles.</summary>
    public static IReadOnlyList<string> ToggleKeys(string section, AdminBackupRestoreItem item) =>
        new[] { ItemKey(section, item.Id!) }.Concat((item.Account?.Profiles ?? []).Where(profile => profile.Selectable == true)
            .Select(profile => ProfileKey(item.Id!, profile.Id!))).ToArray();

    public static IReadOnlyList<AdminRestoreEntry> ChosenItems(AdminBackupRestoreSet set, IReadOnlySet<string> selection) =>
        Index(set).Values.Where(entry => !entry.IsProfile && selection.Contains(entry.Key)).ToArray();

    /// <summary>A new account must restore its primary profile with any of its others.</summary>
    public static AdminBackupRestoreProfile? RequiredPrimary(AdminBackupRestoreItem account) => account.Account?.Target != "new" ? null
        : account.Account.Profiles?.FirstOrDefault(profile => profile.IsPrimary == true && profile.Selectable == true);

    public static IReadOnlyList<AdminBackupRestoreProfile> ChosenProfiles(AdminBackupRestoreItem account, IReadOnlySet<string> selection)
    {
        var primary = RequiredPrimary(account);
        var chosen = (account.Account?.Profiles ?? []).Where(profile => profile.Selectable == true && selection.Contains(ProfileKey(account.Id!, profile.Id!))).ToList();
        if (primary != null && chosen.Count > 0 && !chosen.Contains(primary)) chosen.Insert(0, primary);
        return chosen;
    }

    public static long PendingCount(AdminBackupRestoreDataType data) => data.Pending ?? data.Count ?? 0;

    private static IReadOnlyList<string> ChosenDataTypes(IReadOnlyList<AdminRestoreEntry> accounts, IReadOnlySet<string> selection, IReadOnlySet<string> dataTypes)
    {
        var carried = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in accounts)
            foreach (var profile in ChosenProfiles(entry.Item, selection))
                foreach (var data in profile.DataTypes ?? [])
                    if (PendingCount(data) > 0) carried.Add(data.Id!);
        return DataTypeIds.Where(id => dataTypes.Contains(id) && carried.Contains(id)).ToArray();
    }

    /// <summary>The request's selection: targets the preview showed, each profile with its chosen data types.</summary>
    public static AdminBackupRestoreSelection SelectionRequest(AdminBackupRestoreSet set, IReadOnlySet<string> selection, IReadOnlySet<string> dataTypes)
    {
        var lists = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["libraries"] = [], ["plugins"] = [], ["settings"] = [], ["collections"] = [], ["home_sections"] = [],
        };
        var users = new List<AdminBackupRestoreUserSelection>();
        foreach (var entry in ChosenItems(set, selection))
        {
            if (entry.Section == "users")
                users.Add(new(entry.Item.Id!, entry.Item.Account?.Target ?? "new", ChosenProfiles(entry.Item, selection).Select(profile =>
                    new AdminBackupRestoreProfileSelection(profile.Id!, profile.Target!,
                        DataTypeIds.Where(id => dataTypes.Contains(id) && (profile.DataTypes ?? []).Any(data => data.Id == id)).ToArray())).ToArray()));
            else if (lists.TryGetValue(entry.Section, out var list)) list.Add(entry.Item.Id!);
        }
        return new()
        {
            Libraries = lists["libraries"], Plugins = lists["plugins"], Settings = lists["settings"], Collections = lists["collections"],
            HomeSections = lists["home_sections"], Users = users,
        };
    }

    public static IReadOnlyList<AdminBackupRestoreSetting> DifferingSettings(AdminBackupRestoreItem item) =>
        (item.Settings ?? []).Where(setting => setting.Status == "differs").ToArray();

    private static string ItemSentence(AdminBackupRestoreItem item)
    {
        var name = PlainName(item.Label);
        switch (item.Kind)
        {
            case "library":
                return T(item.Status == "not_on_this_server" ? "web.admin.backups.restore.plan.libraryAdd" : "web.admin.backups.restore.plan.libraryUpdate", new { name });
            case "plugin":
                var plugin = item.Plugin;
                if (plugin?.InstalledVersion is { Length: > 0 } installed)
                    return installed == plugin.Version ? T("web.admin.backups.restore.plan.pluginSettings", new { name })
                        : T("web.admin.backups.restore.plan.pluginSettingsOtherVersion", new { name, version = installed });
                if (plugin is { ConfigRestorable: false }) return T("web.admin.backups.restore.plan.pluginInstallOff", new { name });
                if (plugin is { ConfigKeys.Length: > 0 }) return T("web.admin.backups.restore.plan.pluginInstallWithSettings", new { name });
                return T("web.admin.backups.restore.plan.pluginInstall", new { name });
            case "settings_page":
                return Counted(item.ExternalService == true ? "web.admin.backups.restore.plan.settingsPageExternal" : "web.admin.backups.restore.plan.settingsPage",
                    DifferingSettings(item).Count, new { name });
            case "account":
                return T(item.Account?.Target == "new" ? "web.admin.backups.restore.plan.accountCreate" : "web.admin.backups.restore.plan.accountMerge", new { name });
            case "collection":
                var collection = item.Collection;
                if (item.Status == "not_on_this_server")
                    return collection != null ? Counted("web.admin.backups.restore.plan.collectionCreateWithItems", (collection.Items ?? 0) - (collection.UnknownItems ?? 0), new { name })
                        : T("web.admin.backups.restore.plan.collectionCreate", new { name });
                return collection != null ? Counted("web.admin.backups.restore.plan.collectionAddItems", collection.MissingItems ?? 0, new { name })
                    : T("web.admin.backups.restore.plan.collectionAddMissing", new { name });
            case "template_collection":
                return T(item.Collection?.LocalId is { Length: > 0 } ? "web.admin.backups.restore.plan.templateApply" : "web.admin.backups.restore.plan.templateRecreate", new { name });
            case "home_section":
                return T(item.HomeSection?.LocalId is { Length: > 0 } ? "web.admin.backups.restore.plan.sectionReplace" : "web.admin.backups.restore.plan.sectionAdd", new { name });
            case "item_artwork":
                return T("web.admin.backups.restore.plan.artwork", new { name });
            default:
                return T("web.admin.backups.restore.plan.branding", new { name = BrandingName(name ?? "") });
        }
    }

    private static IReadOnlyList<string> UsersSentences(IReadOnlyList<AdminRestoreEntry> accounts, IReadOnlySet<string> selection, IReadOnlySet<string> dataTypes)
    {
        var sentences = new List<string>();
        int ProfileCount(IEnumerable<AdminRestoreEntry> group) => group.Sum(entry => ChosenProfiles(entry.Item, selection).Count);
        var merged = accounts.Where(entry => entry.Item.Account?.Target != "new").ToArray();
        var created = accounts.Where(entry => entry.Item.Account?.Target == "new").ToArray();
        if (merged.Length > 0)
        {
            var profiles = ProfileCount(merged);
            sentences.Add(profiles > 0
                ? Counted("web.admin.backups.restore.plan.mergeAccountsWithProfiles", merged.Length, new { profiles = Counted("web.admin.backups.restore.plan.profileCount", profiles) })
                : Counted("web.admin.backups.restore.plan.mergeAccounts", merged.Length));
        }
        if (created.Length > 0)
        {
            var profiles = ProfileCount(created);
            sentences.Add(profiles > 0
                ? Counted("web.admin.backups.restore.plan.createAccountsWithProfiles", created.Length, new { profiles = Counted("web.admin.backups.restore.plan.profileCount", profiles) })
                : Counted("web.admin.backups.restore.plan.createAccounts", created.Length));
        }
        if (ProfileCount(accounts) > 0)
        {
            var types = ChosenDataTypes(accounts, selection, dataTypes);
            sentences.Add(types.Count > 0 ? T("web.admin.backups.restore.plan.restoreData", new { data = ListPhrase(types.Select(DataTypePhrase).ToArray()) })
                : dataTypes.Count > 0 ? T("web.admin.backups.restore.plan.nothingToMerge") : T("web.admin.backups.restore.plan.profilesWithoutData"));
        }
        return sentences;
    }

    private static IReadOnlyList<string> ItemSentences(IReadOnlyList<AdminRestoreEntry> entries)
    {
        var templates = entries.Where(entry => entry.Item.Kind == "template_collection").ToArray();
        var recreated = templates.Count(entry => entry.Item.Collection?.LocalId is not { Length: > 0 });
        var updated = templates.Length - recreated;
        var sentences = entries.Where(entry => entry.Item.Kind != "template_collection").Select(entry => ItemSentence(entry.Item)).ToList();
        if (recreated > 0) sentences.Add(Counted("web.admin.backups.restore.plan.templatesRecreate", recreated));
        if (updated > 0) sentences.Add(Counted("web.admin.backups.restore.plan.templatesApply", updated));
        return sentences;
    }

    private static IReadOnlyList<string> HomeSectionSentences(IReadOnlyList<AdminRestoreEntry> entries)
    {
        if (entries.Count == 0) return [];
        var scopes = entries.Select(entry => entry.Item.HomeSection?.Scope ?? "home").ToHashSet(StringComparer.Ordinal);
        if (scopes.Count == 1 && scopes.Contains("home")) return [Counted("web.admin.backups.restore.plan.homeSections", entries.Count)];
        return [Counted(scopes.Count == 1 ? "web.admin.backups.restore.plan.libraryPageSections" : "web.admin.backups.restore.plan.mixedSections", entries.Count)];
    }

    /// <summary>The chosen items as sentences, grouped by section in restore order.</summary>
    public static IReadOnlyList<AdminRestorePlanSection> Plan(AdminBackupRestoreSet set, IReadOnlySet<string> selection, IReadOnlySet<string> dataTypes)
    {
        var chosen = ChosenItems(set, selection);
        return (set.Sections ?? []).Select(section =>
        {
            var inSection = chosen.Where(entry => entry.Section == section.Id).ToArray();
            var sentences = section.Id switch
            {
                "users" => UsersSentences(inSection, selection, dataTypes),
                "home_sections" => HomeSectionSentences(inSection),
                _ => ItemSentences(inSection),
            };
            return new AdminRestorePlanSection(section.Id!, SectionTitle(section.Id!), sentences);
        }).Where(group => group.Sentences.Count > 0).ToArray();
    }

    /// <summary>What a chosen item leaves out or needs afterwards (review's "Left out" list).</summary>
    public static IReadOnlyList<AdminRestoreCaveat> Caveats(AdminBackupRestoreSet set, IReadOnlySet<string> selection)
    {
        var caveats = new List<AdminRestoreCaveat>();
        foreach (var entry in ChosenItems(set, selection))
        {
            var item = entry.Item;
            void Add(string suffix, string text, string? label = null) => caveats.Add(new(entry.Key + ":" + suffix, label ?? DisplayLabel(item.Kind, item.Label), text));
            if (item.Reason is "other_version" or "config_needs_another_key") Add("reason", ReasonSentence(item.Reason));
            if (item.MissingBlobs is { Length: > 0 } missing) Add("files", Counted("web.admin.backups.restore.caveat.filesMissing", missing.Length));
            var settings = item.Settings ?? [];
            var otherKey = settings.Count(setting => setting.Status == "needs_another_key");
            if (otherKey > 0) Add("key", Counted("web.admin.backups.restore.caveat.otherKeySettings", otherKey));
            var stricter = settings.Count(setting => setting.Status == "kept_stricter");
            if (settings.Any(setting => setting.Status == "cleared")) Add("cleared", T("web.admin.backups.restore.caveat.credentialsCleared"));
            var unverified = settings.Count(setting => setting.Status == "unverified_backup");
            if (unverified > 0) Add("unverified", Counted("web.admin.backups.restore.caveat.unverifiedSettings", unverified));
            if (stricter > 0) Add("stricter", Counted("web.admin.backups.restore.caveat.stricterSettings", stricter));
            if (item.Collection?.UnknownItems is long unknown && unknown > 0) Add("unknown", Counted("web.admin.backups.restore.caveat.unknownItems", unknown));
            if (item.RestartRequired == true)
            {
                var keys = DifferingSettings(item).Where(setting => setting.RestartRequired == true).Select(setting => setting.Key).ToArray();
                Add("restart", keys.Length > 0 ? T("web.admin.backups.restore.caveat.restartKeys", new { keys = string.Join(", ", keys) }) : T("web.admin.backups.restore.caveat.restart"));
            }
            if (item.Account is not { } account) continue;
            if (account.Admin == true && account.Target == "new") Add("admin", T("web.admin.backups.restore.caveat.administrator"));
            if (account.LibrariesLeftOut is long dropped && dropped > 0) Add("account-libraries", Counted("web.admin.backups.restore.caveat.librariesDropped", dropped));
            var profiles = ChosenProfiles(item, selection);
            var extra = profiles.Count(profile => profile.Target == "new") - (account.ProfileRoom ?? 0);
            if (extra > 0) Add("room", Counted("web.admin.backups.restore.caveat.noRoom", extra, new { room = Counted("web.admin.backups.restore.caveat.roomCount", account.ProfileRoom ?? 0) }));
            foreach (var profile in profiles)
            {
                var label = T("web.admin.backups.restore.caveat.profileLabel", new { account = PlainName(item.Label), profile = PlainName(profile.Name) });
                if (profile.NoLibraryAccess == true) Add("profile:" + profile.Id + ":access", T("web.admin.backups.restore.caveat.noLibraryAccess"), label);
                else if (profile.LibrariesLeftOut is long left && left > 0) Add("profile:" + profile.Id + ":libraries", Counted("web.admin.backups.restore.caveat.librariesDropped", left), label);
            }
        }
        return caveats;
    }

    public static string ReportHeadline(AdminBackupRestoreCounts counts)
    {
        var parts = new List<string>
        {
            counts.Restored > 0 ? Counted("web.admin.backups.restore.report.restored", counts.Restored!.Value) : T("web.admin.backups.restore.report.restoredNothing"),
        };
        if (counts.Unchanged > 0) parts.Add(Counted("web.admin.backups.restore.report.unchanged", counts.Unchanged!.Value));
        if (counts.LeftOut > 0) parts.Add(Counted("web.admin.backups.restore.report.leftOut", counts.LeftOut!.Value));
        parts.Add(counts.Failed > 0 ? Counted("web.admin.backups.restore.report.failed", counts.Failed!.Value) : T("web.admin.backups.restore.report.nothingFailed"));
        return T("web.admin.backups.restore.report.headline", new { parts = string.Join(", ", parts) });
    }

    // ── Groups (restoreGroups.ts) ────────────────────────────────────

    public static AdminRestoreItemState State(AdminBackupRestoreItem item)
    {
        if (item.Selectable == true) return item.Status == "not_on_this_server" ? AdminRestoreItemState.New : AdminRestoreItemState.Differs;
        return item.Status == "on_this_server" || item.Reason == "same" ? AdminRestoreItemState.Same : AdminRestoreItemState.Blocked;
    }

    public static IReadOnlyDictionary<AdminRestoreItemState, int> CountStates(IEnumerable<AdminBackupRestoreItem> items)
    {
        var counts = StateOrder.ToDictionary(state => state, _ => 0);
        foreach (var item in items) counts[State(item)]++;
        return counts;
    }

    private static string StateName(AdminRestoreItemState state) => state.ToString().ToLowerInvariant();

    public static string StateCountsText(IReadOnlyDictionary<AdminRestoreItemState, int> counts) => string.Join(" · ",
        new[] { AdminRestoreItemState.Same, AdminRestoreItemState.Differs, AdminRestoreItemState.New, AdminRestoreItemState.Blocked }
            .Where(state => counts[state] > 0).Select(state => Counted("web.admin.backups.restore.counts." + StateName(state) + "Short", counts[state])));

    public static string StateCountsLabel(IReadOnlyDictionary<AdminRestoreItemState, int> counts)
    {
        var parts = StateOrder.Where(state => counts[state] > 0).Select(state => Counted("web.admin.backups.restore.counts." + StateName(state) + "Long", counts[state])).ToArray();
        return parts.Length > 0 ? string.Join(", ", parts) : T("web.admin.backups.restore.counts.empty");
    }

    public static string NothingToRestore(IReadOnlyDictionary<AdminRestoreItemState, int> counts)
    {
        if (counts[AdminRestoreItemState.Blocked] == 0) return Counted("web.admin.backups.restore.nothing.allMatch", counts[AdminRestoreItemState.Same]);
        var blocked = Counted("web.admin.backups.restore.counts.blockedLong", counts[AdminRestoreItemState.Blocked]);
        return counts[AdminRestoreItemState.Same] > 0
            ? T("web.admin.backups.restore.nothing.sameAndBlocked", new { same = Counted("web.admin.backups.restore.counts.sameLong", counts[AdminRestoreItemState.Same]), blocked })
            : T("web.admin.backups.restore.nothing.blocked", new { blocked });
    }

    private static string BlockedTitleKey(AdminBackupRestoreItem item)
    {
        if (item.Reason is { Length: > 0 } reason) return "web.admin.backups.restore.blocked." + Camel(reason);
        return item.Status switch
        {
            "folder_missing" => "web.admin.backups.restore.blocked.folderMissing",
            "ambiguous" => "web.admin.backups.restore.blocked.ambiguous",
            "needs_another_key" => "web.admin.backups.restore.blocked.needsAnotherKey",
            _ => "web.admin.backups.restore.blocked.fallback",
        };
    }

    public static AdminRestoreSectionGroups GroupSection(IReadOnlyList<AdminBackupRestoreItem> items)
    {
        var restorable = new List<AdminBackupRestoreItem>();
        var same = new List<AdminBackupRestoreItem>();
        var blocked = new Dictionary<string, (string Title, List<AdminBackupRestoreItem> Items)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var item in items)
        {
            switch (State(item))
            {
                case AdminRestoreItemState.Same: same.Add(item); break;
                case AdminRestoreItemState.Blocked:
                    var key = item.Reason ?? "status:" + item.Status;
                    var title = BlockedTitleKey(item);
                    if (!AdminText.Has(title)) title = "web.admin.backups.restore.blocked.fallback";
                    if (!blocked.TryGetValue(key, out var group)) { group = (title, []); blocked[key] = group; order.Add(key); }
                    group.Items.Add(item); break;
                default: restorable.Add(item); break;
            }
        }
        return new(restorable, order.Select(key => new AdminRestoreBlockedGroup(key, Counted(blocked[key].Title, blocked[key].Items.Count), blocked[key].Items))
            .OrderByDescending(group => group.Items.Count).ToArray(), same);
    }

    public static AdminRestoreLibraryNames LibraryNames(AdminBackupRestoreSet set, IReadOnlyList<AdminBackupLocalLibrary> local)
    {
        var localNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var library in local) localNames[library.Id] = PlainName(library.Name);
        var backupNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mapping in set.LibraryMap ?? [])
        {
            backupNames[mapping.BackupId!] = PlainName(mapping.Name);
            if (mapping.LocalId is { Length: > 0 } localId && !localNames.ContainsKey(localId)) localNames[localId] = PlainName(mapping.Name);
        }
        return new(localNames, backupNames);
    }

    private static (string Key, string Name) ItemLibrary(AdminBackupRestoreItem item, AdminRestoreLibraryNames names)
    {
        if (item.Kind is "item_artwork" or "branding") return ("artwork", T("web.admin.backups.restore.library.artwork"));
        if (item.HomeSection?.Scope == "home") return ("home", T("web.admin.backups.restore.library.home"));
        var localId = item.HomeSection?.LibraryId ?? item.Collection?.LibraryIds?.FirstOrDefault();
        if (!string.IsNullOrEmpty(localId))
            return ("local:" + localId, names.Local.TryGetValue(localId, out var name) ? name : T("web.admin.backups.restore.library.fallback", new { id = PlainName(localId) }));
        if ((item.Requires ?? []).FirstOrDefault(key => key.StartsWith("libraries:", StringComparison.Ordinal)) is { } required)
        {
            var backupId = required["libraries:".Length..];
            return ("backup:" + backupId, names.Backup.TryGetValue(backupId, out var name) ? name : T("web.admin.backups.restore.library.fallback", new { id = PlainName(backupId) }));
        }
        return ("none", T("web.admin.backups.restore.library.none"));
    }

    public static IReadOnlyList<AdminRestoreLibraryGroup> GroupByLibrary(IReadOnlyList<AdminBackupRestoreItem> items, AdminRestoreLibraryNames names)
    {
        var groups = new Dictionary<string, (string Name, List<AdminBackupRestoreItem> Items)>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var library = ItemLibrary(item, names);
            if (!groups.TryGetValue(library.Key, out var group)) { group = (library.Name, []); groups[library.Key] = group; }
            group.Items.Add(item);
        }
        static int Rank(string key) => key == "home" ? 0 : key is "artwork" or "none" ? 2 : 1;
        return groups.Select(pair => new AdminRestoreLibraryGroup(pair.Key, pair.Value.Name, pair.Value.Items))
            .OrderBy(group => Rank(group.Key)).ThenBy(group => group.Name, StringComparer.CurrentCulture).ToArray();
    }

    public static bool GroupsByLibrary(string section) => section is "collections" or "home_sections";

    public static bool MatchesFilter(AdminBackupRestoreItem item, string query, AdminRestoreLibraryNames names)
    {
        var needle = query.Trim().ToLowerInvariant();
        if (needle.Length == 0) return true;
        var account = item.Account;
        var haystack = new List<string?>
        {
            item.Label, item.Collection != null || item.HomeSection != null ? ItemLibrary(item, names).Name : null,
            item.Library?.LocalId is { } localId && names.Local.TryGetValue(localId, out var local) ? local : null,
            account?.Username, account?.Email, account?.LocalUsername,
        };
        haystack.AddRange((account?.Profiles ?? []).Select(profile => profile.Name));
        return haystack.Any(value => value != null && value.ToLowerInvariant().Contains(needle, StringComparison.Ordinal));
    }

    public static string SectionProgress(AdminBackupRestoreSection section, IReadOnlySet<string> selection)
    {
        var restorable = (section.Items ?? []).Where(item => item.Selectable == true).ToArray();
        if (restorable.Length == 0) return T("web.admin.backups.restore.progress.nothing");
        var chosen = restorable.Count(item => selection.Contains(ItemKey(section.Id!, item.Id!)));
        return T("web.admin.backups.restore.progress.chosen", new { chosen, chosen_text = AdminText.FormatNumber(chosen), total_text = AdminText.FormatNumber(restorable.Length) });
    }

    /// <summary>
    /// What a restore replaces in a section or writes to a template collection, as the codes the server sends (official RestoreChange).
    /// Every code but <c>collection_items</c>, which carries a count, has a label; an older server's English word is not a code.
    /// </summary>
    private static readonly HashSet<string> ChangeCodes = new(StringComparer.Ordinal)
    {
        "type", "title", "title_translation", "position", "featured", "item_limit", "enabled", "web_only", "collection_replaced", "configuration",
        "description", "visibility", "sort_order", "item_sort", "source", "sync_schedule", "poster", "backdrop", "libraries",
    };

    /// <summary>
    /// One entry of a row's change list in the interface language (official changeLabel). A code this client has no label for, or an
    /// older server's English word, shows as sent instead of a blank bullet. <c>collection_items</c> takes its count from the home section.
    /// </summary>
    public static string ChangeLabel(AdminBackupRestoreItem item, string change)
    {
        if (change == "collection_items") return Counted("web.admin.backups.restore.rows.change.collectionItems", Math.Max(0, item.HomeSection?.MissingCollectionItems ?? 0));
        return ChangeCodes.Contains(change) ? T("web.admin.backups.restore.rows.change." + Camel(change)) : AdminBackupText.Plain(change);
    }

    /// <summary>A row's change codes: a home section's, else a template collection's. A null entry has nothing to show and is skipped, as the official client does.</summary>
    public static IReadOnlyList<string> Changes(AdminBackupRestoreItem item) =>
        (item.HomeSection?.Changes ?? item.Collection?.Changes ?? []).Where(change => change != null).ToArray();

    /// <summary>
    /// The line under an account's name: what merging adds, then the username and email when the name is not already one of them
    /// (an invited account is labeled by its email, an account with no username by its email).
    /// </summary>
    public static string AccountContact(AdminBackupRestoreItem item, string? pending)
    {
        var account = item.Account;
        var label = item.Label;
        return string.Join(" · ", new[] { pending, account?.Username != label ? account?.Username : null, account?.Email != label ? account?.Email : null }
            .Where(part => !string.IsNullOrEmpty(part)).Select(part => AdminBackupText.Plain(part)).Where(part => part.Length > 0));
    }

    /// <summary>
    /// Who a matched account merges into: the account's own username, or its email when it has none (the server sends the email in
    /// <c>local_username</c> now; an older server left it empty), else the backup's username, else the row's own name.
    /// </summary>
    public static string MergeDestination(AdminBackupRestoreItem item)
    {
        var account = item.Account;
        return AdminBackupText.Plain(account?.LocalUsername is { Length: > 0 } local ? local : account?.Username is { Length: > 0 } username ? username : item.Label);
    }

    public static string? RowSecondary(AdminBackupRestoreItem item, AdminRestoreLibraryNames names, bool withLibrary = false)
    {
        var library = withLibrary && (item.Collection != null || item.HomeSection != null) ? ItemLibrary(item, names).Name : null;
        static string? Join(params string?[] parts) => string.Join(" · ", parts.Where(part => !string.IsNullOrEmpty(part))) is { Length: > 0 } text ? text : null;
        switch (item.Kind)
        {
            case "library":
                var local = item.Library?.LocalId is { } localId && names.Local.TryGetValue(localId, out var named) ? named : null;
                if (State(item) == AdminRestoreItemState.Same) return T("web.admin.backups.restore.row.sameFolders", new { name = local ?? PlainName(item.Label) });
                return item.Library?.Paths is { } paths ? string.Join(", ", paths.Select(path => AdminBackupText.Plain(path, 400))) : null;
            case "plugin":
                return item.Plugin?.InstalledVersion is { Length: > 0 } version ? T("web.admin.backups.restore.row.pluginInstalled", new { version })
                    : T("web.admin.backups.restore.row.pluginNotInstalled");
            case "settings_page":
                var changed = DifferingSettings(item).Count;
                return changed > 0 ? Counted("web.admin.backups.restore.row.settingsDiffer", changed) : null;
            case "template_collection":
                return Join(library, T(item.Collection?.LocalId is { Length: > 0 } ? "web.admin.backups.restore.row.templateChanges" : "web.admin.backups.restore.row.templateCreated"));
            case "collection":
                if (item.Collection is not { } collection) return library;
                return Join(library, item.Status == "differs" && collection.MissingItems > 0
                    ? Counted("web.admin.backups.restore.row.collectionAdds", collection.MissingItems!.Value)
                    : Counted("web.admin.backups.restore.row.collectionItems", collection.Items ?? 0));
            case "home_section":
                return Join(library, item.HomeSection is { } section ? AdminHomeSectionsKinds.Label(section.SectionType ?? "") : null,
                    T(item.HomeSection?.LocalId is { Length: > 0 } ? "web.admin.backups.restore.row.sectionReplaces" : "web.admin.backups.restore.row.sectionAdded"));
            default:
                return library;
        }
    }

    /// <summary>The Now / In backup cells of one setting (official settingValues).</summary>
    public static (string Now, string Backup) SettingValues(AdminBackupRestoreSetting setting)
    {
        var set = T("web.admin.backups.restore.setting.set");
        var empty = T("web.admin.backups.restore.setting.empty");
        var current = setting.CurrentValue is { Length: > 0 } now ? now : empty;
        var backup = setting.BackupValue is { Length: > 0 } stored ? stored : empty;
        var masked = setting.Masked == true;
        return setting.Status switch
        {
            "needs_another_key" => (masked ? set : current, T("web.admin.backups.restore.setting.otherKey")),
            "cleared" => (masked ? set : current, masked ? T("web.admin.backups.restore.setting.cleared") : T("web.admin.backups.restore.setting.turnedOff", new { value = backup })),
            "unverified_backup" => (masked ? set : current, T("web.admin.backups.restore.setting.unverified")),
            "kept_stricter" => (T("web.admin.backups.restore.setting.keptStricter", new { value = current }), backup),
            _ when masked => (set, T("web.admin.backups.restore.setting.differentEncrypted")),
            _ => (current, backup),
        };
    }

    // ── Profile data (lib/profileDataTypes.ts) ───────────────────────

    public static string? PendingSummary(IEnumerable<AdminBackupRestoreDataType> data)
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var entry in data)
        {
            var pending = PendingCount(entry);
            if (pending > 0) totals[entry.Id!] = totals.GetValueOrDefault(entry.Id!) + pending;
        }
        if (totals.Count == 0) return null;
        var ordered = DataTypeIds.Where(totals.ContainsKey).OrderByDescending(id => totals[id]).ToArray();
        var items = string.Join(" · ", ordered.Take(3).Select(id => Counted("web.admin.backups.dataTypes.pending." + Camel(id), totals[id])));
        return T(ordered.Length > 3 ? "web.admin.backups.dataTypes.pending.toAddMore" : "web.admin.backups.dataTypes.pending.toAdd", new { items });
    }

    public static string DataCountsPhrase(AdminBackupRestoreReportData data)
    {
        var parts = new List<string>();
        if (data.Added > 0) parts.Add(T("web.profiles.dataTypes.counts.added", new { count = data.Added }));
        if (data.Updated > 0) parts.Add(T("web.profiles.dataTypes.counts.updated", new { count = data.Updated }));
        if (data.Unchanged > 0) parts.Add(T("web.profiles.dataTypes.counts.unchanged", new { count = data.Unchanged }));
        if (data.LeftOut > 0) parts.Add(T("web.profiles.dataTypes.counts.leftOut", new { count = data.LeftOut }));
        return parts.Count > 0 ? ListPhrase(parts) : T("web.profiles.dataTypes.counts.nothing");
    }

    public static string TitleList(IReadOnlyList<string> titles)
    {
        var shown = ListPhrase(titles.Take(10).Select(title => PlainName(title)).ToArray());
        var rest = titles.Count - 10;
        return rest > 0 ? T("web.profiles.dataTypes.titleListMore", new { titles = shown, count = rest }) : shown;
    }
}
