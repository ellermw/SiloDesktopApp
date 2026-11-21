using System.Reflection;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One piece of a message that carries bold or link markup.</summary>
public readonly record struct AdminPluginsTextRun(string Text, bool Bold, bool Link);

/// <summary>
/// Verbatim official Plugins messages (web.admin.plugins.*, plus the few web.admin.errors, web.admin.common and
/// web.admin.tasks messages this page shows) from web/src/locales/en.json at 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e,
/// compiled ICU AST restored to text. Admin messages are outside the generated viewer catalogs; shared
/// web.common.* and common.* messages come from the generated catalog.
/// </summary>
public static class AdminPluginsText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {        ["web.admin.plugins.capabilities.artwork"] = "Artwork",
        ["web.admin.plugins.capabilities.audiobooks"] = "Audiobooks",
        ["web.admin.plugins.capabilities.ebooks"] = "Ebooks",
        ["web.admin.plugins.capabilities.events"] = "Events",
        ["web.admin.plugins.capabilities.markers"] = "Markers",
        ["web.admin.plugins.capabilities.mediaAnalysis"] = "Media analysis",
        ["web.admin.plugins.capabilities.metadata"] = "Metadata",
        ["web.admin.plugins.capabilities.networkAccess"] = "Network access",
        ["web.admin.plugins.capabilities.requests"] = "Requests",
        ["web.admin.plugins.capabilities.scanning"] = "Scanning",
        ["web.admin.plugins.capabilities.scheduledTasks"] = "Scheduled tasks",
        ["web.admin.plugins.capabilities.signIn"] = "Sign-in",
        ["web.admin.plugins.capabilities.watchSync"] = "Watch sync",
        ["web.admin.plugins.capabilities.webPages"] = "Web pages",
        ["web.admin.plugins.catalog.emptyDescription"] = "Add a repository or upload a plugin file.",
        ["web.admin.plugins.catalog.emptyTitle"] = "No plugins available",
        ["web.admin.plugins.catalog.filterLabel"] = "Filter by what plugins do",
        ["web.admin.plugins.catalog.groupApprovedCommunity"] = "Approved community",
        ["web.admin.plugins.catalog.groupExternal"] = "Other sources",
        ["web.admin.plugins.catalog.groupSilo"] = "Made by Silo",
        ["web.admin.plugins.catalog.noMatches"] = "No catalog plugins match",
        ["web.admin.plugins.catalog.search"] = "Search the plugin catalog",
        ["web.admin.plugins.communityCatalog.description"] = "Reviewed by Silo maintainers to work as described and be safe for their documented use. These plugins remain maintained and supported by community contributors.",
        ["web.admin.plugins.communityCatalog.hideConfirm"] = "Hide and pause updates",
        ["web.admin.plugins.communityCatalog.hideDescription"] = "{count, plural, one{{count_text} installed plugin will keep running, but update discovery will pause until this catalog is included again.} other{{count_text} installed plugins will keep running, but update discovery will pause until this catalog is included again.}}",
        ["web.admin.plugins.communityCatalog.hideTitle"] = "Hide approved community plugins?",
        ["web.admin.plugins.communityCatalog.includeLabel"] = "Include approved community plugins",
        ["web.admin.plugins.communityCatalog.migrated"] = "{count, plural, one{{count_text} existing installation was moved here without changing configuration.} other{{count_text} existing installations were moved here without changing configuration.}}",
        ["web.admin.plugins.configForm.checkFailed"] = "Connection check failed.",
        ["web.admin.plugins.configForm.clearSecret"] = "Clear saved secret",
        ["web.admin.plugins.configForm.keepSecret"] = "Keep saved secret",
        ["web.admin.plugins.configForm.save"] = "Save config",
        ["web.admin.plugins.configForm.savedSecretPlaceholder"] = "Saved secret — leave blank to keep",
        ["web.admin.plugins.configForm.savedSecretStatus"] = "{label}: {state, select, cleared{will be cleared} other{saved}}{required, select, yes{ (required)} other{}}",
        ["web.admin.plugins.configForm.unsupportedSchema"] = "This plugin uses a configuration schema shape that the admin form does not support yet.",
        ["web.admin.plugins.detail.allPlugins"] = "All plugins",
        ["web.admin.plugins.detail.back"] = "Plugins",
        ["web.admin.plugins.detail.byPublisher"] = "by {publisher}",
        ["web.admin.plugins.detail.catalogErrorDescription"] = "This plugin isn't installed, and Silo couldn't read the catalog to look it up.",
        ["web.admin.plugins.detail.catalogErrorTitle"] = "Couldn't load the plugin catalog",
        ["web.admin.plugins.detail.enabledLabel"] = "Enabled",
        ["web.admin.plugins.detail.install"] = "Install",
        ["web.admin.plugins.detail.installationsErrorDescription"] = "Silo couldn't read the installed plugins. Try again in a moment.",
        ["web.admin.plugins.detail.installationsErrorTitle"] = "Couldn't load plugins",
        ["web.admin.plugins.detail.installing"] = "Installing...",
        ["web.admin.plugins.detail.loading"] = "Loading plugin...",
        ["web.admin.plugins.detail.missingConfig"] = "<b>Finish setting up {name}.</b> Add {fields}. Until then it can't do its job.",
        ["web.admin.plugins.detail.missingListLast"] = "{items} and {item}",
        ["web.admin.plugins.detail.missingListNext"] = "{items}, {item}",
        ["web.admin.plugins.detail.moreActions"] = "More actions",
        ["web.admin.plugins.detail.notFoundDescription"] = "This plugin isn't installed and isn't in any catalog Silo can see.",
        ["web.admin.plugins.detail.notFoundTitle"] = "Plugin not found",
        ["web.admin.plugins.detail.notInstalled"] = "Not installed",
        ["web.admin.plugins.detail.off"] = "Off",
        ["web.admin.plugins.detail.on"] = "On",
        ["web.admin.plugins.detail.pausedCommunity"] = "<b>Update checks are paused.</b> Approved community plugins are turned off, so Silo doesn't look for new versions of {name}. <link>Turn them back on</link> to resume its {has_policy, select, yes{“{policy}” setting} other{updates}}.",
        ["web.admin.plugins.detail.pausedRepository"] = "<b>Update checks are paused.</b> Its repository, {repository}, is disabled, so Silo doesn't look for new versions of {name}. <link>Enable it</link> to resume its {has_policy, select, yes{“{policy}” setting} other{updates}}.",
        ["web.admin.plugins.detail.pausedRepositoryUnnamed"] = "<b>Update checks are paused.</b> Its repository is disabled, so Silo doesn't look for new versions of {name}. <link>Enable it</link> to resume its {has_policy, select, yes{“{policy}” setting} other{updates}}.",
        ["web.admin.plugins.detail.policyAuto"] = "Update automatically",
        ["web.admin.plugins.detail.policyNotify"] = "Ask before updating",
        ["web.admin.plugins.detail.policyOff"] = "Don't check for updates",
        ["web.admin.plugins.detail.restart"] = "Restart",
        ["web.admin.plugins.detail.restartNow"] = "Restart now",
        ["web.admin.plugins.detail.restarting"] = "Restarting...",
        ["web.admin.plugins.detail.runtimeBackoff"] = "{count, plural, one{<b>{name} keeps stopping.</b> Silo has restarted it {count_text} time.} other{<b>{name} keeps stopping.</b> Silo has restarted it {count_text} times.}}",
        ["web.admin.plugins.detail.runtimeBackoffNext"] = "{count, plural, one{<b>{name} keeps stopping.</b> Silo has restarted it {count_text} time. The next attempt is at {time}.} other{<b>{name} keeps stopping.</b> Silo has restarted it {count_text} times. The next attempt is at {time}.}}",
        ["web.admin.plugins.detail.runtimeFailed"] = "{count, plural, one{<b>{name} stopped and won't restart on its own.</b> Silo has restarted it {count_text} time.} other{<b>{name} stopped and won't restart on its own.</b> Silo has restarted it {count_text} times.}}",
        ["web.admin.plugins.detail.startFailed"] = "<b>{name} failed to start.</b> Silo tries again the next time it needs the plugin.",
        ["web.admin.plugins.detail.tierNotice"] = "<b>{source}.</b> {notice}",
        ["web.admin.plugins.detail.uninstallConfirm"] = "Uninstall plugin",
        ["web.admin.plugins.detail.uninstallDescription"] = "Silo will stop the plugin, then remove its installation, configuration, and installed files. This cannot be undone.",
        ["web.admin.plugins.detail.uninstallMenu"] = "Uninstall...",
        ["web.admin.plugins.detail.uninstallTitle"] = "Uninstall {name}?",
        ["web.admin.plugins.detail.uninstalling"] = "Uninstalling...",
        ["web.admin.plugins.detail.updateFailed"] = "<b>Silo couldn't update {name} to {version}.</b> {reason}",
        ["web.admin.plugins.detail.updateFailedStays"] = "It stays on version {version}. Last tried {date}.",
        ["web.admin.plugins.detail.updateTo"] = "Update to {version}",
        ["web.admin.plugins.detail.updatedByUpload"] = "Updated by upload",
        ["web.admin.plugins.detail.updatesLabel"] = "Updates",
        ["web.admin.plugins.detail.updatesPaused"] = "Updates paused",
        ["web.admin.plugins.detail.version"] = "Version {version}",
        ["web.admin.plugins.hooks.plugins.applyUpdate.done"] = "Plugin updated",
        ["web.admin.plugins.hooks.plugins.applyUpdate.unconfirmed"] = "Plugin update could not be confirmed. Refresh installations before submitting again.",
        ["web.admin.plugins.hooks.plugins.authBinding.saved"] = "Sign-in provider saved",
        ["web.admin.plugins.hooks.plugins.authBinding.selectProfile"] = "Select an administrator profile before saving an auth binding.",
        ["web.admin.plugins.hooks.plugins.authBinding.unconfirmed"] = "Auth binding could not be confirmed. Reload the plugin before saving again.",
        ["web.admin.plugins.hooks.plugins.catalogSettings.changed"] = "Plugin catalog settings changed. Reload and review them before submitting another edit.",
        ["web.admin.plugins.hooks.plugins.catalogSettings.reloadBeforeEditing"] = "Reload plugin catalog settings before editing.",
        ["web.admin.plugins.hooks.plugins.catalogSettings.selectProfile"] = "Select an administrator profile before editing.",
        ["web.admin.plugins.hooks.plugins.catalogSettings.updateFailed"] = "Failed to update plugin catalog settings",
        ["web.admin.plugins.hooks.plugins.catalogSettings.updated"] = "Plugin catalog settings updated",
        ["web.admin.plugins.hooks.plugins.config.saved"] = "Plugin config saved",
        ["web.admin.plugins.hooks.plugins.config.selectProfile"] = "Select an administrator profile before saving plugin config.",
        ["web.admin.plugins.hooks.plugins.config.unconfirmed"] = "Plugin config could not be confirmed. Reload the plugin before saving again.",
        ["web.admin.plugins.hooks.plugins.install.done"] = "Plugin installed",
        ["web.admin.plugins.hooks.plugins.install.selectProfile"] = "Select an administrator profile before installing a plugin.",
        ["web.admin.plugins.hooks.plugins.install.unconfirmed"] = "Plugin install could not be confirmed. Refresh installations before submitting again.",
        ["web.admin.plugins.hooks.plugins.installation.updateFailed"] = "Failed to update plugin",
        ["web.admin.plugins.hooks.plugins.installation.updateSelectProfile"] = "Select an administrator profile before updating a plugin.",
        ["web.admin.plugins.hooks.plugins.installation.updated"] = "Plugin updated",
        ["web.admin.plugins.hooks.plugins.remove.done"] = "Plugin removed",
        ["web.admin.plugins.hooks.plugins.remove.selectProfile"] = "Select an administrator profile before removing a plugin.",
        ["web.admin.plugins.hooks.plugins.remove.unconfirmed"] = "Plugin removal could not be confirmed. Refresh installations before submitting again.",
        ["web.admin.plugins.hooks.plugins.repository.added"] = "Repository added",
        ["web.admin.plugins.hooks.plugins.repository.createSelectProfile"] = "Select an administrator profile before adding a repository.",
        ["web.admin.plugins.hooks.plugins.repository.createUnconfirmed"] = "Repository creation could not be confirmed. Refresh repositories before submitting again.",
        ["web.admin.plugins.hooks.plugins.repository.deleteSelectProfile"] = "Select an administrator profile before deleting a repository.",
        ["web.admin.plugins.hooks.plugins.repository.deleteUnconfirmed"] = "Repository deletion could not be confirmed. Refresh repositories before submitting again.",
        ["web.admin.plugins.hooks.plugins.repository.removed"] = "Repository removed",
        ["web.admin.plugins.hooks.plugins.repository.updateSelectProfile"] = "Select an administrator profile before updating a repository.",
        ["web.admin.plugins.hooks.plugins.repository.updateUnconfirmed"] = "Repository update could not be confirmed. Refresh repositories before submitting again.",
        ["web.admin.plugins.hooks.plugins.repository.updated"] = "Repository updated",
        ["web.admin.plugins.hooks.plugins.restart.requested"] = "Plugin restart requested",
        ["web.admin.plugins.hooks.plugins.restart.selectProfile"] = "Select an administrator profile before restarting a plugin.",
        ["web.admin.plugins.hooks.plugins.restart.unconfirmed"] = "Plugin restart could not be confirmed. Refresh installations before trying again.",
        ["web.admin.plugins.hooks.plugins.settings.selectProfile"] = "Select an administrator profile before changing plugin settings.",
        ["web.admin.plugins.hooks.plugins.taskBinding.saved"] = "Task binding saved",
        ["web.admin.plugins.hooks.plugins.taskBinding.savedRestart"] = "Task binding saved — restart the server to apply it",
        ["web.admin.plugins.hooks.plugins.taskBinding.selectProfile"] = "Select an administrator profile before saving a task binding.",
        ["web.admin.plugins.hooks.plugins.taskBinding.unconfirmed"] = "Task binding could not be confirmed. Reload the plugin before saving again.",
        ["web.admin.plugins.hooks.plugins.updateCheck.failed"] = "Failed to start plugin update check",
        ["web.admin.plugins.hooks.plugins.updateCheck.started"] = "Plugin update check started",
        ["web.admin.plugins.hooks.plugins.upload.done"] = "Plugin uploaded",
        ["web.admin.plugins.hooks.plugins.upload.selectProfile"] = "Select an administrator profile before uploading a plugin.",
        ["web.admin.plugins.hooks.plugins.upload.unconfirmed"] = "Plugin upload could not be confirmed. Refresh installations before uploading again.",
        ["web.admin.plugins.installed.emptyDescription"] = "Browse the Catalog tab to find and install plugins.",
        ["web.admin.plugins.installed.emptyTitle"] = "No plugins installed",
        ["web.admin.plugins.installed.filterAttention"] = "Needs attention",
        ["web.admin.plugins.installed.filterLabel"] = "Filter installed plugins",
        ["web.admin.plugins.installed.filterOff"] = "Off",
        ["web.admin.plugins.installed.filterUpdate"] = "Update available",
        ["web.admin.plugins.installed.noMatches"] = "No installed plugins match",
        ["web.admin.plugins.installed.search"] = "Search installed plugins",
        ["web.admin.plugins.list.clearFilters"] = "Clear search and filters",
        ["web.admin.plugins.list.clearSearch"] = "Clear plugin search",
        ["web.admin.plugins.list.filterAll"] = "All",
        ["web.admin.plugins.list.resultCount"] = "{count, plural, one{{count_text} plugin} other{{count_text} plugins}}",
        ["web.admin.plugins.list.resultCountFiltered"] = "{count, plural, one{{shown} of {count_text} plugin} other{{shown} of {count_text} plugins}}",
        ["web.admin.plugins.page.catalogTab"] = "Catalog",
        ["web.admin.plugins.page.checkUpdates"] = "Check for updates",
        ["web.admin.plugins.page.checkingUpdates"] = "Checking updates...",
        ["web.admin.plugins.page.installedTab"] = "Installed",
        ["web.admin.plugins.page.loading"] = "Loading plugins...",
        ["web.admin.plugins.page.subtitle"] = "Add metadata sources, playback markers, request routing, and watch sync to Silo.",
        ["web.admin.plugins.page.title"] = "Plugins",
        ["web.admin.plugins.provisioning.applied"] = "Applied",
        ["web.admin.plugins.provisioning.held"] = "Held",
        ["web.admin.plugins.provisioning.loadError"] = "Couldn’t load plugin provisioning status.",
        ["web.admin.plugins.provisioning.managed"] = "Managed by the environment",
        ["web.admin.plugins.provisioning.pending"] = "Pending",
        ["web.admin.plugins.provisioning.refused"] = "Refused",
        ["web.admin.plugins.provisioning.sourceManaged"] = "The deployment controls the package source and removal.",
        ["web.admin.plugins.provisioning.title"] = "Provisioning status",
        ["web.admin.plugins.provisioning.versionPinned"] = "The deployment pins this plugin to {version}.",
        ["web.admin.plugins.rail.about"] = "About",
        ["web.admin.plugins.rail.changelog"] = "Changelog",
        ["web.admin.plugins.rail.details"] = "Details",
        ["web.admin.plugins.rail.installed"] = "Installed",
        ["web.admin.plugins.rail.label"] = "About this plugin",
        ["web.admin.plugins.rail.lastStarted"] = "Last started",
        ["web.admin.plugins.rail.license"] = "License",
        ["web.admin.plugins.rail.licenseNotSpecified"] = "Not specified",
        ["web.admin.plugins.rail.nextRestart"] = "Next restart",
        ["web.admin.plugins.rail.pluginId"] = "Plugin ID",
        ["web.admin.plugins.rail.process"] = "Process",
        ["web.admin.plugins.rail.publisher"] = "Publisher",
        ["web.admin.plugins.rail.repository"] = "Repository",
        ["web.admin.plugins.rail.restarts"] = "Restarts",
        ["web.admin.plugins.rail.setup"] = "Setup",
        ["web.admin.plugins.rail.source"] = "Source",
        ["web.admin.plugins.rail.sourceCode"] = "Source code",
        ["web.admin.plugins.rail.state"] = "State",
        ["web.admin.plugins.rail.stateBackoff"] = "Waiting to restart",
        ["web.admin.plugins.rail.stateFailed"] = "Failed",
        ["web.admin.plugins.rail.stateRunning"] = "Running",
        ["web.admin.plugins.rail.stateStarting"] = "Starting",
        ["web.admin.plugins.rail.stateStopped"] = "Stopped",
        ["web.admin.plugins.rail.support"] = "Support",
        ["web.admin.plugins.repositories.add"] = "Add repository",
        ["web.admin.plugins.repositories.description"] = "Where Silo finds plugins.",
        ["web.admin.plugins.repositories.disable"] = "Disable",
        ["web.admin.plugins.repositories.empty"] = "No repositories configured. Add one to browse available plugins.",
        ["web.admin.plugins.repositories.enable"] = "Enable",
        ["web.admin.plugins.repositories.heading"] = "Repositories",
        ["web.admin.plugins.repositories.loadFailed"] = "Failed to load plugin repositories.",
        ["web.admin.plugins.repositories.managed"] = "Managed by Silo",
        ["web.admin.plugins.repositories.name"] = "Repository name",
        ["web.admin.plugins.repositories.remove"] = "Remove {name}",
        ["web.admin.plugins.repositories.submit"] = "Add",
        ["web.admin.plugins.repositories.url"] = "Repository URL",
        ["web.admin.plugins.settings.defaultSchedule"] = "Default schedule: {schedule}",
        ["web.admin.plugins.settings.noneInstalled"] = "This plugin has no settings. It works as soon as it's installed and turned on.",
        ["web.admin.plugins.settings.nonePreview"] = "This plugin has no settings. It works as soon as it's installed.",
        ["web.admin.plugins.settings.offerSignIn"] = "Offer {label} on the sign-in screen",
        ["web.admin.plugins.settings.previewDescription"] = "You can change these after installing.",
        ["web.admin.plugins.settings.required"] = "Required",
        ["web.admin.plugins.settings.runTask"] = "Run {label}",
        ["web.admin.plugins.settings.scheduleLink"] = "Schedule and history",
        ["web.admin.plugins.settings.signInDescription"] = "Changes apply at once. Test the connection and choose the sign-in options on Settings → Sign-in.",
        ["web.admin.plugins.settings.signInTitle"] = "Sign-in",
        ["web.admin.plugins.settings.tasksDescription"] = "Turning a task on or off applies without a restart, though other servers can take a moment to pick it up. Once a task is on, change its schedule on its task page.",
        ["web.admin.plugins.settings.tasksTitle"] = "Scheduled tasks",
        ["web.admin.plugins.settings.title"] = "Settings",
        ["web.admin.plugins.signature.invalid"] = "Failed the check",
        ["web.admin.plugins.signature.invalidBanner"] = "<b>The release signature check failed for {name}.</b> The signature that came with this version doesn't prove which repository built it. The server log has the details.",
        ["web.admin.plugins.signature.label"] = "Release signature",
        ["web.admin.plugins.signature.tileInvalid"] = "Signature failed",
        ["web.admin.plugins.signature.tileVerified"] = "Signed release built from {repository}",
        ["web.admin.plugins.signature.unknown"] = "Not checked",
        ["web.admin.plugins.signature.unsigned"] = "Not signed",
        ["web.admin.plugins.signature.verified"] = "Built from <link>{repository}</link>",
        ["web.admin.plugins.signature.verifiedAt"] = "Verified {date}",
        ["web.admin.plugins.signature.verifiedNoRepository"] = "Verified",
        ["web.admin.plugins.source.approvedCommunity"] = "Approved community",
        ["web.admin.plugins.source.approvedCommunityNotice"] = "Reviewed by Silo maintainers to work as described and be safe for its documented use. Its author maintains and supports it, not the Silo project.",
        ["web.admin.plugins.source.external"] = "External source",
        ["web.admin.plugins.source.externalNotice"] = "Silo has not reviewed this plugin. It comes from a repository you added, so only run it if you trust its source.",
        ["web.admin.plugins.source.silo"] = "Silo maintained",
        ["web.admin.plugins.source.unverified"] = "Unverified",
        ["web.admin.plugins.source.unverifiedNotice"] = "It isn't linked to any of your catalogs, so Silo can't verify it or check it for updates. Only run it if you trust its source. To update it, upload the new version.",
        ["web.admin.plugins.status.missing"] = "Missing: {settings}",
        ["web.admin.plugins.status.needsSetup"] = "Needs setup",
        ["web.admin.plugins.statusIndicator.active"] = "Active",
        ["web.admin.plugins.statusIndicator.failed"] = "Failed",
        ["web.admin.plugins.statusIndicator.off"] = "Off",
        ["web.admin.plugins.statusIndicator.restarting"] = "Restarting ({restart_count})",
        ["web.admin.plugins.statusIndicator.running"] = "Running",
        ["web.admin.plugins.statusIndicator.starting"] = "Starting",
        ["web.admin.plugins.statusIndicator.stopped"] = "Stopped",
        ["web.admin.plugins.tile.catalogSubline"] = "by {publisher}, {version}",
        ["web.admin.plugins.tile.confirmDescription"] = "{source}. {notice}",
        ["web.admin.plugins.tile.confirmInstall"] = "Install plugin",
        ["web.admin.plugins.tile.confirmTitle"] = "Install {name}?",
        ["web.admin.plugins.tile.install"] = "Install",
        ["web.admin.plugins.tile.installLabel"] = "Install {name}",
        ["web.admin.plugins.tile.installed"] = "Installed",
        ["web.admin.plugins.tile.installedSubline"] = "{version}, {source}",
        ["web.admin.plugins.tile.installing"] = "Installing…",
        ["web.admin.plugins.tile.noDescription"] = "No description provided.",
        ["web.admin.plugins.tile.updateAvailable"] = "{version} available",
        ["web.admin.plugins.tile.updateFailed"] = "Update to {version} failed",
        ["web.admin.plugins.tile.updatesPaused"] = "Updates paused",
        ["web.admin.plugins.upload.choose"] = "Choose plugin file...",
        ["web.admin.plugins.upload.description"] = "Upload a plugin file for a plugin that isn't in a repository.",
        ["web.admin.plugins.upload.fileLabel"] = "Plugin file",
        ["web.admin.plugins.upload.heading"] = "Install from a file",
        ["web.admin.plugins.upload.progress"] = "Plugin upload progress",
        ["web.admin.plugins.upload.submit"] = "Upload",
        ["web.admin.plugins.upload.uploading"] = "Uploading...",
        ["web.admin.common.connectionCheck.check"] = "Check Connection",
        ["web.admin.common.connectionCheck.checking"] = "Checking...",
        ["web.admin.errors.catalogRevisionMissing"] = "Catalog revision unavailable. Reload before editing.",
        ["web.admin.tasks.triggerText.daily"] = "Daily at {time}",
        ["web.admin.tasks.triggerText.everyDays"] = "{count, plural, one{Every {count_text} day} other{Every {count_text} day(s)}}",
        ["web.admin.tasks.triggerText.everyDaysShort"] = "{count, plural, one{Every {count_text}d} other{Every {count_text}d}}",
        ["web.admin.tasks.triggerText.everyHours"] = "{count, plural, one{Every {count_text} hour} other{Every {count_text} hour(s)}}",
        ["web.admin.tasks.triggerText.everyHoursShort"] = "{count, plural, one{Every {count_text}h} other{Every {count_text}h}}",
        ["web.admin.tasks.triggerText.everyMilliseconds"] = "{count, plural, one{Every {count_text} millisecond} other{Every {count_text} millisecond(s)}}",
        ["web.admin.tasks.triggerText.everyMillisecondsShort"] = "{count, plural, one{Every {count_text}ms} other{Every {count_text}ms}}",
        ["web.admin.tasks.triggerText.everyMinutes"] = "{count, plural, one{Every {count_text} minute} other{Every {count_text} minute(s)}}",
        ["web.admin.tasks.triggerText.everyMinutesShort"] = "{count, plural, one{Every {count_text}m} other{Every {count_text}m}}",
        ["web.admin.tasks.triggerText.everySeconds"] = "{count, plural, one{Every {count_text} second} other{Every {count_text} second(s)}}",
        ["web.admin.tasks.triggerText.everySecondsShort"] = "{count, plural, one{Every {count_text}s} other{Every {count_text}s}}",
        ["web.admin.tasks.triggerText.startup"] = "On server startup",
        ["web.admin.tasks.triggerText.startupShort"] = "On startup",
        ["web.admin.tasks.triggerText.weekly"] = "{day, select, 0{Sunday} 1{Monday} 2{Tuesday} 3{Wednesday} 4{Thursday} 5{Friday} 6{Saturday} other{Day {day}}} at {time}",
        ["web.admin.tasks.triggerText.weeklyShort"] = "{day, select, 0{Sun} 1{Mon} 2{Tue} 3{Wed} 4{Thu} 5{Fri} 6{Sat} other{Day {day}}} at {time}",
    };

    /// <summary>Every key this table owns, for catalog-coverage tests.</summary>
    public static IReadOnlyCollection<string> Keys { get; } = English.Keys.ToArray();

    public static bool Has(string key) => English.ContainsKey(key);

    /// <summary>The unformatted English pattern.</summary>
    public static string Pattern(string key) => English.TryGetValue(key, out var pattern) ? pattern : key;

    public static string T(string key, object? arguments = null)
    {
        var values = ToValues(arguments);
        return English.TryGetValue(key, out var pattern) ? AdminText.Format(pattern, values) : AppStrings.Get(key, values);
    }

    /// <summary>A count in the shape the catalog's plural messages expect.</summary>
    public static Dictionary<string, object?> Count(long count, params (string Name, object? Value)[] more)
    {
        var values = new Dictionary<string, object?> { ["count"] = count, ["count_text"] = AdminText.FormatNumber(count) };
        foreach (var (name, value) in more) values[name] = value;
        return values;
    }

    // Markers stand in for the markup tags while a message is formatted, so a plugin-supplied name or reason that
    // contains angle brackets can never become bold text or a link.
    private const char BoldOn = '\uE000', BoldOff = '\uE001', LinkOn = '\uE002', LinkOff = '\uE003';
    /// <summary>The longest single value a message takes; callers bound names, versions and reasons tighter before this.</summary>
    private const int ValueLimit = 2000;

    /// <summary>A message with &lt;b&gt; and &lt;link&gt; markup as runs. Values are inserted as plain text.</summary>
    public static IReadOnlyList<AdminPluginsTextRun> Runs(string key, object? arguments = null)
    {
        var pattern = Pattern(key).Replace("<b>", BoldOn.ToString()).Replace("</b>", BoldOff.ToString())
            .Replace("<link>", LinkOn.ToString()).Replace("</link>", LinkOff.ToString());
        var values = ToValues(arguments);
        foreach (var pair in values.ToArray())
            if (pair.Value is string text) values[pair.Key] = StripMarkers(text);
        var formatted = AdminText.Format(pattern, values);
        var runs = new List<AdminPluginsTextRun>();
        bool bold = false, link = false;
        var buffer = new System.Text.StringBuilder();
        void Flush() { if (buffer.Length > 0) { runs.Add(new(buffer.ToString(), bold, link)); buffer.Clear(); } }
        foreach (var c in formatted)
        {
            switch (c)
            {
                case BoldOn: Flush(); bold = true; break;
                case BoldOff: Flush(); bold = false; break;
                case LinkOn: Flush(); link = true; break;
                case LinkOff: Flush(); link = false; break;
                default: buffer.Append(c); break;
            }
        }
        Flush();
        return runs;
    }

    /// <summary>The same message as plain text, markup removed.</summary>
    public static string Plain(string key, object? arguments = null) => string.Concat(Runs(key, arguments).Select(run => run.Text));

    private static string StripMarkers(string text) => new(text.Where(c => c is not (BoldOn or BoldOff or LinkOn or LinkOff)).ToArray());

    private static Dictionary<string, object?> ToValues(object? arguments)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (arguments is IReadOnlyDictionary<string, object?> ready) foreach (var pair in ready) values[pair.Key] = pair.Value;
        else if (arguments is IDictionary<string, object?> dictionary) foreach (var pair in dictionary) values[pair.Key] = pair.Value;
        else if (arguments != null) foreach (var property in arguments.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) values[property.Name] = property.GetValue(arguments);
        // Every value a message shows may come from a plugin, a publisher, a repository or the server. None of them can bring a
        // line break or a direction control into the message, so no value can stand as a statement of the page's own.
        foreach (var pair in values.ToArray())
            if (pair.Value is string text) values[pair.Key] = AdminPluginPresentation.Clean(text, ValueLimit);
        return values;
    }
}