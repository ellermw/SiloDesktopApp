using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Player;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class SubtitlePreferencesNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        try
        {
            foreach (var revision in new[] { 19, 18 })
            {
                using var handler = new Handler(revision);
                using var http = new HttpClient(handler);
                var client = new SiloApiClient(http); client.SetBaseUrl("https://subtitle-native.invalid"); client.SetProfile("viewer");
                var api = new SettingsApi(client);
                var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "subtitle-" + revision));
                var auth = new AuthService(client, new AuthApi(client));
                var theme = new ThemeService(settings, api);
                var vm = new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client), new HistoryImportApi(client),
                    new WatchProvidersApi(client), auth, theme, settings, new AccessibilityService(settings, theme));
                using var player = new PlayerService(new PlaybackApi(client), new CatalogApi(client), auth, client, settings, api);
                using var services = new ServiceCollection().AddSingleton(client).AddSingleton(api).AddSingleton(settings)
                    .AddSingleton(auth).AddSingleton(vm).AddSingleton(theme).AddSingleton(player)
                    .AddSingleton(new CardOverlayService(api)).AddSingleton(new UICustomizationService(api))
                    .AddSingleton(new ToastService()).BuildServiceProvider();
                field.SetValue(null, services);
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_FINAL_CASE") is { Length: > 0 } finalCase)
                {
                    await CheckFinalAsync(parent, player, vm, client, handler, finalCase);
                    continue;
                }
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_DURABILITY_CASE") is { Length: > 0 } durabilityCase)
                {
                    await CheckOffsetDurabilityAsync(player, handler, durabilityCase);
                    continue;
                }
                var page = new SettingsPage { Width = 900, Height = 900 };
                parent.Children.Add(page);
                try
                {
                    await InvokeAsync(vm, "LoadSubtitleAppearanceAsync");
                    vm.SubFontColor = "#facc15";
                    await vm.SaveSubtitleAppearanceCommand.ExecuteAsync(null);
                    Require(handler.Writes.Take(2).SequenceEqual(new[] { "PUT playback.subtitle_appearance profile", "DELETE playback.subtitle_appearance profile_device" }), "Settings saves profile appearance and clears source-only shadow");
                    handler.Writes.Clear();
                    Invoke(page, "SubtitleLater_Click", page.FindName("SubtitleLaterButton"), new RoutedEventArgs());
                    await WaitAsync(() => handler.Writes.Count > 0 && !Get<bool>(vm, "IsSavingSubtitlePreferences"));
                    Require(handler.Offset == 550, "native Settings nudge uses loaded offset");
                    Require(handler.Writes[0] == "PUT player.subtitle_sync_ms " + (revision == 19 ? "profile" : "profile_device"), "Settings uses supported timing scope");
                    var dialog = new SubtitleAppearanceDialog(player.ActiveAuthority) { XamlRoot = parent.XamlRoot };
                    handler.Writes.Clear();
                    var showing = dialog.ShowAsync();
                    await Task.Delay(650);
                    var unsolicitedOpenWrites = handler.Writes.Count;
                    handler.Writes.Clear();
                    var state = (SiloPlayer.Core.Models.Settings.SubtitleAppearance)dialog.GetType().GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                    state.FontColor = "#22c55e";
                    Invoke(dialog, "ScheduleSave");
                    await Task.Delay(650);
                    Require(handler.Writes.Any(row => row == "PUT playback.subtitle_appearance profile"), "player dialog shares profile writer");
                    handler.FailWrites = true;
                    state.FontColor = "#facc15"; Invoke(dialog, "ScheduleSave"); await Task.Delay(650);
                    Require(Field<bool>(dialog, "_savePending") && ((TextBlock)dialog.FindName("SaveStatusText")).Text.StartsWith("Failed"), "dialog retains failed save and exposes retry");
                    handler.FailWrites = false;
                    Invoke(dialog, "Retry_Click", dialog.FindName("RetryButton"), new RoutedEventArgs());
                    await WaitAsync(() => !Field<bool>(dialog, "_savePending"));
                    handler.Writes.Clear();
                    Invoke(dialog, "Reset_Click", dialog.FindName("ResetButton"), new RoutedEventArgs());
                    await Task.Delay(650);
                    Require(unsolicitedOpenWrites == 0 && handler.Writes.All(row => !row.StartsWith("PUT ")),
                        $"opening/resetting dialog must not save resolved values (opening writes={unsolicitedOpenWrites}, reset writes={string.Join(",", handler.Writes)})");
                    dialog.Hide(); await showing;
                    var overrideWrites = 0;
                    var explicitDialog = new SubtitleAppearanceDialog { XamlRoot = parent.XamlRoot, ApplyToLocalPlayer = false,
                        InitialValue = "{}", SaveOverrideAsync = _ => { overrideWrites++; return Task.CompletedTask; } };
                    var explicitShowing = explicitDialog.ShowAsync(); await Task.Delay(120);
                    handler.Writes.Clear(); Invoke(explicitDialog, "ScheduleSave"); await Task.Delay(550);
                    Require(overrideWrites == 1 && handler.Writes.Count == 0, "explicit device editor keeps callback");
                    explicitDialog.Hide(); await explicitShowing;
                    await CheckPlayerAsync(player, client, api, handler, revision);
                    Invoke(page, "SyncSubtitleAppearanceControls");
                    Invoke(page, "ShowSettingsDetail", page.FindName("SubtitlesTab"));
                    ((StackPanel)page.FindName("SubtitlesPanel")).Visibility = Visibility.Visible;
                    foreach (var width in new[] { 460, 1280, 2560, 3840 })
                    {
                        page.Width = width;
                        page.Measure(new Windows.Foundation.Size(width, 1080));
                        page.Arrange(new Windows.Foundation.Rect(0, 0, width, 1080)); page.UpdateLayout();
                        var timing = (FrameworkElement)((FrameworkElement)page.FindName("SubtitleTimingDescription")).Parent;
                        await CaptureAsync(timing, $"timing-{revision}-{width}.png");
                        Require(((Button)page.FindName("SubtitleLaterButton")).ActualWidth > 0, "visible timing controls");
                    }
                    Program.Log($"PASS: subtitle preferences revision {revision}: actual Settings/profile appearance + timing, player dialog, explicit device callbacks.");
                }
                finally { parent.Children.Remove(page); await Task.Delay(150); }
            }
        }
        finally { field.SetValue(null, original); }
    }

    private static async Task CheckFinalAsync(StackPanel parent, PlayerService player, SettingsViewModel vm, SiloApiClient client, Handler handler, string mode)
    {
        if (mode.StartsWith("cross-"))
        {
            player.GetType().GetProperty("State")!.SetValue(player, PlayerState.Expanded);
            await InvokeAsync(player, "EnsureSubtitleAppearanceLoadedAsync", CancellationToken.None);
            await vm.ReloadSubtitlePreferencesAsync(); handler.Writes.Clear();
            var debounce = mode.Contains("debounce"); handler.HoldOffset = !debounce;
            handler.FailHeldOffset = mode.Contains("failure");
            var first = player.SetSubtitleOffsetAsync(600, immediate: !debounce);
            if (!debounce) await handler.OffsetEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var second = vm.SetSubtitleOffsetAsync(900, reset: mode.Contains("reset"));
            await Task.Delay(debounce ? 750 : 100);
            var before = handler.Writes.Count;
            if (mode.Contains("owner")) client.SetProfile("replacement");
            handler.OffsetRelease.TrySetResult(); await Task.WhenAll(first, second); await Task.Delay(100);
            if (mode.Contains("owner")) Require(before == 1 && handler.Writes.Count == 1, "retired cross-surface queued owner dispatches neither Settings nor cleanup");
            else
            {
                var expected = mode.Contains("reset") ? 0 : 900;
                Require((debounce || before == 1) && handler.Offset == expected && vm.SubtitleOffsetMs == expected && player.SubtitleOffsetMs == expected,
                    $"shared durable ordering preserves newest Settings intent in server and both actual callers ({mode}, before={before}, server={handler.Offset}, vm={vm.SubtitleOffsetMs}, player={player.SubtitleOffsetMs})");
                Require(Field<int?>(player, "_subtitleOffsetDraft") == null, "superseded player draft clears without restoring its old value");
            }
            Program.Log($"PASS: final subtitle {mode}, revision={handler.Revision}: actual PlayerService and SettingsViewModel same shared API."); return;
        }
        if (mode.StartsWith("player-"))
        {
            if (mode is "player-stale-load" or "player-canceled-load")
            {
                handler.TimeoutPath = "capabilities"; handler.HoldTimeout = true;
                using var canceled = new CancellationTokenSource();
                var load = InvokeAsync(player, "EnsureSubtitleAppearanceLoadedAsync", canceled.Token);
                await handler.OffsetEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (mode == "player-stale-load") client.SetProfile("replacement"); else canceled.Cancel();
                handler.OffsetRelease.TrySetResult(); await load;
                Require(player.SubtitlePreferenceError == null && !player.SubtitlePreferencesLoaded, "retired or actually canceled load remains silent");
                Program.Log($"PASS: final subtitle {mode}, revision={handler.Revision}."); return;
            }
            if (mode == "player-load-cap") handler.TimeoutPath = "capabilities";
            if (mode == "player-load-effective") handler.TimeoutPath = "effective";
            await InvokeAsync(player, "EnsureSubtitleAppearanceLoadedAsync", CancellationToken.None);
            if (mode.StartsWith("player-load")) Require(!player.SubtitlePreferencesLoaded && player.SubtitlePreferenceError?.Contains("retry") == true, "current player load timeout is visible and retryable");
            else
            {
                handler.TimeoutPath = "player.subtitle_sync_ms";
                if (mode is "player-carried-timeout" or "player-stale-write") handler.HoldTimeout = true;
                var writing = player.SetSubtitleOffsetAsync(600, immediate: true);
                if (handler.HoldTimeout)
                {
                    await handler.OffsetEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    if (mode == "player-stale-write") client.SetProfile("replacement");
                    else
                    {
                        Field(player, "_playRequestGeneration", Field<long>(player, "_playRequestGeneration") + 1);
                        Invoke(player, "ResetSubtitlePreferenceSession");
                        await InvokeAsync(player, "EnsureSubtitleAppearanceLoadedAsync", CancellationToken.None);
                    }
                    handler.OffsetRelease.TrySetResult();
                }
                await writing;
                if (mode == "player-stale-write")
                {
                    Require(player.SubtitlePreferenceError == null, "retired held timeout does not publish failure to new owner");
                    Program.Log($"PASS: final subtitle {mode}, revision={handler.Revision}."); return;
                }
                Require(player.SubtitlePreferenceError?.Contains("retry") == true && player.SubtitleOffsetMs == 600, "current offset timeout retains draft with visible retry instead of Saving");
                handler.TimeoutPath = null; await player.SetSubtitleOffsetAsync(700, immediate: true);
                Require(player.SubtitlePreferenceError == null && handler.Offset == 700, "adjust-again offset retry persists");
            }
            Program.Log($"PASS: final subtitle {mode}, revision={handler.Revision}."); return;
        }
        if (mode.StartsWith("settings-"))
        {
            if (mode == "settings-load") handler.TimeoutPath = "capabilities";
            await vm.ReloadSubtitlePreferencesAsync();
            if (mode == "settings-load") Require(!vm.SubtitlePreferencesLoaded && vm.ErrorMessage?.Contains("Could not load") == true, "Settings load timeout explains unavailable controls");
            else
            {
                handler.TimeoutPath = mode.Contains("appearance") ? "playback.subtitle_appearance" : "player.subtitle_sync_ms";
                if (mode == "settings-appearance-save") await vm.SaveSubtitleAppearanceCommand.ExecuteAsync(null);
                else if (mode == "settings-appearance-reset") await vm.ResetSubtitleAppearanceCommand.ExecuteAsync(null);
                else await vm.SetSubtitleOffsetAsync(900, reset: mode.Contains("reset"));
                Require(!vm.IsSavingSubtitlePreferences && vm.ErrorMessage?.StartsWith("Failed") == true, "Settings current write timeout reports failure and releases saving");
            }
            Program.Log($"PASS: final subtitle {mode}, revision={handler.Revision}."); return;
        }
        var dialog = new SubtitleAppearanceDialog(player.ActiveAuthority) { XamlRoot = parent.XamlRoot };
        if (mode == "dialog-load") handler.TimeoutPath = "capabilities";
        var showing = dialog.ShowAsync().AsTask();
        try
        {
            await Task.Delay(180);
            var status = (TextBlock)dialog.FindName("SaveStatusText");
            if (mode == "dialog-load")
            {
                Require(status.Text.StartsWith("Failed") && ((Button)dialog.FindName("RetryButton")).Visibility == Visibility.Visible, "dialog load timeout exposes Retry");
                handler.TimeoutPath = null; Invoke(dialog, "Retry_Click", dialog.FindName("RetryButton"), new RoutedEventArgs());
                await WaitAsync(() => Field<bool>(dialog, "_loaded"));
            }
            else if (mode.StartsWith("dialog-reset"))
            {
                if (mode.Contains("draft")) Invoke(dialog, "ScheduleSave");
                if (mode == "dialog-reset-timeout") handler.TimeoutPath = "playback.subtitle_appearance";
                else handler.FailWrites = true;
                Invoke(dialog, "Reset_Click", dialog.FindName("ResetButton"), new RoutedEventArgs());
                await WaitAsync(() => status.Text.StartsWith("Failed"));
                handler.FailWrites = false; handler.TimeoutPath = null; handler.Writes.Clear();
                Invoke(dialog, "Retry_Click", dialog.FindName("RetryButton"), new RoutedEventArgs());
                await Task.Delay(650);
                Require(handler.Writes.Count == 2 && handler.Writes.All(row => row.StartsWith("DELETE ")) && handler.DefaultAppearance && !Field<bool>(dialog, "_savePending"), "failed Reset Retry must DELETE defaults with and without old draft, never PUT/no-op");
            }
            else
            {
                handler.TimeoutPath = "playback.subtitle_appearance"; Invoke(dialog, "ScheduleSave");
                dialog.Hide(); await Task.Delay(180);
                Require(!showing.IsCompleted && Field<bool>(dialog, "_savePending") && status.Text.StartsWith("Failed"), "current appearance PUT timeout prevents close and retains failed draft");
                handler.TimeoutPath = null; Invoke(dialog, "Retry_Click", dialog.FindName("RetryButton"), new RoutedEventArgs());
                await WaitAsync(() => !Field<bool>(dialog, "_savePending"));
            }
            Program.Log($"PASS: final subtitle {mode}, revision={handler.Revision}: actual native dialog failure and Retry.");
        }
        finally { handler.TimeoutPath = null; handler.FailWrites = false; dialog.Hide(); await showing; await Task.Delay(80); }
    }

    private static async Task CheckOffsetDurabilityAsync(PlayerService player, Handler handler, string mode)
    {
        await InvokeAsync(player, "EnsureSubtitleAppearanceLoadedAsync", CancellationToken.None);
        handler.Writes.Clear(); handler.HoldOffset = true; handler.CommitAfterCancellation = mode == "order";
        handler.FailHeldOffset = mode == "failure";
        var first = player.SetSubtitleOffsetAsync(600, immediate: mode is not ("reset" or "failure"));
        var flush = mode is "reset" or "failure" ? InvokeAsync(player, "FlushSubtitleOffsetAsync") : Task.CompletedTask;
        await handler.OffsetEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = Task.CompletedTask;
        try
        {
            if (mode is "reset" or "failure")
            {
                Field(player, "_playRequestGeneration", Field<long>(player, "_playRequestGeneration") + 1);
                Invoke(player, "ResetSubtitlePreferenceSession");
                if (mode == "failure") await InvokeAsync(player, "EnsureSubtitleAppearanceLoadedAsync", CancellationToken.None);
                var statusChanges = 0;
                player.SubtitlePreferencesChanged += () => statusChanges++;
                await Task.Delay(100);
                handler.OffsetRelease.TrySetResult(); await Task.WhenAll(first, flush);
                if (mode == "failure") Require(player.SubtitlePreferenceError != null && statusChanges > 0 && player.SubtitleOffsetMs == 600,
                    "carried draft failure reports retryable error after the next session loads");
                else Require(handler.Offset == 600, "same-owner next-session reset must preserve an already-dispatched profile save");
            }
            else
            {
                second = player.SetSubtitleOffsetAsync(700, immediate: true);
                if (mode == "owner") player.ActiveAuthority.ApiClient.SetProfile("replacement");
                await Task.Delay(100);
                var beforeRelease = handler.Writes.Count(row => row.StartsWith("PUT player.subtitle_sync_ms"));
                handler.OffsetRelease.TrySetResult(); await Task.WhenAll(first, second); await Task.Delay(50);
                if (mode == "owner") Require(beforeRelease == 1 && handler.Writes.Count == 1, "retired profile cannot dispatch queued value or replacement-device cleanup");
                else Require(beforeRelease == 1 && handler.Offset == 700, $"dispatched saves remain serial despite new nudges (before release={beforeRelease}, final={handler.Offset})");
            }
            Program.Log($"PASS: native subtitle durable write {mode}: held real HTTP preference dispatch and owner/session lifecycle.");
        }
        finally { handler.OffsetRelease.TrySetResult(); await Task.WhenAll(first, second, flush); }
    }

    private static async Task CheckPlayerAsync(PlayerService player, SiloApiClient local, SettingsApi api, Handler handler, int revision)
    {
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Field(player, "_mpv", mpv);
        var localAuthority = player.ActiveAuthority;
        await InvokeAsync(player, "EnsureSubtitleAppearanceLoadedAsync", CancellationToken.None);
        Invoke(player, "PushResolvedSubtitlePreferences");
        Require(Math.Abs(mpv.GetPropertyDouble("sub-delay") - .55) < .001, "resolved positive offset applied to real mpv");
        handler.Writes.Clear();
        var first = player.SetSubtitleOffsetAsync(600); var second = player.SetSubtitleOffsetAsync(700);
        await Task.WhenAll(first, second);
        Require(handler.Writes.Count(row => row.StartsWith("PUT player.subtitle_sync_ms")) == 1 && handler.Offset == 700, "rapid native nudges debounce into one durable write");
        using var remoteHandler = new Handler(revision) { Offset = -750 };
        using var remoteHttp = new HttpClient(remoteHandler);
        var remote = new SiloApiClient(remoteHttp); remote.SetBaseUrl("https://remote-subtitle.invalid"); remote.SetProfile("viewer");
        using var remoteAuth = new AuthService(remote, new AuthApi(remote));
        var remoteAuthority = new PlaybackAuthorityScope(remote, remoteAuth);
        Field(player, "_activeAuthority", remoteAuthority);
        Field(player, "_playRequestGeneration", Field<long>(player, "_playRequestGeneration") + 1);
        Invoke(player, "ResetSubtitlePreferenceSession");
        await InvokeAsync(player, "EnsureSubtitleAppearanceLoadedAsync", CancellationToken.None);
        Invoke(player, "PushResolvedSubtitlePreferences");
        Require(Math.Abs(mpv.GetPropertyDouble("sub-delay") + .75) < .001, "same-profile remote owner resolves its negative offset");
        mpv.SetProperty("sub-font-size", "61");
        player.ApplySubtitleAppearance(new() { FontColor = "#00ff00" });
        Require(Math.Abs(mpv.GetPropertyDouble("sub-font-size") - 61) < .001, "local Settings cannot recolor remote playback");
        player.GetType().GetProperty("State")!.SetValue(player, PlayerState.Expanded);
        await player.ApplySiloCastControlAsync(JsonNode.Parse("{\"name\":\"set_subtitle_sync_ms\",\"milliseconds\":120000}")!.AsObject());
        await InvokeAsync(player, "EnsureSubtitleAppearanceLoadedAsync", CancellationToken.None);
        Invoke(player, "PushResolvedSubtitlePreferences");
        Require(Math.Abs(mpv.GetPropertyDouble("sub-delay") - 120) < .001 && remoteHandler.Writes.Count == 0, "SiloCast session offset keeps protocol bounds and survives preference refresh without persistence");
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply = (Task)player.GetType().GetMethod("ApplySubtitleAppearanceWhenLoadedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(player, [hold.Task, remoteAuthority, remote.CaptureContext(), Field<long>(player, "_playRequestGeneration")])!;
        Field(player, "_activeAuthority", localAuthority);
        Field(player, "_playRequestGeneration", Field<long>(player, "_playRequestGeneration") + 1);
        mpv.SetProperty("sub-delay", "1.234"); var sentinel = mpv.GetPropertyDouble("sub-delay"); hold.SetResult(); await apply;
        Require(Math.Abs(mpv.GetPropertyDouble("sub-delay") - sentinel) < .001, "late load application cannot touch replacement owner mpv");
        Invoke(player, "ResetSubtitlePreferenceSession");
        Field(player, "_mpv", null);
        player.GetType().GetProperty("State")!.SetValue(player, PlayerState.Idle);
        Program.Log($"PASS: subtitle mpv revision {revision}: load ±offset, debounce, local/remote ownership, remote session override and stale final apply.");
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Field(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(50);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Require(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "timing panel layout");
        var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Program.ResultDirectory);
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }

    private static T Get<T>(object owner, string name) => (T)owner.GetType().GetProperty(name)!.GetValue(owner)!;
    private static void Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    private static async Task InvokeAsync(object owner, string name, params object?[] args) => await (Task)owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args)!;
    private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException("Subtitle preferences: " + name); }
    private static async Task WaitAsync(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException("Subtitle preferences native wait"); await Task.Delay(20); }
    }
    private sealed class Handler(int revision) : HttpMessageHandler
    {
        public int Revision => revision;
        public string? TimeoutPath;
        public bool HoldTimeout;
        public List<string> Writes { get; } = [];
        public int Offset = 450;
        private bool _profileOffset;
        public bool FailWrites;
        public bool DefaultAppearance;
        public bool HoldOffset, CommitAfterCancellation, FailHeldOffset;
        public TaskCompletionSource OffsetEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OffsetRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (TimeoutPath != null && path.EndsWith(TimeoutPath))
            {
                if (HoldTimeout) { OffsetEntered.TrySetResult(); await OffsetRelease.Task; }
                throw new TaskCanceledException("Synthetic HTTP timeout with uncanceled request token");
            }
            if (path.EndsWith("capabilities")) return Json(new { api_version = 1, manifest_revision = revision });
            if (path.EndsWith("effective")) return Json(new { items = new object[] {
                new { key = "playback.subtitle_appearance", source = DefaultAppearance ? "default" : "profile_device", value = (object)(DefaultAppearance
                    ? new { fontColor = "#ffffff", textOpacity = 100, textOutline = false, backgroundOpacity = 75 }
                    : new { fontColor = "#ffffff", textOpacity = 67, textOutline = true, backgroundOpacity = 60 }) },
                new { key = "player.subtitle_sync_ms", source = "profile_device", value = (object)Offset } } });
            if (path.Contains("/settings/values/") && request.Method != HttpMethod.Get)
            {
                var key = path.Split('/').Last();
                var scope = request.RequestUri.Query.Contains("profile_device") ? "profile_device" : "profile";
                Writes.Add(request.Method + " " + key + " " + scope);
                if (FailWrites) return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") };
                if (key == "playback.subtitle_appearance" && request.Method == HttpMethod.Delete && scope == "profile") DefaultAppearance = true;
                if (key == "player.subtitle_sync_ms" && request.Method == HttpMethod.Delete)
                {
                    if (scope == "profile") _profileOffset = false;
                    if (scope == "profile" || !_profileOffset) Offset = 0;
                }
                if (request.Method == HttpMethod.Put)
                {
                    if (key == "playback.subtitle_appearance") DefaultAppearance = false;
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    if (key == "player.subtitle_sync_ms")
                    {
                        if (scope == "profile") _profileOffset = true;
                        var next = body.RootElement.GetProperty("value").GetInt32();
                        if (HoldOffset)
                        {
                            HoldOffset = false; OffsetEntered.TrySetResult();
                            if (CommitAfterCancellation)
                            {
                                async Task CommitAsync() { await OffsetRelease.Task; Offset = next; }
                                await CommitAsync().WaitAsync(ct);
                            }
                            else
                            {
                                await OffsetRelease.Task.WaitAsync(ct);
                                if (FailHeldOffset) return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") };
                                Offset = next;
                            }
                        }
                        else Offset = next;
                    }
                    else Require(body.RootElement.GetProperty("value").ValueKind == JsonValueKind.Object, "appearance object payload");
                }
                return Json(new { });
            }
            return Json(new { items = Array.Empty<object>() });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
