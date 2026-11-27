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
    private bool _canRevokeOtherSessions;
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
        if (!_canManageProfiles || append && (_loginSessionLoading || !_loginSessionCapabilitiesLoaded || _loginSessionNextCursor == null)) return;
        var revision = ++_loginSessionRevision;
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = client.CaptureContext();
        var sameContext = context == _loginSessionContext;
        _loginSessionContext = context;
        _loginSessionLoading = true; _loginSessionError = null; _loginSessionUnavailable = false;
        if (!append)
        {
            _loginSessionCapabilitiesLoaded = false;
            if (!sameContext) ClearLoginSessionRows();
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
                if (!capabilities.Available) { _loginSessionUnavailable = true; ClearLoginSessionRows(); return; }
                if (_canRevokeOtherSessions != capabilities.RevokeOtherSessions) ClearLoginSessionRows();
                _canRevokeOtherSessions = capabilities.RevokeOtherSessions;
            }
            var cursor = append ? _loginSessionNextCursor : null;
            var page = await api.GetSessionsPageAsync(cursor);
            if (!Current()) return;
            var next = page.Page!.HasMore ? page.Page.NextCursor : null;
            if (append && next != null && (next == cursor || _loginSessionCursors.Contains(next)))
                throw new InvalidDataException("Couldn't load more sessions. Reload the list.");
            if (!append) ClearLoginSessionRows();
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
        catch (Exception ex) { if (Current()) _loginSessionError = ex.DisplayMessage(); }
        finally
        {
            if (Current()) { _loginSessionLoading = false; RebuildSessionCards(); }
        }
    }

    private void ClearLoginSessionRows()
    {
        _loginSessions.Clear();
        _currentLoginSession = null;
        _loginSessionCursors.Clear();
        _loginSessionNextCursor = null;
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
        var refresh = new Button { Content = L("web.settings.sessions.refresh", "Refresh"), IsEnabled = !_loginSessionLoading, HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += async (_, _) => await LoadSessionsAsync();
        AutomationProperties.SetName(refresh, L("web.settings.sessions.refreshLabel", "Refresh sessions"));
        SessionCardsContainer.Children.Add(refresh);
        if (_loginSessionLoading) SessionCardsContainer.Children.Add(SecondaryText(L("web.settings.sessions.loading", "Loading sessions…")));
        if (_loginSessionUnavailable) { SessionCardsContainer.Children.Add(SecondaryText(L("web.settings.signedInSessions.unavailable", "Signed-in session management is unavailable on this server."))); return; }
        if (_loginSessionError != null)
        {
            SessionCardsContainer.Children.Add(ErrorText($"{(_loginSessionCapabilitiesLoaded ? "Couldn't load sessions." : "Couldn't load session settings.")} {_loginSessionError}"));
            var retry = new Button { Content = L("common.retry", "Retry"), IsEnabled = !_loginSessionLoading };
            retry.Click += async (_, _) => await LoadSessionsAsync(append: _loginSessionCapabilitiesLoaded && _loginSessionNextCursor != null);
            SessionCardsContainer.Children.Add(retry);
        }
        if (_currentLoginSession is { } current)
        {
            var group = CreateSettingsGroup("This app", L("web.settings.sessions.current.description", "The session you're using right now."));
            ((StackPanel)group.Child).Children.Add(BuildSessionCard(current));
            SessionCardsContainer.Children.Add(group);
        }
        var others = _loginSessions.Where(row => !row.IsCurrent && row.Id != _currentLoginSession?.Id).ToList();
        if (!_loginSessionLoading || _loginSessions.Count > 0)
        {
            var group = CreateSettingsGroup(L("web.settings.sessions.others.title", "Other signed-in sessions"), L("web.settings.sessions.others.description", "Other browsers and apps with access to your account."));
            var stack = (StackPanel)group.Child;
            foreach (var session in others) stack.Children.Add(BuildSessionCard(session));
            if (others.Count == 0) stack.Children.Add(SecondaryText(L("web.settings.sessions.others.empty", "No other signed-in sessions.")));
            SessionCardsContainer.Children.Add(group);
        }
        if (_loginSessionCapabilitiesLoaded && _loginSessionNextCursor != null && _loginSessionError == null)
        {
            var more = new Button { Content = _loginSessionLoading ? L("web.settings.sessions.loadingMore", "Loading…") : L("web.settings.sessions.loadMore", "Load more sessions"), IsEnabled = !_loginSessionLoading };
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
        var current = IsCurrentLoginSession(session);
        if (current) stack.Children.Add(SecondaryText("This app"));
        var redacted = LoginSessionDisplay.HasRedactedDetails(session, _currentLoginSession);
        if (!redacted)
        {
            var lastSeen = LoginSessionDisplay.LastSeen(session.LastSeenAt, DateTimeOffset.UtcNow);
            var signedIn = SessionDate(session.CreatedAt);
            stack.Children.Add(SecondaryText(L("web.settings.sessions.row.summary", $"{lastSeen} · Signed in {signedIn}",
                Args(("last_seen", lastSeen), ("signed_in", signedIn)))));
        }
        if (CanRevokeLoginSession(session))
        {
            var label = current ? "Sign out this app" : L("common.signOut", "Sign out");
            var signOut = new Button { HorizontalAlignment = HorizontalAlignment.Left };
            LoginSessionAction.AllowWrapping(signOut, label);
            AutomationProperties.SetName(signOut, current ? label : L("web.settings.sessions.row.signOutDevice", $"Sign out {name}", Args(("device", name))));
            signOut.Click += async (_, _) => await ConfirmSessionSignOutAsync(session);
            stack.Children.Add(signOut);
        }
        if (redacted) return card;

        var expander = new Expander
        {
            Header = LoginSessionAction.Label(L("web.settings.sessions.row.details", "Details")),
            Content = BuildSessionDetails(session), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 0
        };
        AutomationProperties.SetName(expander, L("web.settings.sessions.row.detailsFor", $"Details for {name}", Args(("device", name))));
        stack.Children.Add(expander);
        return card;
    }

    private static Grid BuildSessionDetails(AuthSession session)
    {
        var fields = new[]
        {
            (L("web.settings.sessions.row.lastSeen", "Last seen"), SessionDate(session.LastSeenAt)),
            (L("web.settings.sessions.row.signedIn", "Signed in"), SessionDate(session.CreatedAt)),
            (L("web.settings.sessions.row.ipAddress", "IP address at sign-in"), string.IsNullOrEmpty(session.IpAddress)
                ? L("web.settings.sessions.row.unknown", "Unknown") : session.IpAddress),
            (L("web.settings.sessions.row.expires", "Expires"), SessionDate(session.ExpiresAt)),
            (L("web.settings.sessions.row.userAgent", "User-Agent"), string.IsNullOrEmpty(session.DeviceName)
                ? L("web.settings.sessions.row.notSent", "Not sent") : session.DeviceName)
        };
        var details = new Grid { RowSpacing = 12, ColumnSpacing = 16 };
        details.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        foreach (var (label, value) in fields)
        {
            var heading = SecondaryText(label);
            heading.FontSize = 12;
            details.Children.Add(new StackPanel { Spacing = 2, Children =
            {
                heading, new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 12 }
            } });
        }
        Arrange(false);
        details.SizeChanged += (_, args) => Arrange(args.NewSize.Width >= 520);
        return details;

        void Arrange(bool twoColumns)
        {
            var rowCount = twoColumns ? 3 : fields.Length;
            if (details.RowDefinitions.Count == rowCount) return;
            details.RowDefinitions.Clear();
            for (var i = 0; i < rowCount; i++) details.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var i = 0; i < fields.Length; i++)
            {
                var field = (FrameworkElement)details.Children[i];
                Grid.SetRow(field, twoColumns ? i / 2 : i);
                Grid.SetColumn(field, twoColumns ? i % 2 : 0);
                Grid.SetColumnSpan(field, !twoColumns || i == fields.Length - 1 ? 2 : 1);
            }
        }
    }

    private bool IsCurrentLoginSession(AuthSession session) => session.IsCurrent || session.Id == _currentLoginSession?.Id;

    private bool CanRevokeLoginSession(AuthSession session) =>
        !_loginSessionUnavailable &&
        (IsCurrentLoginSession(session) || _loginSessionCapabilitiesLoaded && _canRevokeOtherSessions);

    private static string SessionDate(string? value) => DateTimeOffset.TryParse(value, out var date)
        ? DateTimeDisplay.FormatDate(date.LocalDateTime, medium: true) + " " + DateTimeDisplay.FormatTime(date.LocalDateTime) : L("web.settings.sessions.row.notRecorded", "Not recorded yet");

    private async Task ConfirmSessionSignOutAsync(AuthSession session)
    {
        if (_loginSessionDialog != null || !_canManageProfiles || !CanRevokeLoginSession(session) ||
            SessionsPanel.Visibility != Visibility.Visible) return;
        var current = IsCurrentLoginSession(session);
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = _loginSessionContext;
        var revision = _loginSessionRevision;
        if (!client.IsCurrentContext(context)) return;
        var error = ErrorText(""); error.Visibility = Visibility.Collapsed;
        var content = new StackPanel { Spacing = 12, Children = {
            SecondaryText(current ? L("web.settings.sessions.confirm.thisBrowserDescription", "You'll return to the sign-in screen. You can sign in again with the same password or sign-in provider.")
                : L("web.settings.sessions.confirm.clientDescription", "This client will lose access on its next authenticated request. Its other sessions and password stay unchanged.")),
            SecondaryText(L("web.settings.sessions.confirm.note", "A video already playing may continue until its next request. Saved device settings are kept.")), error } };
        var dialog = new ContentDialog { Title = current ? "Sign out this app?" : L("web.settings.sessions.confirm.deviceTitle", $"Sign out {LoginSessionDisplay.Name(session)}?", Args(("device", LoginSessionDisplay.Name(session)))),
            Content = content, PrimaryButtonText = L("common.signOut", "Sign out"), CloseButtonText = L("common.cancel", "Cancel"), DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot };
        var completed = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false; dialog.CloseButtonText = ""; dialog.PrimaryButtonText = L("web.settings.sessions.confirm.signingOut", "Signing out…");
            try
            {
                if (revision != _loginSessionRevision || !client.IsCurrentContext(context) || !CanRevokeLoginSession(session)) return;
                await App.Services.GetRequiredService<AuthApi>().RevokeSessionAsync(context, session.Id);
                if (revision != _loginSessionRevision || !client.IsCurrentContext(context)) return;
                completed = true; args.Cancel = false;
            }
            catch (OperationCanceledException) { dialog.Hide(); }
            catch (Exception ex) { error.Text = ex.DisplayMessage(); error.Visibility = Visibility.Visible; }
            finally { dialog.IsPrimaryButtonEnabled = true; dialog.CloseButtonText = L("common.cancel", "Cancel"); dialog.PrimaryButtonText = L("common.signOut", "Sign out"); deferral.Complete(); }
        };
        _loginSessionDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_loginSessionDialog, dialog)) _loginSessionDialog = null; }
        if (!completed || !client.IsCurrentContext(context)) return;
        if (current) await App.Services.GetRequiredService<AuthService>().LogoutAsync();
        else { Toast(L("web.settings.sessions.signedOut", "Session signed out")); await LoadSessionsAsync(); }
    }
}
