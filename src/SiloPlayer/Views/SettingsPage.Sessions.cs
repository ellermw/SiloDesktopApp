using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private readonly List<AuthSession> _loginSessions = [];
    private readonly HashSet<string> _loginSessionCursors = [];
    private AuthSession? _currentLoginSession;
    private string? _loginSessionNextCursor;
    private ApiRequestContext _loginSessionContext;
    private int _loginSessionRevision;
    private bool _loginSessionLoading;
    private string? _loginSessionError;
    private bool _loginSessionUnavailable;
    private bool _loginSessionCapabilitiesLoaded;
    private ContentDialog? _loginSessionDialog;
    private DispatcherTimer? _loginSessionTimer;

    private void DeactivateLoginSessions()
    {
        ++_loginSessionRevision;
        _loginSessionLoading = false;
        _loginSessionTimer?.Stop();
        _loginSessionDialog?.Hide();
    }

    private async Task LoadSessionsAsync(bool append = false)
    {
        if (!_canManageProfiles || append && (_loginSessionLoading || _loginSessionNextCursor == null)) return;
        var revision = ++_loginSessionRevision;
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = client.CaptureContext();
        var sameContext = context == _loginSessionContext;
        _loginSessionContext = context;
        _loginSessionLoading = true; _loginSessionError = null; _loginSessionUnavailable = false;
        if (!append)
        {
            _loginSessionCapabilitiesLoaded = false;
            if (!sameContext) { _loginSessions.Clear(); _currentLoginSession = null; _loginSessionCursors.Clear(); _loginSessionNextCursor = null; }
        }
        RebuildSessionCards();
        bool Current() => revision == _loginSessionRevision && client.IsCurrentContext(context) && SessionsPanel.Visibility == Visibility.Visible;
        try
        {
            var api = App.Services.GetRequiredService<AuthApi>();
            if (!append)
            {
                var capabilities = await api.GetLoginSessionCapabilitiesAsync();
                if (!Current()) return;
                _loginSessionCapabilitiesLoaded = true;
                if (!capabilities.Available) { _loginSessionUnavailable = true; return; }
            }
            var cursor = append ? _loginSessionNextCursor : null;
            var page = await api.GetSessionsPageAsync(cursor);
            if (!Current()) return;
            var next = page.Page!.HasMore ? page.Page.NextCursor : null;
            if (append && next != null && (next == cursor || _loginSessionCursors.Contains(next)))
                throw new InvalidDataException("Couldn't load more sessions. Reload the list.");
            if (!append) { _loginSessions.Clear(); _currentLoginSession = null; _loginSessionCursors.Clear(); }
            if (cursor != null) _loginSessionCursors.Add(cursor);
            foreach (var session in page.Sessions)
            {
                var old = _loginSessions.FindIndex(row => row.Id == session.Id);
                if (old >= 0) _loginSessions[old] = session; else _loginSessions.Add(session);
            }
            _currentLoginSession = page.CurrentSession ?? _currentLoginSession ?? _loginSessions.FirstOrDefault(row => row.IsCurrent);
            _loginSessionNextCursor = next;
            _loginSessionTimer ??= CreateLoginSessionTimer();
            _loginSessionTimer.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (Current()) _loginSessionError = ex.Message; }
        finally
        {
            if (Current()) { _loginSessionLoading = false; RebuildSessionCards(); }
        }
    }

    private DispatcherTimer CreateLoginSessionTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += async (_, _) => { if (!_loginSessionLoading && _loginSessionDialog == null) await LoadSessionsAsync(); };
        return timer;
    }

    private void RebuildSessionCards()
    {
        SessionCardsContainer.Children.Clear();
        var refresh = new Button { Content = "Refresh", IsEnabled = !_loginSessionLoading, HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += async (_, _) => await LoadSessionsAsync();
        AutomationProperties.SetName(refresh, "Refresh sessions");
        SessionCardsContainer.Children.Add(refresh);
        if (_loginSessionLoading) SessionCardsContainer.Children.Add(SecondaryText("Loading sessions…"));
        if (_loginSessionUnavailable) { SessionCardsContainer.Children.Add(SecondaryText("Signed-in session management is unavailable on this server.")); return; }
        if (_loginSessionError != null)
        {
            SessionCardsContainer.Children.Add(ErrorText($"{(_loginSessionCapabilitiesLoaded ? "Couldn't load sessions." : "Couldn't load session settings.")} {_loginSessionError}"));
            var retry = new Button { Content = "Retry", IsEnabled = !_loginSessionLoading };
            retry.Click += async (_, _) => await LoadSessionsAsync(append: _loginSessionNextCursor != null);
            SessionCardsContainer.Children.Add(retry);
        }
        if (_currentLoginSession is { } current)
        {
            var group = CreateSettingsGroup("This app", "The session you're using right now.");
            ((StackPanel)group.Child).Children.Add(BuildSessionCard(current));
            SessionCardsContainer.Children.Add(group);
        }
        var others = _loginSessions.Where(row => !row.IsCurrent && row.Id != _currentLoginSession?.Id).ToList();
        if (!_loginSessionLoading || _loginSessions.Count > 0)
        {
            var group = CreateSettingsGroup("Other signed-in sessions", "Other browsers and apps with access to your account.");
            var stack = (StackPanel)group.Child;
            foreach (var session in others) stack.Children.Add(BuildSessionCard(session));
            if (others.Count == 0) stack.Children.Add(SecondaryText("No other signed-in sessions."));
            SessionCardsContainer.Children.Add(group);
        }
        if (_loginSessionNextCursor != null && _loginSessionError == null)
        {
            var more = new Button { Content = _loginSessionLoading ? "Loading…" : "Load more sessions", IsEnabled = !_loginSessionLoading };
            more.Click += async (_, _) => await LoadSessionsAsync(append: true);
            SessionCardsContainer.Children.Add(more);
        }
        SessionCardsContainer.Children.Add(SecondaryText("Last seen records authenticated activity, updated at most once a minute. Older sessions may have no recorded activity yet. Signing out keeps the password and saved device settings."));
        var account = new Button { Content = "Manage your account's password" };
        account.Click += (_, _) => ShowSettingsDetail(AccountPasswordTab);
        SessionCardsContainer.Children.Add(account);
    }

    private Border BuildSessionCard(AuthSession session)
    {
        var card = SettingsSurface();
        var stack = (StackPanel)card.Child;
        var name = LoginSessionDisplay.Name(session);
        stack.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (session.IsCurrent) stack.Children.Add(SecondaryText("This app"));
        stack.Children.Add(SecondaryText($"{LoginSessionDisplay.LastSeen(session.LastSeenAt, DateTimeOffset.UtcNow)} · Signed in {SessionDate(session.CreatedAt)}"));
        var signOut = new Button { Content = session.IsCurrent ? "Sign out this app" : "Sign out", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(signOut, session.IsCurrent ? "Sign out this app" : $"Sign out {name}");
        signOut.Click += async (_, _) => await ConfirmSessionSignOutAsync(session);
        stack.Children.Add(signOut);
        var details = new StackPanel { Spacing = 8 };
        foreach (var (label, value) in new[] { ("Last seen", session.LastSeenAt == null ? "Not recorded yet" : SessionDate(session.LastSeenAt)),
            ("Signed in", SessionDate(session.CreatedAt)), ("IP address at sign-in", string.IsNullOrEmpty(session.IpAddress) ? "Unknown" : session.IpAddress),
            ("Expires", SessionDate(session.ExpiresAt)), ("User-Agent", string.IsNullOrEmpty(session.DeviceName) ? "Not sent" : session.DeviceName) })
            details.Children.Add(new TextBlock { Text = $"{label}\n{value}", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        stack.Children.Add(new Expander { Header = "Details", Content = details, HorizontalAlignment = HorizontalAlignment.Stretch });
        return card;
    }

    private static string SessionDate(string? value) => DateTimeOffset.TryParse(value, out var date)
        ? DateTimeDisplay.FormatDate(date.LocalDateTime, medium: true) + " " + DateTimeDisplay.FormatTime(date.LocalDateTime) : "Not recorded yet";

    private async Task ConfirmSessionSignOutAsync(AuthSession session)
    {
        if (_loginSessionDialog != null || !_canManageProfiles) return;
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = _loginSessionContext;
        var revision = _loginSessionRevision;
        if (!client.IsCurrentContext(context)) return;
        var error = ErrorText(""); error.Visibility = Visibility.Collapsed;
        var content = new StackPanel { Spacing = 12, Children = {
            SecondaryText(session.IsCurrent ? "You'll return to the sign-in screen. You can sign in again with the same password or sign-in provider."
                : "This client will lose access on its next authenticated request. Its other sessions and password stay unchanged."),
            SecondaryText("A video already playing may continue until its next request. Saved device settings are kept."), error } };
        var dialog = new ContentDialog { Title = session.IsCurrent ? "Sign out this app?" : $"Sign out {LoginSessionDisplay.Name(session)}?",
            Content = content, PrimaryButtonText = "Sign out", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot };
        var completed = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false; dialog.CloseButtonText = ""; dialog.PrimaryButtonText = "Signing out…";
            try
            {
                if (revision != _loginSessionRevision || !client.IsCurrentContext(context)) return;
                await App.Services.GetRequiredService<AuthApi>().RevokeSessionAsync(context, session.Id);
                if (revision != _loginSessionRevision || !client.IsCurrentContext(context)) return;
                completed = true; args.Cancel = false;
            }
            catch (OperationCanceledException) { dialog.Hide(); }
            catch (Exception ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
            finally { dialog.IsPrimaryButtonEnabled = true; dialog.CloseButtonText = "Cancel"; dialog.PrimaryButtonText = "Sign out"; deferral.Complete(); }
        };
        _loginSessionDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_loginSessionDialog, dialog)) _loginSessionDialog = null; }
        if (!completed || !client.IsCurrentContext(context)) return;
        if (session.IsCurrent) await App.Services.GetRequiredService<AuthService>().LogoutAsync();
        else { Toast("Session signed out"); await LoadSessionsAsync(); }
    }
}
