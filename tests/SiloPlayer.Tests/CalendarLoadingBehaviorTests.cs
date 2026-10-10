using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CalendarLoadingBehaviorTests
{
    [Fact]
    public async Task OptionalLibraryLookupDoesNotBlockTheUsableWeek()
    {
        using var wire = new Wire { HoldLibraries = true };
        var vm = wire.Model(); var pending = vm.LoadCommand.ExecuteAsync(null);
        try
        {
            await wire.LibraryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(80);
            Assert.Single(vm.Days);
            Assert.False(vm.IsLoading);
            Assert.True(vm.HasLoaded);
        }
        finally { wire.Release.TrySetResult(); await pending; vm.CancelLoad(); }
    }

    [Fact]
    public async Task WeekResponseCannotBecomeVisibleInAnotherProfile()
    {
        using var wire = new Wire { HoldCalendar = true };
        var vm = wire.Model(); var pending = vm.LoadCommand.ExecuteAsync(null);
        try
        {
            await wire.CalendarStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            wire.Client!.SetProfile("other-profile");
            wire.Release.TrySetResult(); await pending;
            Assert.Empty(vm.Days);
            Assert.False(vm.HasLoaded);
            Assert.Null(vm.ErrorMessage);
        }
        finally { wire.Release.TrySetResult(); await pending; vm.CancelLoad(); }
    }

    [Fact]
    public async Task CancelingAnotherWeekDoesNotMakeTheOldCacheLookLoadedForIt()
    {
        using var wire = new Wire(); var vm = wire.Model();
        await vm.LoadCommand.ExecuteAsync(null);
        wire.HoldCalendar = true; wire.CalendarStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.NextWeekCommand.ExecuteAsync(null);
        try
        {
            await wire.CalendarStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            vm.CancelLoad(); await pending;
            Assert.False(vm.HasLoaded);
            Assert.False(vm.IsLoading);
        }
        finally { wire.Release.TrySetResult(); await pending; vm.CancelLoad(); }
    }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "silo-calendar-test-" + Guid.NewGuid());
        internal bool HoldLibraries, HoldCalendar;
        internal SiloApiClient? Client;
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource LibraryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CalendarStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HttpClient? _http;
        internal CalendarViewModel Model()
        {
            _http = new HttpClient(this, disposeHandler: false);
            Client = new(_http); Client.SetBaseUrl("https://calendar-loading.invalid"); Client.SetProfile("original");
            return new(new CatalogApi(Client), new SettingsService(_directory));
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "calendar-loading.invalid") throw new InvalidOperationException("Fixture external request.");
            var libraries = request.RequestUri.AbsolutePath.EndsWith("/libraries");
            if (libraries) { LibraryStarted.TrySetResult(); if (HoldLibraries) await Release.Task.WaitAsync(ct); }
            else { CalendarStarted.TrySetResult(); if (HoldCalendar) await Release.Task.WaitAsync(ct); }
            return new(HttpStatusCode.OK) { Content = new StringContent(libraries
                ? "{\"items\":[{\"id\":1,\"name\":\"Fixture library\"}]}"
                : "{\"events\":[{\"date\":\"2026-10-05\",\"items\":[{\"content_id\":\"fixture\",\"type\":\"movie\",\"title\":\"Fixture title\"}]}]}") };
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Release.TrySetResult(); _http?.Dispose(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
            base.Dispose(disposing);
        }
    }
}
