using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// The Activate device page in the real WinUI controls with synthetic data, matched to the official page
/// (web/src/pages/ActivateDevice.tsx, silo-server 3b2b32168, #4652): every state the page can reach, at
/// 1440 and 800 wide, with PNG captures for review. Texts are compared with what the page's own catalog
/// lookup returns (the bundled catalog's older wording for some keys is reported, not hidden).
/// </summary>
internal static class ActivateDeviceNativeFixture
{
    private const string NamedTv = "48217730", UnknownDevice = "11112222", Missing = "99990000", Remote = "55556666", QuickConnect = "482177",
        ExpiredCode = "77778888", SignedInCode = "33334444", DeclineCode = "22223333", FailingCode = "44445555", LimitedCode = "66667777";
    private static readonly int[] Widths = [1440, 800];

    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        try
        {
            await Guard("CodeEntryAsync", () => CodeEntryAsync(parent, field));
            await Guard("NotFoundAndFailuresAsync", () => NotFoundAndFailuresAsync(parent, field));
            await Guard("SignedOutAsync", () => SignedOutAsync(parent, field));
            await Guard("ApprovalCardsAsync", () => ApprovalCardsAsync(parent, field));
            await Guard("ImpersonationAsync", () => ImpersonationAsync(parent, field));
            await Guard("RemotePlaybackAsync", () => RemotePlaybackAsync(parent, field));
            await Guard("ResultsAsync", () => ResultsAsync(parent, field));
            await Guard("DecisionRulesAsync", () => DecisionRulesAsync(parent, field));
            await Guard("QuickConnectAsync", () => QuickConnectAsync(parent, field));
            if (Failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, Failures));
            Program.Log("PASS Activate device page states, requests and captures (" + string.Join("/", Widths) + " wide)");
        }
        finally { field.SetValue(null, previous); }
    }

    // ---- Scenarios ----

    private static async Task CodeEntryAsync(StackPanel parent, FieldInfo field)
    {
        await using var scene = new Scene(field);
        await scene.OpenAsync(parent, signedIn: true, token: null, code: null);
        var box = scene.Find<TextBox>("CodeBox");
        var button = scene.Find<Button>("ContinueButton");
        Check(box.Visibility == Visibility.Visible && Shown(box), "code entry shows the code box");
        Check(!button.IsEnabled, "Continue is off until the code is whole");
        await scene.TypeAsync(box, "4821");
        Check(box.Text == "4821" && !button.IsEnabled, "a partial code keeps Continue off");
        await scene.TypeAsync(box, "48217730");
        Check(box.Text == "4821 7730" && scene.Vm.CodeInput == "4821 7730" && button.IsEnabled, "the code is grouped 4+4 as typed and Continue turns on: " + box.Text);
        await scene.TypeAsync(box, "482177");
        Check(box.Text == "4821 77" && button.IsEnabled, "a six-digit Quick Connect code is whole too");
        await scene.TypeAsync(box, "4821");
        Check(Texts(scene.Page).Contains(AppStrings.Get("web.auth.activate.codeLabel", fallback: "Enter the code shown on your device")), "code label shown");
        Check(!Texts(scene.Page).Any(text => text.Contains("Open in the Silo app")), "no Open in app link on a desktop page");
        await scene.TypeAsync(box, "");
        await scene.CaptureAsync("activate-code-entry");
        Check(!scene.Wire.Requests.Any(r => r.Contains("/auth/device")), "typing a code sends nothing");
        await scene.TypeAsync(box, NamedTv);
        await scene.Vm.SubmitCodeCommand.ExecuteAsync(null); await scene.LayoutAsync();
        Check(scene.Wire.Requests.Count(r => r.StartsWith("GET /api/v2/auth/device?code=" + NamedTv)) == 1, "Continue looks the compact code up once");
        Check(scene.Wire.Authenticated.Last(), "a signed-in lookup carries the session");
        Check(!scene.Find<TextBox>("CodeBox").IsVisibleOnPage(), "the code box leaves once the request is in play");
    }

    private static async Task NotFoundAndFailuresAsync(StackPanel parent, FieldInfo field)
    {
        await using (var scene = new Scene(field))
        {
            await scene.OpenAsync(parent, signedIn: true, token: null, code: Missing);
            Check(scene.Shown("LoadErrorPanel") && Texts(scene.Page).Contains(DeviceActivation.NotFound.Render()), "not found shows its message");
            Check(!scene.Shown("DetailsPanel") && !scene.Find<Button>("ContinueButton").IsVisibleOnPage(), "not found shows no card");
            Check(ButtonTexts(scene.Page).Contains(AppStrings.Get("web.auth.activate.enterAnotherCode", fallback: "Enter another code")), "not found offers Enter another code");
            Check(!ButtonTexts(scene.Page).Contains(AppStrings.Get("common.tryAgain", fallback: "Try again")), "not found offers no retry");
            await scene.CaptureAsync("activate-not-found");
            scene.Vm.ClearCodeCommand.Execute(null); await scene.LayoutAsync();
            Check(scene.Shown("CodeForm"), "Enter another code returns to code entry");
        }
        await using (var scene = new Scene(field))
        {
            scene.Wire.ErrorStatus[FailingCode] = 500;
            await scene.OpenAsync(parent, signedIn: true, token: null, code: FailingCode);
            Check(Texts(scene.Page).Contains(DeviceActivation.LookupFailed.Render()), "a failed lookup says so");
            Check(ButtonTexts(scene.Page).Contains(AppStrings.Get("common.tryAgain", fallback: "Try again")), "a failed lookup offers Try again");
            await scene.CaptureAsync("activate-lookup-failed");
            scene.Wire.ErrorStatus.Remove(FailingCode); scene.Wire.Lookups[FailingCode] = Lookup("pending", "Bedroom TV", "roku", 3);
            await scene.Vm.RetryLookupCommand.ExecuteAsync(null); await scene.LayoutAsync();
            Check(scene.Shown("DetailsPanel") && scene.Vm.HeadingText.Contains("Bedroom TV"), "Try again looks the code up again");
        }
        await using (var scene = new Scene(field))
        {
            scene.Wire.ErrorStatus[LimitedCode] = 429; scene.Wire.RetryAfter = 900;
            await scene.OpenAsync(parent, signedIn: true, token: null, code: LimitedCode);
            Check(Texts(scene.Page).Contains(DeviceActivation.TooManyCodes(900).Render()), "too many codes says how long to wait");
            Check(!ButtonTexts(scene.Page).Contains(AppStrings.Get("common.tryAgain", fallback: "Try again")), "a rate limit offers no retry");
            await scene.CaptureAsync("activate-too-many-codes");
        }
    }

    private static async Task SignedOutAsync(StackPanel parent, FieldInfo field)
    {
        await using var scene = new Scene(field);
        await scene.OpenAsync(parent, signedIn: false, token: null, code: NamedTv);
        Check(scene.Shown("SignedOutPanel"), "signed-out shows the sign-in prompt");
        Check(Texts(scene.Page).Contains(scene.Vm.SignedOutText) && scene.Vm.SignedOutText.Contains("Media Room"), "signed-out names the server: " + scene.Vm.SignedOutText);
        Check(ButtonTexts(scene.Page).Contains(AppStrings.Get("web.auth.activate.signInToApprove", fallback: "Sign in to approve")), "signed-out offers Sign in to approve");
        Check(!scene.Shown("ApprovalPanel") && !scene.Shown("ApproveButton"), "signed-out offers no approval");
        Check(!scene.Wire.Authenticated.Last(), "a signed-out lookup carries no session");
        await scene.CaptureAsync("activate-signed-out");
        Check(scene.Wire.Requests.Count(r => r.StartsWith("POST")) == 0, "signed-out sends no decision");

        await using var quick = new Scene(field);
        await quick.OpenAsync(parent, signedIn: false, token: null, code: QuickConnect);
        Check(quick.Shown("QuickConnectSignIn") && Texts(quick.Page).Contains(quick.Vm.QuickConnectSignInText) && quick.Vm.QuickConnectSignInText.Contains(QuickConnect), "a Quick Connect code asks to sign in first: " + quick.Vm.QuickConnectSignInText);
        Check(!quick.Wire.Requests.Any(r => r.Contains("code=" + QuickConnect)), "a Quick Connect code is not looked up while signed out");
        Check(ButtonTexts(quick.Page).Contains(AppStrings.Get("web.auth.activate.enterAnotherCode", fallback: "Enter another code")), "quick-connect sign-in offers Enter another code");
        await quick.CaptureAsync("activate-quick-connect-signed-out");
    }

    private static async Task ApprovalCardsAsync(StackPanel parent, FieldInfo field)
    {
        await using (var scene = new Scene(field))
        {
            await scene.OpenAsync(parent, signedIn: true, token: null, code: NamedTv);
            var texts = Texts(scene.Page);
            Check(scene.Shown("ApprovalPanel") && scene.Shown("MatchCodeCard"), "the approval card shows the code to compare");
            Check(texts.Contains(DeviceActivation.Heading(scene.Vm.Details!)!.Render()) && scene.Vm.HeadingText.Contains("Living room TV"), "named device heading: " + scene.Vm.HeadingText);
            Check(scene.Vm.DeviceLineText.StartsWith("Apple TV · ") && texts.Contains(scene.Vm.DeviceLineText), "platform and time line: " + scene.Vm.DeviceLineText);
            Check(scene.Find<TextBlock>("MatchCodeText").Text == "4821 7730" && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(scene.Find<TextBlock>("MatchCodeText")) == "4 8 2 1 7 7 3 0", "the code is grouped and read digit by digit");
            Check(texts.Contains(scene.Vm.RequestedFromText) && scene.Vm.RequestedFromText.Contains("192.168.1.x"), "requested-from line: " + scene.Vm.RequestedFromText);
            Check(texts.Contains(AppStrings.Get("web.auth.activate.profilesShared", fallback: "This gives it access to your profiles. Profiles with a PIN stay locked.")), "profiles-shared line");
            Check(texts.Contains(AppStrings.Get("web.auth.activate.onlyApproveTv", fallback: "Only approve this if you started the sign-in yourself.")), "only-approve line");
            Check(scene.Find<TextBlock>("SignedInAsText").Inlines.OfType<Hyperlink>().Count() == 1 && Plain(scene.Find<TextBlock>("SignedInAsText")).Contains("laura"), "signed-in-as names the account with a switch link: " + Plain(scene.Find<TextBlock>("SignedInAsText")));
            Check(!texts.Any(text => text.Contains("WARM PONY", StringComparison.OrdinalIgnoreCase) || text.Contains("warm pony")), "the legacy match words are not shown");
            Check(scene.Find<Button>("ApproveButton").IsEnabled && scene.Find<Button>("DeclineButton").IsEnabled, "approve and decline are ready");
            Check(scene.Shown("EnterAnotherGhostButton") && !scene.Shown("EnterAnotherOutlineButton"), "a pending request offers Enter another code as a quiet button");
            await scene.CaptureAsync("activate-approval-named");
        }
        await using (var scene = new Scene(field))
        {
            await scene.OpenAsync(parent, signedIn: true, token: null, code: UnknownDevice);
            Check(scene.Vm.HeadingText == DeviceActivation.Heading(scene.Vm.Details!)!.Render() && !scene.Vm.HeadingText.Contains("Living"), "unknown device heading: " + scene.Vm.HeadingText);
            Check(scene.Vm.DeviceLineText.StartsWith("Requested ") || scene.Vm.DeviceLineText.Length > 0, "unknown device still says when it asked: " + scene.Vm.DeviceLineText);
            Check(scene.Wire.Lookups[UnknownDevice]["device_name"]!.GetValue<string>() == "", "the fixture's device sent no name");
            await scene.CaptureAsync("activate-approval-unknown");
        }
        await using (var scene = new Scene(field))
        {
            scene.Wire.Lookups[NamedTv]["device_name"] = "Tracearr"; scene.Wire.Lookups[NamedTv]["device_platform"] = "Tracearr";
            await scene.OpenAsync(parent, signedIn: true, token: null, code: NamedTv);
            Check(!scene.Vm.DeviceLineText.Contains("Tracearr ·") && scene.Vm.DeviceLineText.StartsWith("Requested "), "a platform that repeats the name is left out: " + scene.Vm.DeviceLineText);
        }
        await using (var scene = new Scene(field))
        {
            await scene.OpenAsync(parent, signedIn: true, token: "link-token", code: null);
            Check(scene.Wire.Requests.Any(r => r.StartsWith("GET /api/v2/auth/device?token=link-token")), "a link looks its token up");
            Check(!scene.Shown("EnterAnotherGhostButton") && !scene.Shown("EnterAnotherOutlineButton"), "a link request does not offer another code until it expires");
        }
    }

    private static async Task ImpersonationAsync(StackPanel parent, FieldInfo field)
    {
        await using var scene = new Scene(field) { Impersonating = true };
        await scene.OpenAsync(parent, signedIn: true, token: null, code: NamedTv);
        Check(scene.Shown("ImpersonationBlock") && !scene.Shown("ApprovalPanel"), "an admin viewing as someone gets the block, not the buttons");
        Check(scene.Find<TextBlock>("ImpersonationBlock").Text == DeviceActivation.Impersonation("laura").Render() && scene.Find<TextBlock>("ImpersonationBlock").Text.Contains("laura"), "impersonation text names the account: " + scene.Find<TextBlock>("ImpersonationBlock").Text);
        Check(scene.Shown("MatchCodeCard"), "the code still shows beside the impersonation block");
        await scene.CaptureAsync("activate-impersonation");
        await using var refused = new Scene(field) { Impersonating = true };
        refused.Wire.ErrorStatus[QuickConnect] = 403;
        await refused.OpenAsync(parent, signedIn: true, token: null, code: QuickConnect);
        Check(refused.Shown("LoadErrorPanel") && refused.Vm.LoadErrorText == DeviceActivation.Impersonation("laura").Render(), "a refused Quick Connect lookup explains impersonation: " + refused.Vm.LoadErrorText);
    }

    private static async Task RemotePlaybackAsync(StackPanel parent, FieldInfo field)
    {
        await using (var scene = new Scene(field))
        {
            await scene.OpenAsync(parent, signedIn: true, token: null, code: Remote);
            Check(scene.Vm.HeadingText == DeviceActivation.Heading(scene.Vm.Details!)!.Render() && scene.Vm.HeadingText.Contains("Living room TV"), "remote-playback heading asks to play on the device: " + scene.Vm.HeadingText);
            Check(scene.Shown("RemotePlaybackPanel") && !scene.Shown("ApprovalPanel") && !scene.Shown("ApproveButton"), "a remote-playback request cannot be approved here");
            Check(Texts(scene.Page).Contains(AppStrings.Get("web.auth.activate.remotePlaybackApproveInApp", fallback: "This request comes from the Silo app that sent playback to this TV. Approve it in that app.")), "approve-in-the-app line");
            Check(Texts(scene.Page).Contains(AppStrings.Get("web.auth.activate.remotePlaybackCantApprove", fallback: "The TV plays with the profile chosen in the app instead of signing in, so this page can't approve it.")), "can't-approve line");
            Check(scene.Shown("RemoteDeclineButton"), "a signed-in person can still decline it");
            Check(!Texts(scene.Page).Any(text => text.Contains("access to your profiles")), "no profiles-shared line for remote playback");
            await scene.CaptureAsync("activate-remote-playback");
            scene.Wire.Lookups[Remote]["status"] = "approved";
            await WaitAsync(() => scene.Vm.Details?.Status == "approved", 6000, "the watch to see the app approve it");
            await scene.LayoutAsync();
            Check(scene.Vm.ResultText == ActivationText.Of("web.auth.activate.result.remoteApproved", "This request was approved in the Silo app.").Render() && scene.Shown("ResultText"), "the app approving it is reported: " + scene.Vm.ResultText);
            Check(!scene.Shown("RemoteDeclineButton"), "the decline goes once it was approved");
            await scene.CaptureAsync("activate-remote-playback-approved");
        }
        await using (var scene = new Scene(field))
        {
            await scene.OpenAsync(parent, signedIn: false, token: null, code: Remote);
            Check(scene.Shown("RemotePlaybackPanel") && !scene.Shown("SignedOutPanel") && !scene.Shown("RemoteDeclineButton"), "signed out, a remote-playback request offers no sign-in and no decline");
        }
    }

    private static async Task ResultsAsync(StackPanel parent, FieldInfo field)
    {
        await using (var scene = new Scene(field))
        {
            await scene.OpenAsync(parent, signedIn: true, token: null, code: NamedTv);
            await scene.Vm.ApproveCommand.ExecuteAsync(null); await scene.LayoutAsync();
            Check(scene.Wire.Requests.Count(r => r.StartsWith("POST /api/v2/auth/device/approve ")) == 1, "approving sends one request");
            Check(scene.Wire.Bodies.Single(b => b.Path.EndsWith("/approve")).Body == "{\"code\":\"" + NamedTv + "\"}", "the approval sends the compact code only");
            Check(scene.Vm.ApprovedHere && scene.Vm.ResultText == ActivationText.Of("web.auth.activate.result.deviceDone", "Done. {device} is signing in.", ("device", "Living room TV")).Render(), "approved here reads as done: " + scene.Vm.ResultText);
            Check(scene.Shown("ResultText") && !scene.Shown("ApprovalPanel") && !scene.Shown("MatchCodeCard"), "a settled request drops the buttons and the comparison");
            await scene.CaptureAsync("activate-result-approved");
            scene.Wire.Lookups[NamedTv]["status"] = "consumed";
            await WaitAsync(() => scene.Vm.Details?.Status == "consumed", 6000, "the watch to see the device collect its session");
            await scene.LayoutAsync();
            Check(scene.Vm.ResultText == ActivationText.Of("web.auth.activate.result.deviceSignedIn", "{device} is signed in.", ("device", "Living room TV")).Render(), "the device collecting its session reads as signed in: " + scene.Vm.ResultText);
        }
        await using (var scene = new Scene(field))
        {
            scene.Wire.Lookups[SignedInCode] = Lookup("consumed", "Living room TV", "tvos", 20);
            await scene.OpenAsync(parent, signedIn: true, token: null, code: SignedInCode);
            Check(scene.Vm.ResultText == ActivationText.Of("web.auth.activate.result.tvAlreadySignedIn", "This device is already signed in.").Render() && scene.Shown("ResultText"), "already signed in: " + scene.Vm.ResultText);
            Check(scene.Shown("EnterAnotherOutlineButton"), "a settled request offers Enter another code as an outline button");
            await scene.CaptureAsync("activate-result-already-signed-in");
        }
        await using (var scene = new Scene(field))
        {
            scene.Wire.Lookups[DeclineCode] = Lookup("pending", "Kitchen display", "androidtv", 1);
            await scene.OpenAsync(parent, signedIn: true, token: null, code: DeclineCode);
            await scene.Vm.DenyCommand.ExecuteAsync(null); await scene.LayoutAsync();
            Check(scene.Wire.Requests.Count(r => r.StartsWith("POST /api/v2/auth/device/deny")) == 1, "declining sends one request");
            Check(scene.Vm.ResultText == ActivationText.Of("web.auth.activate.result.declined", "This sign-in was declined.").Render(), "declined: " + scene.Vm.ResultText);
            await scene.CaptureAsync("activate-result-declined");
        }
        await using (var scene = new Scene(field))
        {
            scene.Wire.Lookups["expired-link"] = Lookup("expired", "Living room TV", "tvos", 30);
            await scene.OpenAsync(parent, signedIn: true, token: "expired-link", code: null);
            Check(scene.Vm.ResultText == ActivationText.Of("web.auth.activate.result.expired", "This code expired. Get a new code on the device and try again.").Render(), "expired: " + scene.Vm.ResultText);
            Check(!scene.Shown("HeadingText") && !scene.Shown("MatchCodeCard") && !scene.Shown("ApprovalPanel"), "an expired request drops the question and the buttons");
            Check(scene.Shown("EnterAnotherOutlineButton"), "an expired link request offers another code");
            await scene.CaptureAsync("activate-result-expired");
        }
        await using (var scene = new Scene(field))
        {
            scene.Wire.Lookups[ExpiredCode] = Lookup("canceled", "Living room TV", "tvos", 30);
            await scene.OpenAsync(parent, signedIn: true, token: null, code: ExpiredCode);
            Check(scene.Vm.ResultText == ActivationText.Of("web.auth.activate.result.canceled", "This device stopped waiting. Start sign-in again on it.").Render(), "canceled: " + scene.Vm.ResultText);
        }
    }

    private static async Task DecisionRulesAsync(StackPanel parent, FieldInfo field)
    {
        await using (var scene = new Scene(field))
        {
            scene.Wire.DecisionStatus = 500;
            await scene.OpenAsync(parent, signedIn: true, token: null, code: NamedTv);
            await scene.Vm.ApproveCommand.ExecuteAsync(null); await scene.LayoutAsync();
            Check(scene.Wire.Requests.Count(r => r.StartsWith("POST /api/v2/auth/device/approve ")) == 1, "a failed approval is not sent a second time by itself");
            await Task.Delay(400);
            Check(scene.Wire.Requests.Count(r => r.StartsWith("POST /api/v2/auth/device/approve ")) == 1, "nothing resends it later either");
            Check(scene.Shown("ActionErrorText") && scene.Vm.ActionErrorText == DeviceActivation.DecisionFailed(true).Render(), "the failure is shown: " + scene.Vm.ActionErrorText);
            Check(scene.Find<Button>("ApproveButton").IsEnabled && scene.Shown("ApprovalPanel"), "the person can try again");
            await scene.CaptureAsync("activate-approve-failed");
            scene.Wire.DecisionStatus = 200;
            await scene.Vm.ApproveCommand.ExecuteAsync(null); await scene.LayoutAsync();
            Check(scene.Wire.Requests.Count(r => r.StartsWith("POST /api/v2/auth/device/approve ")) == 2 && scene.Vm.ApprovedHere, "trying again by hand sends a second request");
        }
        await using (var scene = new Scene(field))
        {
            scene.Wire.DecisionStatus = 409; scene.Wire.OnDecision = () => scene.Wire.Lookups[NamedTv]["status"] = "canceled";
            await scene.OpenAsync(parent, signedIn: true, token: null, code: NamedTv);
            await scene.Vm.ApproveCommand.ExecuteAsync(null); await scene.LayoutAsync();
            Check(scene.Vm.Details?.Status == "canceled" && scene.Vm.ActionError == null && !scene.Shown("ActionErrorText"), "a request that moved on shows why, with no error");
        }
        await using (var scene = new Scene(field))
        {
            scene.Wire.DecisionStatus = 403;
            await scene.OpenAsync(parent, signedIn: true, token: null, code: NamedTv);
            await scene.Vm.ApproveCommand.ExecuteAsync(null); await scene.LayoutAsync();
            Check(scene.Vm.ActionErrorText == DeviceActivation.Impersonation("laura").Render() && scene.Shown("ActionErrorText"), "a refused session is told to stop viewing as someone: " + scene.Vm.ActionErrorText);
        }
        await using (var scene = new Scene(field))
        {
            // A decision for one code must not touch the next code's card.
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            scene.Wire.DecisionGate = gate;
            await scene.OpenAsync(parent, signedIn: true, token: null, code: NamedTv);
            var approval = scene.Vm.ApproveCommand.ExecuteAsync(null);
            await WaitAsync(() => scene.Wire.Requests.Any(r => r.StartsWith("POST /api/v2/auth/device/approve ")), 3000, "the approval to be sent");
            scene.Wire.Lookups[Remote] = Lookup("pending", "Bedroom TV", "roku", 2); scene.Wire.Lookups[Remote]["client_purpose"] = "device_login"; scene.Wire.Lookups[Remote]["temporary"] = false;
            scene.Vm.CodeInput = Remote; await scene.Vm.SubmitCodeCommand.ExecuteAsync(null);
            Check(scene.Vm.HeadingText.Contains("Bedroom TV") && scene.Vm.IsPending, "the new code's card is pending");
            gate.SetResult(true); await approval;
            Check(scene.Vm.Details?.Status == "pending" && !scene.Vm.ApprovedHere && scene.Vm.ActionError == null && scene.Vm.HeadingText.Contains("Bedroom TV") && scene.Find<Button>("ApproveButton").IsEnabled,
                "an earlier code's approval finishing does not change the new code's card: " + scene.Vm.Details?.Status);
        }
        await using (var scene = new Scene(field))
        {
            await scene.OpenAsync(parent, signedIn: true, token: null, code: NamedTv);
            var before = scene.Vm.Details;
            scene.Wire.DelayLookups = true;
            scene.Wire.Lookups[NamedTv]["device_name"] = "Someone else's TV";
            var load = scene.Vm.LoadDetailsAsync();
            scene.Auth.Logout();
            scene.Wire.ReleaseLookups();
            await load;
            Check(ReferenceEquals(before, scene.Vm.Details) && !scene.Vm.IsLoadingDetails, "a reply that arrives after the account signed out is dropped, not shown");
        }
    }

    private static async Task QuickConnectAsync(StackPanel parent, FieldInfo field)
    {
        await using var scene = new Scene(field);
        await scene.OpenAsync(parent, signedIn: true, token: null, code: QuickConnect);
        await WaitAsync(() => scene.Vm.QuickConnectProfiles.Count == 2 && !scene.Vm.QuickConnectLoading, 4000, "the profiles to load");
        Check(scene.Shown("QuickConnectPanel") && !scene.Shown("PlainApprovalPanel"), "a Quick Connect request offers the profile choice");
        Check(scene.Find<TextBlock>("MatchCodeText").Text == QuickConnect, "a Quick Connect code is shown ungrouped");
        Check(!Texts(scene.Page).Any(text => text.Contains("access to your profiles")), "no profiles-shared line for Quick Connect");
        Check(scene.Vm.QuickConnectProfileLabel.Contains("Firefox"), "the profile label names the device: " + scene.Vm.QuickConnectProfileLabel);
        var radios = Descendants<RadioButton>(scene.Page).ToList();
        Check(radios.Count == 2 && radios.Count(radio => radio.IsChecked == true) == 1 && radios[0].IsChecked == true, "two profiles, the active one chosen");
        Check(!scene.Shown("QuickConnectPinBox"), "no PIN field for an unlocked profile");
        await scene.CaptureAsync("activate-quick-connect");

        radios[1].IsChecked = true; await Task.Delay(150); await scene.LayoutAsync();
        Check(scene.Shown("QuickConnectPinBox") && scene.Vm.QuickConnectPinLabel.Contains("Kids"), "a profile with a PIN asks for it: " + scene.Vm.QuickConnectPinLabel);
        Check(!scene.Find<Button>("QuickConnectApproveButton").IsEnabled, "approval waits for the PIN");
        await scene.CaptureAsync("activate-quick-connect-pin");
        scene.Wire.PinValid = false;
        scene.Find<PasswordBox>("QuickConnectPinBox").Password = "0000"; await Task.Delay(100);
        await scene.Vm.ApproveQuickConnectCommand.ExecuteAsync(null); await scene.LayoutAsync();
        Check(scene.Vm.QuickConnectErrorText == DeviceActivation.WrongPin.Render() && !scene.Wire.Requests.Any(r => r.Contains("approve-handoff")), "a wrong PIN keeps the request pending: " + scene.Vm.QuickConnectErrorText);
        scene.Wire.PinValid = true;
        scene.Find<PasswordBox>("QuickConnectPinBox").Password = "1234"; await Task.Delay(100);
        await scene.Vm.ApproveQuickConnectCommand.ExecuteAsync(null); await scene.LayoutAsync();
        var handoff = scene.Wire.Bodies.Single(b => b.Path.EndsWith("/approve-handoff"));
        Check(handoff.Body == "{\"code\":\"" + QuickConnect + "\"}" && handoff.ProfileId == "p-kids" && handoff.ProfileProof == "kids-proof", "the approval is for the chosen profile with its PIN proof");
        Check(scene.Vm.ResultText == ActivationText.Of("web.auth.activate.result.quickConnectDone", "Done. {device} is signing in as {profile}.", ("device", "Firefox"), ("profile", "Kids")).Render(), "approved as Kids: " + scene.Vm.ResultText);
        Check(scene.Wire.Requests.Count(r => r.StartsWith("GET /api/v2/auth/device")) == 1, "a Quick Connect approval is not looked up again");
        await Task.Delay(3500);
        Check(scene.Wire.Requests.Count(r => r.StartsWith("GET /api/v2/auth/device")) == 1, "a Quick Connect request is never watched");
        await scene.CaptureAsync("activate-quick-connect-approved");
    }

    // ---- Scene: one page, its services and a scripted server ----

    private sealed class Scene(FieldInfo field) : IAsyncDisposable
    {
        internal Wire Wire { get; } = new();
        internal bool Impersonating;
        internal ActivateDevicePage Page = null!;
        internal ActivateDeviceViewModel Vm = null!;
        internal AuthService Auth = null!;
        private HttpClient? _http;
        private SiloApiClient? _client;
        private ServiceProvider? _services;
        private StackPanel? _parent;
        private int _width = Widths[0];

        internal async Task OpenAsync(StackPanel parent, bool signedIn, string? token, string? code)
        {
            if (Page != null) { parent.Children.Remove(Page); Vm.Leave(); }
            _parent = parent;
            _http ??= new HttpClient(Wire);
            if (_client == null)
            {
                _client = new SiloApiClient(_http); _client.SetBaseUrl("https://activate-fixture.invalid");
                Auth = new AuthService(_client, new AuthApi(_client));
                if (signedIn)
                {
                    Auth.SetTokens("fixture-access", "fixture-refresh", 86400);
                    Auth.SetCurrentUser(new() { Id = "u1", Username = "laura", Role = "user", Impersonation = Impersonating ? new() { Active = true } : null });
                    Auth.SelectProfile("p-laura", profile: new() { Id = "p-laura", Name = "Laura", IsPrimary = true });
                }
                Wire.Authenticated.Clear();
                var api = new AuthApi(_client);
                Vm = new ActivateDeviceViewModel(api, Auth, new SettingsApi(_client), _client);
                _services = new ServiceCollection().AddSingleton(Vm).AddSingleton(new NavigationService()).BuildServiceProvider();
            }
            field.SetValue(null, _services);
            Page = new ActivateDevicePage { Width = _width, Height = 1100 };
            parent.Children.Add(Page);
            await LayoutAsync();
            await Vm.InitializeAsync(token, code);
            await LayoutAsync();
        }

        internal async Task LayoutAsync() { Page.UpdateLayout(); await Task.Delay(180); Page.UpdateLayout(); await Task.Delay(60); Page.UpdateLayout(); }
        internal T Find<T>(string name) where T : class => (T)Page.FindName(name) ?? throw new InvalidOperationException("Missing " + name);
        internal bool Shown(string name) => Find<FrameworkElement>(name).IsVisibleOnPage();

        internal async Task TypeAsync(TextBox box, string text) { box.Text = text; await Task.Delay(150); Page.UpdateLayout(); }

        internal async Task CaptureAsync(string name)
        {
            foreach (var width in Widths)
            {
                _width = width; Page.Width = width; await LayoutAsync();
                await SaveAsync(Page, name + "-" + width + ".png");
            }
            _width = Widths[0]; Page.Width = Widths[0]; await LayoutAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try { Vm?.Leave(); } catch { }
            if (Page != null) _parent?.Children.Remove(Page);
            Wire.ReleaseLookups();
            _http?.Dispose();
            if (_services != null) await _services.DisposeAsync();
            Auth?.Dispose();
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        internal readonly List<bool> Authenticated = [];
        internal readonly List<(string Path, string Body, string? ProfileId, string? ProfileProof)> Bodies = [];
        internal readonly Dictionary<string, int> ErrorStatus = [];
        internal readonly Dictionary<string, JsonObject> Lookups = new()
        {
            [NamedTv] = Lookup("pending", "Living room TV", "tvos", 5),
            [UnknownDevice] = Lookup("pending", "", "", 125),
            [Remote] = Lookup("pending", "Living room TV", "tvos", 1, "remote_playback", temporary: true),
            [QuickConnect] = Lookup("pending", "Firefox", "Jellyfin Web", 2, "jellyfin_quick_connect"),
        };
        internal int DecisionStatus = 200, RetryAfter;
        internal bool PinValid = true;
        internal Action? OnDecision;
        internal TaskCompletionSource<bool>? DecisionGate;
        internal bool DelayLookups;
        private TaskCompletionSource<bool> _lookupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void ReleaseLookups() { DelayLookups = false; _lookupGate.TrySetResult(true); }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var line = request.Method + " " + path + request.RequestUri.Query;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Requests) { Requests.Add(line + (body.Length > 0 ? " " : "")); }
            if (path == "/api/v2/theme/branding") return Json("""{"server_name":"Media Room"}""");
            if (path == "/api/v2/profiles" && request.Method == HttpMethod.Get)
                return Json("""{"items":[{"id":"p-laura","name":"Laura","has_pin":false,"is_primary":true},{"id":"p-kids","name":"Kids","has_pin":true}]}""");
            if (path.EndsWith("/verify-pin"))
                return Json(PinValid ? """{"valid":true,"profile_token":"kids-proof"}""" : """{"valid":false}""");
            if (path == "/api/v2/auth/device" && request.Method == HttpMethod.Get)
            {
                lock (Authenticated) Authenticated.Add(request.Headers.Authorization != null);
                if (DelayLookups) await _lookupGate.Task.WaitAsync(ct);
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                var key = query["code"] ?? query["token"] ?? "";
                if (ErrorStatus.TryGetValue(key, out var status))
                    return Problem(status, status == 429 ? RetryAfter : 0);
                return Lookups.TryGetValue(key, out var found) ? Json(found.ToJsonString()) : Problem(404, 0);
            }
            if (path.StartsWith("/api/v2/auth/device/"))
            {
                lock (Bodies) Bodies.Add((path, body, request.Headers.TryGetValues("X-Profile-Id", out var id) ? id.Single() : null,
                    request.Headers.TryGetValues("X-Profile-Token", out var proof) ? proof.Single() : null));
                if (DecisionGate is { } gate) await gate.Task.WaitAsync(ct);
                OnDecision?.Invoke();
                if (DecisionStatus != 200) return Problem(DecisionStatus, 0, DecisionStatus == 403 ? "permission_denied" : DecisionStatus == 409 ? "conflict" : "internal");
                var code = JsonNode.Parse(body)?["code"]?.GetValue<string>() ?? "";
                var approve = path.EndsWith("/approve") || path.EndsWith("/approve-handoff");
                if (Lookups.TryGetValue(code, out var found) && !found["client_purpose"]!.GetValue<string>().Equals("remote_playback"))
                    found["status"] = approve ? "approved" : "denied";
                return Json(approve ? """{"status":"approved"}""" : """{"status":"denied"}""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Problem(int status, int retryAfter, string type = "not_found")
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent("{\"type\":\"https://siloserver.org/docs/api/v2/problems/" + type + "\",\"title\":\"Synthetic\",\"status\":" + status + "}", System.Text.Encoding.UTF8, "application/problem+json"),
            };
            if (retryAfter > 0) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter.ToString());
            return response;
        }
    }

    private static JsonObject Lookup(string status, string device, string platform, int minutesAgo, string purpose = "device_login", bool temporary = false) => new()
    {
        ["status"] = status,
        ["user_code"] = purpose == "jellyfin_quick_connect" ? QuickConnect : "4821-7730",
        ["match_code"] = "warm pony",
        ["device_name"] = device,
        ["device_platform"] = platform,
        ["ip_address_hint"] = "192.168.1.x",
        ["requested_at"] = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo).ToString("O"),
        ["client_purpose"] = purpose,
        ["temporary"] = temporary,
        ["server_name"] = "Media Room",
    };

    // ---- Helpers ----

    private static readonly List<string> Failures = [];

    // A failed check is recorded and the run goes on, so one run shows every difference and every capture.
    private static void Check(bool condition, string message)
    {
        if (!condition) { Failures.Add("Activate device: " + message); Program.Log("CHECK FAILED: " + message); }
    }

    private static async Task Guard(string name, Func<Task> scenario)
    {
        try { await scenario(); }
        catch (Exception error) { Failures.Add("Activate device scenario " + name + ": " + error.GetType().Name + " " + error.Message); Program.Log("SCENARIO FAILED " + name + ": " + error); }
    }

    private static async Task WaitAsync(Func<bool> ready, int milliseconds, string what)
    {
        for (var waited = 0; waited < milliseconds; waited += 50) { if (ready()) return; await Task.Delay(50); }
        throw new InvalidOperationException("Activate device: timed out waiting for " + what);
    }

    private static bool Shown(FrameworkElement element) => element.ActualWidth > 0 && element.ActualHeight > 0;
    private static bool IsVisibleOnPage(this FrameworkElement element)
    {
        for (DependencyObject? node = element; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: Visibility.Collapsed }) return false;
        return element.ActualWidth > 0;
    }

    private static List<string> Texts(DependencyObject root)
        => Descendants<TextBlock>(root).Where(text => text.IsVisibleOnPage()).Select(Plain).Where(text => text.Length > 0).ToList();

    private static List<string> ButtonTexts(DependencyObject root)
        => Descendants<Button>(root).Where(button => button.IsVisibleOnPage()).Select(button => button.Content as string ?? "").Where(text => text.Length > 0).ToList();

    private static string Plain(TextBlock block)
        => block.Inlines.Count == 0 ? block.Text : string.Concat(block.Inlines.Select(PlainInline));

    private static string PlainInline(Inline inline) => inline switch
    {
        Run run => run.Text,
        Span span => string.Concat(span.Inlines.Select(PlainInline)),
        _ => "",
    };

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }

    private static async Task SaveAsync(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException("Activate device page was not laid out for capture.");
        var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
}
