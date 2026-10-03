using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.HistoryImport;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class AccountSettingsNativeFixture
{
    private static readonly PasswordHandler Handler = new();

    internal static IServiceCollection AddAccountSettingsFixture(this IServiceCollection services)
    {
        var client = new SiloApiClient(new HttpClient(Handler));
        client.SetBaseUrl("https://account-fixture.invalid");
        var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("account-fixture-token", "account-fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("primary-fixture", profile: new() { Id = "primary-fixture", Name = "Primary", IsPrimary = true });
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "account-settings"));
        var api = new SettingsApi(client);
        var theme = new ThemeService(settings, api);
        return services.AddSingleton(client)
            .AddTransient(_ => new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client),
                new HistoryImportApi(client), new WatchProvidersApi(client),
                auth, theme, settings,
                new AccessibilityService(settings, theme)));
    }

    internal static async Task RunAsync(StackPanel parent)
    {
        // Construct the real settings XAML without navigating: the general settings
        // loads do not run, and the fixture handler accepts only password routes.
        var page = new SettingsPage { Width = 900, Height = 650 };
        parent.Children.Add(page);
        await Task.Delay(100);
        var vm = (AccountPasswordViewModel)typeof(SettingsPage)
            .GetField("_accountPassword", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        var accountTab = (Button)page.FindName("AccountPasswordTab");
        void OpenAccount() => Invoke(page, "ShowSettingsDetail", accountTab);
        OpenAccount();
        await UntilAsync(() => !vm.IsLoading);
        var form = (StackPanel)page.FindName("AccountPasswordForm");
        var current = (PasswordBox)page.FindName("AccountCurrentPassword");
        var replacement = (PasswordBox)page.FindName("AccountNewPassword");
        var confirmation = (PasswordBox)page.FindName("AccountConfirmPassword");
        var message = (InfoBar)page.FindName("AccountPasswordMessage");
        if (form.Visibility != Visibility.Visible || !vm.CanChangePassword)
            throw new InvalidOperationException("Permitted primary profile cannot see password form.");
        current.Password = "fixture old password";
        replacement.Password = confirmation.Password = "fixture new password";
        await Task.Delay(100);
        if (current.Password.Length != 20 || replacement.Password.Length != 20 || confirmation.Password.Length != 20 ||
            vm.CurrentPassword != current.Password || vm.NewPassword != replacement.Password || vm.ConfirmPassword != confirmation.Password)
            throw new InvalidOperationException("PasswordBox input did not reach the account form.");

        Handler.PostStatus = HttpStatusCode.UnprocessableEntity;
        Invoke(page, "AccountPasswordSubmit_Click", page.FindName("AccountPasswordSubmit"), new RoutedEventArgs());
        await UntilAsync(() => !vm.IsSubmitting);
        if (!message.IsOpen || message.Severity != InfoBarSeverity.Error || current.Password.Length == 0)
            throw new InvalidOperationException("Password failure was not visible or discarded retry input.");

        Handler.PostStatus = HttpStatusCode.NoContent;
        Handler.PostPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke(page, "AccountPasswordSubmit_Click", page.FindName("AccountPasswordSubmit"), new RoutedEventArgs());
        if (current.IsEnabled || replacement.IsEnabled || confirmation.IsEnabled || !vm.IsSubmitting)
            throw new InvalidOperationException("Password form remains enabled while a change is pending.");
        Handler.PostPending.SetResult(new(HttpStatusCode.NoContent));
        await UntilAsync(() => !vm.IsSubmitting);
        Handler.PostPending = null;
        if (current.Password.Length != 0 || replacement.Password.Length != 0 || confirmation.Password.Length != 0 ||
            !message.IsOpen || message.Severity != InfoBarSeverity.Success)
            throw new InvalidOperationException("Successful change did not clear the real PasswordBoxes or show success.");

        Handler.Allowed = false;
        OpenAccount();
        await UntilAsync(() => !vm.IsLoading);
        if (form.Visibility != Visibility.Collapsed ||
            ((FrameworkElement)page.FindName("AccountPasswordUnavailable")).Visibility != Visibility.Visible)
            throw new InvalidOperationException("Restricted account did not hide the password form.");

        Handler.Allowed = true;
        OpenAccount();
        await UntilAsync(() => !vm.IsLoading);
        await HistoryImportAsync(page);
        current.Password = replacement.Password = confirmation.Password = "fixture departure password";
        parent.Children.Remove(page);
        await Task.Delay(150);
        if (current.Password.Length != 0 || replacement.Password.Length != 0 || confirmation.Password.Length != 0 ||
            vm.CurrentPassword.Length != 0 || vm.NewPassword.Length != 0 || vm.ConfirmPassword.Length != 0)
            throw new InvalidOperationException("Departed settings page retained password input.");
    }

    private static async Task HistoryImportAsync(SettingsPage page)
    {
        var vm = page.ViewModel;
        vm.ImportProfileId = "primary-fixture";
        vm.ImportSourceType = "emby";
        vm.ImportEmbyMode = "connect";
        vm.EmbyConnectSessionId = "connect-fixture";
        vm.EmbyConnectUsername = "fixture user";
        var server = new HistoryImportConnectServer { ServerId = "server-fixture", Name = "Fixture server" };
        vm.EmbyConnectServers.Add(server);
        vm.SelectedEmbyConnectServer = server;
        var password = (PasswordBox)page.FindName("EmbyConnectPasswordBox");
        password.Password = "fixture connect password";
        var servers = (ComboBox)page.FindName("EmbyConnectServerCombo");
        servers.Items.Add(new ComboBoxItem { Content = server.Name, Tag = server });
        servers.SelectedIndex = 0;
        var start = (Button)page.FindName("StartImportButton");
        Invoke(page, "UpdateImportPanelVisibility");
        if (!start.IsEnabled) throw new InvalidOperationException("Authorized Connect import cannot start.");

        Handler.ImportStatus = HttpStatusCode.BadGateway;
        Invoke(page, "StartImport_Click", start, new RoutedEventArgs());
        await UntilAsync(() => !vm.IsImporting);
        if (!start.IsEnabled || vm.EmbyConnectSessionId != "connect-fixture" ||
            vm.SelectedEmbyConnectServer != server || password.Password != "fixture connect password")
            throw new InvalidOperationException($"Failed import creation consumed usable Connect authorization: start={start.IsEnabled}, sessionPresent={!string.IsNullOrEmpty(vm.EmbyConnectSessionId)}, serverSame={vm.SelectedEmbyConnectServer == server}, inputLength={password.Password.Length}, modelLength={vm.EmbyConnectPassword.Length}.");

        Handler.ImportStatus = HttpStatusCode.Created;
        Invoke(page, "StartImport_Click", start, new RoutedEventArgs());
        await UntilAsync(() => !vm.IsImporting);
        if (start.IsEnabled || vm.CanStartImport || vm.EmbyConnectSessionId is not null ||
            vm.SelectedEmbyConnectServer is not null || vm.EmbyConnectServers.Count != 0 ||
            password.Password.Length != 0 || servers.Items.Count != 0 || vm.EmbyConnectUsername != "fixture user")
            throw new InvalidOperationException("Successful import did not clear only its consumed authorization and disable Start.");

        var summary = (StackPanel)page.FindName("RunSummaryContainer");
        foreach (var status in new[] { "queued", "running", "completed" })
        {
            vm.DisplayRun = new HistoryImportRun
            {
                Id = "progress-fixture", SourceType = "emby", Status = status,
                Fetched = 100, Matched = 60, Unmatched = 10, Skipped = 40
            };
            Invoke(page, "RebuildRunSummaryCard");
            var bars = summary.Children.OfType<ProgressBar>().ToList();
            if (status == "completed")
            {
                if (bars.Count != 0) throw new InvalidOperationException("Completed import still displays an active progress bar.");
            }
            else if (bars.Count != 1 || bars[0].Value != 70 ||
                !summary.Children.OfType<TextBlock>().Any(text => text.Text == "70 / 100 processed"))
                throw new InvalidOperationException($"{status} import double-counted skipped matches.");
            var text = Descendants(summary).OfType<TextBlock>().Select(block => block.Text).ToList();
            if (!text.Contains("Skipped") || !text.Contains("40"))
                throw new InvalidOperationException("Skipped metric disappeared from import summary.");
        }
        vm.DisplayRun = new HistoryImportRun { Status = "running" };
        Invoke(page, "RebuildRunSummaryCard");
        if (summary.Children.OfType<ProgressBar>().Any())
            throw new InvalidOperationException("Zero-fetched import displayed a misleading determinate bar.");
    }

    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement root)
    {
        yield return root;
        var children = root switch
        {
            Panel panel => panel.Children.OfType<FrameworkElement>(),
            Border { Child: FrameworkElement child } => new[] { child },
            _ => Enumerable.Empty<FrameworkElement>()
        };
        foreach (var child in children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static void Invoke(SettingsPage page, string name, params object[] arguments)
        => typeof(SettingsPage).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, arguments);

    private static async Task UntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++) await Task.Delay(20);
        if (!done()) throw new TimeoutException("Account settings fixture did not settle.");
    }

    private sealed class PasswordHandler : HttpMessageHandler
    {
        public bool Allowed = true;
        public HttpStatusCode PostStatus = HttpStatusCode.NoContent;
        public HttpStatusCode ImportStatus = HttpStatusCode.Created;
        public TaskCompletionSource<HttpResponseMessage>? PostPending;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "account-fixture.invalid" ||
                request.Headers.GetValues("X-Profile-Id").Single() != "primary-fixture")
                throw new InvalidOperationException("Account fixture used an unexpected server/profile.");
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/v2/account/password/capability")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"state":"available","allowed":{{Allowed.ToString().ToLowerInvariant()}},"requires_current_password":true,"minimum_password_length":8,"maximum_password_bytes":72}""")
                });
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/v2/account/password")
                return PostPending?.Task ?? Task.FromResult(new HttpResponseMessage(PostStatus)
                { Content = new StringContent("""{"detail":"Current password is incorrect."}""") });
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/v2/history-imports/runs")
                return Task.FromResult(new HttpResponseMessage(ImportStatus)
                { Content = new StringContent(ImportStatus == HttpStatusCode.Created
                    ? """{"id":"run-fixture","source_type":"emby","status":"queued"}"""
                    : """{"detail":"Fixture import unavailable."}""") });
            throw new InvalidOperationException("Unexpected account fixture request.");
        }
    }
}
