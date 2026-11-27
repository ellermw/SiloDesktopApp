using System.Text.Json.Nodes;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using SiloPlayer.Controls;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin.Users;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private Task SetPasswordAsync() => ResetPasswordAsync("password");

    private static string Words(string key, string fallback, params (string Name, object? Value)[] args) =>
        AppStrings.Get(key, args.Length == 0 ? null : args.ToDictionary(pair => pair.Name, pair => pair.Value), fallback);

    /// <summary>
    /// Reset password (official <c>AdminUserPasswordResetDialog</c>): email the account a single-use reset link, create a link to share, or type a
    /// temporary password now. Every option signs the account out on all its devices. The option the admin came for is chosen first.
    /// </summary>
    private async Task ResetPasswordAsync(string preferred)
    {
        var generation = _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = "Close the active editor and reload/review before another change."; return; }
        var opened = await FreshReviewAsync(generation);
        var user = opened.User;
        var username = user.Text("username");
        var email = user.Text("email");
        var own = user.Text("id") == _review!.Viewer.Text("id");
        var loginByPassword = user.Flag("password_login");
        var enabled = user.Flag("enabled");
        var canEmail = !own && loginByPassword && enabled && Feature("password_reset_email") && email.Length > 0;
        var canLink = !own && loginByPassword && enabled && Feature("password_reset_link");
        var policy = await ReadPasswordPolicyAsync(generation);
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;

        var method = preferred == "email" && canEmail ? "email" : preferred == "link" && canLink ? "link" : preferred == "password" || !canEmail && !canLink ? "password" : canEmail ? "email" : "link";
        var mustChange = true;
        ContentDialog dialog = null!;
        var done = false;
        var busy = false;
        var blocked = false;
        var wrote = false;

        var error = AlertText();
        var status = Text("", 14); status.Visibility = Visibility.Collapsed; status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var warning = Text("", 14); warning.Visibility = Visibility.Collapsed;
        var linkBox = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var options = new StackPanel { Spacing = 8 };
        var content = new StackPanel { Spacing = 12, MinWidth = 280 };
        var description = Text(Words("web.admin.users.passwordResetDialog.description", "Pick how they get back in. Every option signs them out on all devices."), 14, true);
        content.Children.Add(description);
        content.Children.Add(error); content.Children.Add(status); content.Children.Add(warning); content.Children.Add(linkBox); content.Children.Add(options);
        AutomationProperties.SetAutomationId(content, "PasswordResetForm");

        // The three choices, as cards in a radio group.
        var group = "reset-" + Guid.NewGuid().ToString("N");
        var radios = new Dictionary<string, RadioButton>(StringComparer.Ordinal);
        var cards = new Dictionary<string, Border>(StringComparer.Ordinal);
        Border Card(string key, string title, FrameworkElement body, bool enabledChoice)
        {
            var radio = new RadioButton { GroupName = group, IsEnabled = enabledChoice, MinWidth = 0, Padding = new(0), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 0, 0, 0) };
            AutomationProperties.SetAutomationId(radio, "PasswordResetOption_" + key); AutomationProperties.SetName(radio, title);
            radio.Checked += (_, _) => { method = key; Sync(); };
            radios[key] = radio;
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(Medium(title, 14)); text.Children.Add(body);
            var grid = new Grid { ColumnSpacing = 4 };
            grid.ColumnDefinitions.Add(new() { Width = new(32) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.Children.Add(radio); Grid.SetColumn(text, 1); grid.Children.Add(text);
            var card = new Border { Child = grid, Padding = new(14, 12, 14, 12), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.7), Opacity = enabledChoice ? 1 : 0.6 };
            card.Tapped += (_, _) => { if (enabledChoice && !busy) radio.IsChecked = true; };
            cards[key] = card;
            return card;
        }
        FrameworkElement Hint(string text) { var block = Text(text, 12, true); return block; }

        string EmailHint() => canEmail ? Words("web.admin.users.passwordResetDialog.emailHint", $"Sent to {email}. Works once and expires in 24 hours.", ("email", email))
            : !enabled ? Words("web.admin.users.passwordResetDialog.emailHintDisabled", "Enable the account to send it a reset link.")
            : email.Length == 0 ? Words("web.admin.users.passwordResetDialog.emailHintNoAddress", "This account has no email address, so share a link instead.")
            : Words("web.admin.users.passwordResetDialog.emailHintNoMail", "Set up email in Settings to send reset links.");
        string LinkHint() => canLink ? Words("web.admin.users.passwordResetDialog.linkHint", "Copy it and send it yourself.")
            : !enabled ? Words("web.admin.users.passwordResetDialog.linkHintDisabled", "Enable the account to create a reset link.")
            : Words("web.admin.users.passwordResetDialog.linkHintNoUrl", "Set the Silo public URL to create reset links.");

        var password = new PasswordBox { IsEnabled = false };
        AutomationProperties.SetAutomationId(password, "PasswordResetPassword");
        AutomationProperties.SetName(password, Words("web.admin.users.passwordResetDialog.passwordLabel", "Temporary password"));
        var rules = Text(Words("web.admin.users.passwordResetDialog.passwordRules", $"At least {policy.MinimumLength} characters and no more than {policy.MaximumBytes} UTF-8 bytes.", ("min", policy.MinimumLength), ("max", policy.MaximumBytes))
            + (AdminUserRuleText(policy) is { Length: > 0 } extra ? " " + extra : ""), 12, true);
        var mustChangeBox = new CheckBox { IsChecked = true, IsEnabled = false, Content = Text(Words("web.admin.users.passwordResetDialog.mustChange", "They must choose a new one at next sign-in"), 12), MinHeight = 32 };
        AutomationProperties.SetAutomationId(mustChangeBox, "PasswordResetMustChange");
        mustChangeBox.Checked += (_, _) => mustChange = true; mustChangeBox.Unchecked += (_, _) => mustChange = false;
        var passwordBody = new StackPanel { Spacing = 8 };
        passwordBody.Children.Add(Hint(Words("web.admin.users.passwordResetDialog.passwordOptionHint", "Type one now and tell them.")));
        passwordBody.Children.Add(password); passwordBody.Children.Add(rules); passwordBody.Children.Add(mustChangeBox);

        if (!own)
        {
            options.Children.Add(Card("email", Words("web.admin.users.passwordResetDialog.emailOption", "Email a reset link"), Hint(EmailHint()), canEmail));
            options.Children.Add(Card("link", Words("web.admin.users.passwordResetDialog.linkOption", "Create a link to share"), Hint(LinkHint()), canLink));
        }
        options.Children.Add(Card("password", Words("web.admin.users.passwordResetDialog.passwordOption", "Set a temporary password"), passwordBody, true));
        if (own) { description.Visibility = Visibility.Collapsed; }

        dialog = NewFormDialog(Words("web.admin.users.passwordResetDialog.title", $"Reset password for {username}", ("username", username)), content, "", AppStrings.Get("common.cancel", fallback: "Cancel"));
        _dialog = dialog; _dialogDirty = true;

        string PrimaryLabel() => done ? AppStrings.Get("common.done", fallback: "Done") : method switch
        {
            "email" => Words("web.admin.users.passwordResetDialog.emailAction", "Email link"),
            "link" => Words("web.admin.users.passwordResetDialog.linkAction", "Create link"),
            _ => Words("web.admin.users.passwordResetDialog.passwordAction", "Set password"),
        };
        void Sync()
        {
            foreach (var (key, radio) in radios) { var selected = key == method; if (radio.IsChecked != selected && selected) radio.IsChecked = true; cards[key].BorderBrush = selected ? Tint("AccentBrush", 0.5) : Tint("BorderBrush", 0.7); cards[key].Background = selected ? Tint("AccentBrush", 0.06) : null; }
            var passwordMode = method == "password" && !busy;
            password.IsEnabled = passwordMode; mustChangeBox.IsEnabled = passwordMode;
            passwordBody.Opacity = method == "password" ? 1 : 0.5;
            dialog.PrimaryButtonText = PrimaryLabel();
            dialog.CloseButtonText = done ? "" : AppStrings.Get("common.cancel", fallback: "Cancel");
            dialog.IsPrimaryButtonEnabled = done || !busy && !blocked && method switch
            {
                "email" => canEmail,
                "link" => canLink,
                _ => NewPassword.Problem(password.Password, policy) is null,
            };
            options.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
            if (done) description.Visibility = Visibility.Collapsed;
        }
        password.PasswordChanged += (_, _) => Sync();
        radios[method].IsChecked = true;
        Sync();

        dialog.Closing += (_, args) => { if (busy) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (done) return;
            args.Cancel = true;
            if (busy || blocked) return;
            Show(error, null); Show(warning, null);
            var deferral = args.GetDeferral();
            busy = true; Sync();
            var submitted = password.Password;
            try
            {
                if (!_workspace.IsCurrent(generation)) return;
                // Read the account fresh so the write carries its current validator.
                var fresh = await FreshReviewAsync(generation);
                JsonObject body;
                AdminUsersWrite write;
                if (method == "password")
                {
                    body = new JsonObject { ["password"] = submitted };
                    if (mustChange) body["require_password_change"] = true;
                    AdminUsersContract.Validate(AdminUsersContract.Schema("AdminAccountUpdateBody"), body);
                    write = new(AdminUsersOperation.UpdateAccount, body);
                }
                else write = new(method == "email" ? AdminUsersOperation.ResetEmail : AdminUsersOperation.ResetLink);
                var result = await _workspace.WriteAsync(generation, _api, fresh, write);
                wrote = true;
                if (!_workspace.IsCurrent(generation)) { await result.DiscardViewAsAsync(); return; }
                var expires = result.Body is { } reply && reply.Text("expires_at").Length > 0 ? FullDateTime(reply.Text("expires_at")) : "";
                if (method == "password")
                {
                    password.Password = "";
                    done = true;
                    Show(status, mustChange ? Words("web.admin.users.passwordResetDialog.passwordSetMustChange", $"Password set. {username} is signed out everywhere. They must choose a new one at next sign-in.", ("username", username))
                        : Words("web.admin.users.passwordResetDialog.passwordSet", $"Password set. {username} is signed out everywhere.", ("username", username)));
                }
                else if (method == "email")
                {
                    var sent = result.Body?.Text("delivery_status") == "sent";
                    if (sent) { done = true; Show(status, Words("web.admin.users.passwordResetDialog.emailSent", $"Sent a reset link to {email}. It expires {expires}.", ("email", email), ("expires", expires))); }
                    else Show(warning, Words("web.admin.users.passwordResetDialog.emailUnconfirmed", "The mail server did not confirm delivery. Send it again, or create a link to share."));
                }
                else
                {
                    done = true;
                    var url = result.Body?.Text("reset_url") ?? "";
                    var urlText = new TextBlock { Text = url, FontFamily = AdminMonitorUi.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                    AutomationProperties.SetAutomationId(urlText, "PasswordResetLink");
                    linkBox.Children.Clear();
                    linkBox.Children.Add(new Border { Child = urlText, Padding = new(10), CornerRadius = new(6), Background = Tint("MutedBrush", 0.5) });
                    linkBox.Children.Add(Text(Words("web.admin.users.passwordResetDialog.linkShare", $"Share this link only with {username}. Anyone who has it can set the account's password until it expires {expires}.", ("username", username), ("expires", expires)), 12, true));
                    linkBox.Children.Add(Action(Words("web.admin.users.passwordResetDialog.copyLink", "Copy link"), async () => { await CopySensitiveAsync(url); Show(status, Words("web.admin.users.passwordResetDialog.linkCopied", "Link copied")); }, nested: true));
                    linkBox.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException)
            {
                if (!_workspace.IsCurrent(generation)) { busy = false; dialog.Hide(); }
                else { blocked = _workspace.NeedsReview; Show(error, blocked ? UnconfirmedWriteText(false) : "The request didn't finish. Check your connection and try again."); }
            }
            catch (AdminUsersReviewRequiredException) { blocked = true; Show(error, UnconfirmedWriteText(false)); }
            catch (ApiException exception) when (exception.StatusCode == 412) { Show(error, T("web.admin.users.page.accountChanged")); }
            catch (ApiException exception) when (method == "password" && NewPassword.FieldProblem(exception, "body.password") is { } problem) { Show(error, problem); }
            catch (ArgumentException exception) { Show(error, exception.Message); }
            catch (Exception exception)
            {
                if (_workspace.IsCurrent(generation))
                {
                    blocked = _workspace.NeedsReview || _workspace.OutcomeUnknown;
                    Show(error, blocked ? UnconfirmedWriteText(false) : RefusalText(exception, Words(method == "password" ? "web.admin.users.passwordResetDialog.setFailed" : "web.admin.users.passwordResetDialog.issueFailed",
                        method == "password" ? "Could not set the password." : "Could not create a reset link.")));
                }
            }
            finally { busy = false; Sync(); deferral.Complete(); }
        };
        try
        {
            await dialog.ShowAsync();
            if (!_workspace.IsCurrent(generation)) return;
            if (wrote) { RequireGeneration(generation); await LoadAsync(_location); }
        }
        finally { if (ReferenceEquals(_dialog, dialog)) { _dialog = null; _dialogDirty = false; } }
    }

    /// <summary>The capital-letter and special-character rules of the policy, as the official hint sentences.</summary>
    private static string AdminUserRuleText(PasswordPolicy policy) => string.Join(" ", new[]
    {
        policy.RequireUppercase ? AppStrings.Get("web.auth.newPassword.needsUppercase", fallback: "Include at least one capital letter.") : null,
        policy.RequireSpecial ? AppStrings.Get("web.auth.newPassword.needsSpecial", fallback: "Include at least one special character, such as ! or #.") : null,
    }.OfType<string>());
}
