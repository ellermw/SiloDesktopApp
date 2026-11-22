using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Tests.HiddenChangesWire;

namespace SiloPlayer.Tests;

/// <summary>The Downloads limit memory: official LimitField remembers a limit only at the moment Unlimited is checked. Synthetic values only.</summary>
public sealed class AdminLimitMemoryTests
{
    // ---------------------------------------------------------------- Downloads: the remembered limit (official LimitField)

    [Fact]
    public void CheckingUnlimitedWithALimitThenUncheckingRestoresIt()
    {
        var memory = new AdminLimitMemory();
        Assert.Equal("0", memory.Check("1000000000"));
        Assert.Equal("1000000000", memory.Uncheck());
    }

    [Fact]
    public void AMemoryThatSawNoCheckRestoresAnEmptyBox()
    {
        var memory = new AdminLimitMemory();
        Assert.Equal("", memory.Uncheck());
        Assert.Equal("0", memory.Check("0"));
        Assert.Equal("", memory.Uncheck());
        Assert.Equal("0", memory.Check("  0 "));
        Assert.Equal("", memory.Uncheck());
        Assert.Equal("0", memory.Check(""));
        Assert.Equal("", memory.Uncheck());
    }

    [Fact]
    public void ReopeningThePageForgetsTheLimit()
    {
        var memory = new AdminLimitMemory();
        memory.Check("250"); memory.Reset();
        Assert.Equal("", memory.Uncheck());
    }

    private const string DownloadEffective = """{"download.enabled":"true","download.server_bandwidth_mbps":"0","download.user_bandwidth_mbps":"50","download.artifact_max_bytes":"53687091200"}""";

    private sealed class DownloadHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminDownloadSettingsController Controller;
        internal DownloadHarness()
        {
            Authority = new(Kit.Client, Kit.Auth); var api = new AdminServerSettingsApi(Kit.Client);
            Store = new(api, Authority, AdminDownloadSettingsController.Keys.Concat(AdminDownloadSettingsController.ReadOnlyKeys), AdminDownloadSettingsController.Secrets);
            Controller = new(api, Store, Authority);
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) return Task.FromResult(SavedAck());
                var path = request.RequestUri!.AbsolutePath;
                var response = AdminMonitorKit.Json(path.EndsWith("effective") ? DownloadEffective : path.EndsWith("sensitive-status") ? """{"configured":[],"managed_by_env":[]}""" : """{"keys":[],"prefixes":[]}""");
                response.Headers.ETag = new("\"one\""); return Task.FromResult(response);
            };
        }
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private const string Bandwidth = "download.server_bandwidth_mbps";

    [Fact]
    public async Task TypeDiscardThenUncheckUnlimitedLeavesTheBoxEmptyAsTheOfficialFieldDoes()
    {
        using var h = new DownloadHarness(); await h.Controller.OpenAsync();
        var memory = new AdminLimitMemory();
        // Saved Unlimited (0). The admin unchecks it, types 250, discards, and unchecks Unlimited again.
        h.Controller.Set(Bandwidth, memory.Uncheck());
        h.Controller.Set(Bandwidth, "250");
        Assert.Equal("250", h.Controller.State.Values[Bandwidth]);
        h.Controller.Discard();
        Assert.Equal("0", h.Controller.State.Values[Bandwidth]);
        h.Controller.Set(Bandwidth, memory.Uncheck());
        Assert.Equal("", h.Controller.State.Values[Bandwidth]);
    }

    [Fact]
    public async Task ASavedLimitCheckedSavedThenUncheckedComesBack()
    {
        using var h = new DownloadHarness(); await h.Controller.OpenAsync();
        var memory = new AdminLimitMemory();
        const string key = "download.user_bandwidth_mbps";
        h.Controller.Set(key, memory.Check(h.Controller.State.Values[key]));
        await h.Controller.SaveAsync();
        Assert.Equal("0", h.Controller.State.PersistedValues[key]);
        h.Controller.Set(key, memory.Uncheck());
        Assert.Equal("50", h.Controller.State.Values[key]);
        Assert.Contains(key, h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task CheckingUnlimitedOverATypedDraftRemembersTheDraftAsTheOfficialFieldDoes()
    {
        using var h = new DownloadHarness(); await h.Controller.OpenAsync();
        var memory = new AdminLimitMemory();
        h.Controller.Set(Bandwidth, "300");
        h.Controller.Set(Bandwidth, memory.Check(h.Controller.State.Values[Bandwidth]));
        h.Controller.Set(Bandwidth, memory.Uncheck());
        Assert.Equal("300", h.Controller.State.Values[Bandwidth]);
    }
}
