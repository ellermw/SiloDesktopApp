using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Views;

/// <summary>
/// Settings > API Keys, matched to the official WebUI page (silo-server web/src/pages/settings/ApiKeysSettings.tsx at
/// 3b2b32168, #4411): the keys the acting profile may see, creating a scoped key and revoking one. State and rules are
/// in <see cref="PersonalApiKeysModel"/>; this file only draws them. The secret of a new key lives in one text block of
/// the open dialog and nowhere else: it is never logged, stored or copied except by the user's Copy &amp; Close.
/// </summary>
public sealed partial class SettingsPage
{
    // Request text shown as code: header names and a path, the same in every language.
    private const string ApiKeysDocsPath = "/api/v2/docs";
    private const string ApiKeysProfilesRequest = "GET /api/v2/profiles";

    private PersonalApiKeysModel? _apiKeys;
    private ContentDialog? _apiKeyDialog;

    private PersonalApiKeysModel ApiKeysModel()
    {
        if (_apiKeys != null) return _apiKeys;
        var model = new PersonalApiKeysModel(App.Services.GetRequiredService<ApiKeysApi>(), App.Services.GetRequiredService<SiloApiClient>(),
            async ct => (await App.Services.GetRequiredService<AuthApi>().GetProfilesAsync(ct)).Profiles);
        model.Changed += () => { if (DispatcherQueue.HasThreadAccess) RenderApiKeys(); else DispatcherQueue.TryEnqueue(RenderApiKeys); };
        return _apiKeys = model;
    }

    private static PersonalApiKeysViewer ApiKeysViewer()
    {
        var auth = App.Services.GetRequiredService<AuthService>();
        var profile = auth.SelectedProfile;
        return new(profile?.Id ?? "", profile?.Name ?? "", profile?.IsPrimary == true,
            string.Equals(auth.CurrentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadApiKeysAsync() => await ApiKeysModel().LoadAsync(ApiKeysViewer());

    private void DeactivateApiKeys()
    {
        _apiKeys?.Deactivate();
        _apiKeyDialog?.Hide();
    }

    private void ApiKeysCreate_Click(object sender, RoutedEventArgs e) => _ = ShowCreateApiKeyDialogAsync();

    private bool _apiKeysWidthHooked;

    /// <summary>The settings column is as wide as its content, so this page claims a reading width (up to 768) to keep its header action and cards at one edge.</summary>
    private void UpdateApiKeysWidth()
    {
        if (!_apiKeysWidthHooked) { _apiKeysWidthHooked = true; SettingsLayoutGrid.SizeChanged += (_, _) => UpdateApiKeysWidth(); }
        var rail = Grid.GetColumn(SettingsContentPanel) == 1 ? 240 : 0;
        ApiKeysPanel.MinWidth = Math.Clamp(SettingsLayoutGrid.ActualWidth - rail - 48, 0, 768);
    }

    private void RenderApiKeys()
    {
        if (ApiKeysPanel.Visibility != Visibility.Visible || _apiKeys is not { } model) return;
        UpdateApiKeysWidth();
        ApiKeysContent.Children.Clear();
        // The official page shows only its loading line (no title) until the scopes and the first page have answered.
        ApiKeysHeader.Visibility = model.IsLoading ? Visibility.Collapsed : Visibility.Visible;
        ApiKeysCreateButton.Visibility = !model.IsLoading && model.CanCreate ? Visibility.Visible : Visibility.Collapsed;
        if (model.IsLoading)
        {
            ApiKeysContent.Children.Add(SecondaryText(L("web.settings.apiKeys.loading", "Loading API keys…")));
            return;
        }
        if (model.CapabilityFailed)
            ApiKeysContent.Children.Add(ApiKeysAlert(L("web.settings.apiKeys.loadFailed", "Couldn't load your API keys.")));
        else if (!model.Enabled && model.Viewer is { IsAdminAccount: false })
            ApiKeysContent.Children.Add(SecondaryText(L("web.settings.apiKeys.disabled", "This server doesn't let users create API keys. Ask its admin to turn that on.")));
        if (model.Enabled && model.AtLimit)
            ApiKeysContent.Children.Add(SecondaryText(L("web.settings.apiKeys.limitReached", $"You've reached the limit of {model.MaxKeys} keys. Revoke one to create another.",
                Args(("max", model.MaxKeys)))));

        var group = SettingsSurface();
        var stack = (StackPanel)group.Child;
        stack.Spacing = 12;
        stack.Children.Add(new TextBlock { Text = L("web.settings.apiKeys.list.heading", "Your keys"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = PrimaryBrush() });
        if (model.ListFailed)
            stack.Children.Add(ApiKeysAlert(L("web.settings.apiKeys.loadFailed", "Couldn't load your API keys.")));
        else if (model.Rows.Count == 0)
            stack.Children.Add(SecondaryText(L("web.settings.apiKeys.empty", "No API keys yet.")));
        else
        {
            var list = new StackPanel();
            for (var i = 0; i < model.Rows.Count; i++)
            {
                if (i > 0) list.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["BorderBrush"] });
                list.Children.Add(BuildApiKeyRow(model, model.Rows[i]));
            }
            stack.Children.Add(list);
        }
        if (model.HasNextPage)
        {
            var more = new Button { Content = L("web.settings.apiKeys.list.loadMore", "Load more"), IsEnabled = !model.IsFetchingNextPage, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetAutomationId(more, "ApiKeysLoadMore");
            more.Click += (_, _) => _ = model.LoadMoreAsync();
            stack.Children.Add(more);
        }
        ApiKeysContent.Children.Add(group);
    }

    private static TextBlock ApiKeysAlert(string text)
    {
        var alert = ErrorText(text);
        AutomationProperties.SetLiveSetting(alert, AutomationLiveSetting.Assertive);
        return alert;
    }

    private static string ApiKeyDate(string? value)
        => DateTimeOffset.TryParse(value, out var date) ? DateTimeDisplay.FormatDate(date.LocalDateTime, medium: true) : value ?? "";

    private FrameworkElement BuildApiKeyRow(PersonalApiKeysModel model, PersonalApiKey key)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 12, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // The second row holds Revoke when it wraps; a row index without a definition would fall back to the first row.
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var info = new StackPanel { Spacing = 4 };
        info.Children.Add(new TextBlock { Text = key.Label, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false });
        info.Children.Add(SecondaryText(string.IsNullOrEmpty(key.ProfileId)
            ? L("web.settings.apiKeys.list.wholeAccount", "Whole account")
            : L("web.settings.apiKeys.list.profile", $"Profile: {model.ProfileName(key.ProfileId)}", Args(("profile", model.ProfileName(key.ProfileId))))));
        var scopes = SecondaryText(string.Join(" ", key.Scopes));
        scopes.FontSize = 12;
        scopes.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        info.Children.Add(scopes);
        var dates = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 2 };
        var created = ApiKeyDate(key.CreatedAt);
        var lastUsed = string.IsNullOrEmpty(key.LastUsedAt) ? null : ApiKeyDate(key.LastUsedAt);
        var createdText = SecondaryText(L("web.settings.apiKeys.list.created", $"Created {created}", Args(("date", created))));
        createdText.FontSize = 12;
        var usedText = SecondaryText(lastUsed == null
            ? L("web.settings.apiKeys.list.neverUsed", "Never used")
            : L("web.settings.apiKeys.list.lastUsed", $"Last used {lastUsed}", Args(("date", lastUsed))));
        usedText.FontSize = 12;
        dates.Children.Add(createdText);
        dates.Children.Add(usedText);
        info.Children.Add(dates);
        row.Children.Add(info);
        var revoke = new Button
        {
            Content = L("web.settings.apiKeys.list.revoke", "Revoke"),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            VerticalAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(revoke, L("web.settings.apiKeys.list.revokeNamed", $"Revoke {key.Label}", Args(("label", key.Label))));
        revoke.Click += (_, _) => _ = ShowRevokeApiKeyDialogAsync(model, key);
        Grid.SetColumn(revoke, 1);
        row.Children.Add(revoke);
        // Too narrow for the button beside the text: it wraps beneath, as the official row does.
        row.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 420;
            row.ColumnDefinitions[1].Width = compact ? new GridLength(0) : GridLength.Auto;
            // The gap belongs to the wrapped button; beside the text the second row is empty and must add no height.
            row.RowSpacing = compact ? 8 : 0;
            Grid.SetRow(revoke, compact ? 1 : 0);
            Grid.SetColumn(revoke, compact ? 0 : 1);
            revoke.HorizontalAlignment = HorizontalAlignment.Left;
        };
        return row;
    }

    private static string ApiKeyScopeDescription(PersonalApiKeyScope scope) => scope.Name switch
    {
        // What each user scope allows, in the viewer's language. A scope the server adds later shows the server's own English description.
        "libraries:read" => L("web.settings.apiKeys.scope.librariesRead", "See which libraries the profile has."),
        "catalog:read" => L("web.settings.apiKeys.scope.catalogRead", "Browse what the profile can see: libraries, home and library rows, titles, people and search."),
        "watch:read" => L("web.settings.apiKeys.scope.watchRead", "Read the profile's watch history, progress, ratings, favorites, watchlist and hidden titles, and export its data."),
        "watch:write" => L("web.settings.apiKeys.scope.watchWrite", "Mark titles watched or unwatched, sync progress, remove history, and edit ratings, favorites, watchlist and hidden titles."),
        "requests" => L("web.settings.apiKeys.scope.requests", "Browse requestable titles and create, follow and cancel media requests."),
        "notifications" => L("web.settings.apiKeys.scope.notifications", "Read the profile's notifications as they arrive and mark them read."),
        _ => scope.Description,
    };

    private static Border ApiKeyCode(string text, out TextBlock block)
    {
        block = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = PrimaryBrush(),
        };
        return new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = block,
        };
    }

    private async Task ShowCreateApiKeyDialogAsync()
    {
        if (_apiKeyDialog != null || _apiKeys is not { CanCreate: true, Viewer: { } viewer } model) return;
        var scopes = model.UserScopes;
        var label = new TextBox { Header = L("web.settings.apiKeys.create.label", "Label"), MaxLength = 4096, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(label, "ApiKeyLabel");
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(SecondaryText(L("web.settings.apiKeys.create.description", "The full key is shown only once.")));
        body.Children.Add(label);

        // Only the primary profile may choose; every other profile's key is its own.
        ComboBox? target = null;
        if (viewer.IsPrimary)
        {
            target = new ComboBox { Header = L("web.settings.apiKeys.create.actsAs", "Acts as"), HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(target, "ApiKeyTarget");
            target.Items.Add(new ComboBoxItem { Content = L("web.settings.apiKeys.list.wholeAccount", "Whole account"), Tag = PersonalApiKeysModel.WholeAccount });
            foreach (var profile in model.Profiles) target.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
            target.SelectedIndex = 0;
            var actsAs = new StackPanel { Spacing = 6 };
            actsAs.Children.Add(target);
            actsAs.Children.Add(SecondaryText(L("web.settings.apiKeys.create.actsAsHint",
                "A key for one profile always acts as that profile and keeps its limits. A whole-account key can act as any profile on your account.")));
            body.Children.Add(actsAs);
        }

        var access = new StackPanel { Spacing = 12 };
        access.Children.Add(new TextBlock { Text = L("web.settings.apiKeys.create.access", "Access"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = PrimaryBrush() });
        var chosen = new List<string>();
        var checks = new List<CheckBox>();
        var error = ApiKeysAlert("");
        error.Visibility = Visibility.Collapsed;
        ContentDialog? dialogRef = null;
        foreach (var scope in scopes)
        {
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = scope.Name, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(SecondaryText(ApiKeyScopeDescription(scope)));
            var check = new CheckBox { Content = text, Tag = scope.Name, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(check, scope.Name);
            AutomationProperties.SetFullDescription(check, ApiKeyScopeDescription(scope));
            AutomationProperties.SetAutomationId(check, "ApiKeyScope-" + scope.Name);
            check.Checked += (_, _) => { if (!chosen.Contains(scope.Name)) chosen.Add(scope.Name); if (dialogRef != null) dialogRef.IsPrimaryButtonEnabled = chosen.Count > 0 && !model.IsCreating; };
            check.Unchecked += (_, _) => { chosen.Remove(scope.Name); if (dialogRef != null) dialogRef.IsPrimaryButtonEnabled = chosen.Count > 0 && !model.IsCreating; };
            checks.Add(check);
            access.Children.Add(check);
        }
        access.Children.Add(SecondaryText(L("web.settings.apiKeys.create.accessHint", "Choose at least one. No key can play, stream or download media.")));
        body.Children.Add(access);
        body.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = L("web.settings.apiKeys.create.title", "Create API Key"),
            Content = body,
            PrimaryButtonText = L("web.settings.apiKeys.create.submit", "Create"),
            CloseButtonText = L("common.cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot,
        };
        dialogRef = dialog;
        var pending = false;
        var uncertain = false;
        TextBlock? secretText = null;
        // The dialog cannot be dismissed while the create is in flight: its answer carries the only copy of the key.
        dialog.Closing += (_, args) => { if (pending) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (pending || uncertain || chosen.Count == 0) return;
            if (string.IsNullOrEmpty(label.Text)) { label.Focus(FocusState.Programmatic); return; }
            var deferral = args.GetDeferral();
            pending = true;
            error.Visibility = Visibility.Collapsed;
            label.IsEnabled = false;
            if (target != null) target.IsEnabled = false;
            foreach (var check in checks) check.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.IsSecondaryButtonEnabled = false;
            dialog.CloseButtonText = "";
            dialog.PrimaryButtonText = L("web.settings.apiKeys.create.creating", "Creating…");
            var submittedTarget = target?.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : viewer.ProfileId;
            PersonalApiKeyCreateResult result;
            try { result = await model.CreateAsync(label.Text, chosen.ToArray(), submittedTarget); }
            finally { pending = false; }
            try
            {
                switch (result.Status)
                {
                    case PersonalApiKeyCreateStatus.Created:
                        secretText = ShowApiKeySecret(dialog, model, result.Secret!, result.Target ?? submittedTarget, viewer);
                        break;
                    case PersonalApiKeyCreateStatus.Dropped:
                        args.Cancel = false;
                        break;
                    default:
                        // A refusal says what was wrong; anything else may still have made a key, so it is not offered again.
                        uncertain = result.Status == PersonalApiKeyCreateStatus.Uncertain;
                        error.Text = result.Status == PersonalApiKeyCreateStatus.Refused && !string.IsNullOrEmpty(result.Message)
                            ? result.Message
                            : L("web.settings.apiKeys.create.uncertain", "The result is uncertain. Reload the page before creating another key.");
                        error.Visibility = Visibility.Visible;
                        label.IsEnabled = !uncertain;
                        if (target != null) target.IsEnabled = !uncertain;
                        foreach (var check in checks) check.IsEnabled = !uncertain;
                        dialog.CloseButtonText = L("common.cancel", "Cancel");
                        dialog.PrimaryButtonText = L("web.settings.apiKeys.create.submit", "Create");
                        dialog.IsPrimaryButtonEnabled = !uncertain && chosen.Count > 0;
                        break;
                }
            }
            finally { deferral.Complete(); }
        };
        _apiKeyDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally
        {
            // The secret leaves the screen with the dialog.
            if (secretText != null) secretText.Text = "";
            label.Text = "";
            if (ReferenceEquals(_apiKeyDialog, dialog)) _apiKeyDialog = null;
        }
    }

    /// <summary>Swaps the form for the one-time view of the new key. Returns the text block holding the secret so the caller can wipe it.</summary>
    private TextBlock ShowApiKeySecret(ContentDialog dialog, PersonalApiKeysModel model, string secret, string createdFor, PersonalApiKeysViewer viewer)
    {
        var view = new StackPanel { Spacing = 16 };
        view.Children.Add(new TextBlock { Text = L("web.settings.apiKeys.create.secretNotice", "Copy your API key now. You can't see it again."), TextWrapping = TextWrapping.Wrap });
        var code = ApiKeyCode(secret, out var secretText);
        AutomationProperties.SetAutomationId(secretText, "ApiKeySecret");
        view.Children.Add(code);
        // What the key shown was created for: the usage text must describe the key made, not a form that may have changed.
        var boundTo = createdFor == PersonalApiKeysModel.WholeAccount ? null : model.ProfileName(createdFor);
        view.Children.Add(BuildApiKeyUsage(secret, boundTo, viewer));
        var error = ApiKeysAlert("");
        error.Visibility = Visibility.Collapsed;
        view.Children.Add(error);
        var copy = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = L("web.settings.apiKeys.create.copyAndClose", "Copy & Close") } },
            },
        };
        AutomationProperties.SetAutomationId(copy, "ApiKeyCopy");
        AutomationProperties.SetName(copy, L("web.settings.apiKeys.create.copyAndClose", "Copy & Close"));
        copy.Click += (_, _) =>
        {
            // The key is shown once, so a failed copy leaves the dialog open.
            try
            {
                var package = new DataPackage();
                package.SetText(secret);
                Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false });
            }
            catch
            {
                error.Text = L("web.settings.apiKeys.create.copyFailed", "Couldn't copy. Select the key and copy it manually.");
                error.Visibility = Visibility.Visible;
                return;
            }
            Toast(L("web.settings.apiKeys.create.copied", "Copied to clipboard"));
            dialog.Hide();
        };
        view.Children.Add(copy);
        dialog.Content = view;
        dialog.PrimaryButtonText = "";
        dialog.IsPrimaryButtonEnabled = false;
        dialog.CloseButtonText = L("common.close", "Close");
        dialog.DefaultButton = ContentDialogButton.Close;
        return secretText;
    }

    /// <summary>
    /// How to send the key just created. A key tied to one profile needs nothing else; a whole-account key also names the
    /// profile it acts as, shown with the acting profile's id as the example.
    /// </summary>
    private FrameworkElement BuildApiKeyUsage(string secret, string? boundTo, PersonalApiKeysViewer example)
    {
        var usage = new StackPanel { Spacing = 8 };
        usage.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["BorderBrush"], Margin = new Thickness(0, 0, 0, 8) });
        usage.Children.Add(new TextBlock { Text = L("web.settings.apiKeys.usage.title", "How to use this key"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium });
        usage.Children.Add(SecondaryText(L("web.settings.apiKeys.usage.header", "Send it with every request in this header:")));
        // Enough of the key to recognize it; the full key is shown once, above.
        usage.Children.Add(ApiKeyCode($"Authorization: Bearer {secret[..Math.Min(11, secret.Length)]}…", out _));
        if (boundTo != null)
            usage.Children.Add(SecondaryText(L("web.settings.apiKeys.usage.profileKey", $"This key always acts as {boundTo}. It needs no profile header.", Args(("profile", boundTo)))));
        else
        {
            usage.Children.Add(SecondaryText(L("web.settings.apiKeys.usage.accountKey",
                $"This key can act as any profile on your account. Choose one per request with this header. The example uses {example.ProfileName}; {ApiKeysProfilesRequest} lists every profile's ID.",
                Args(("profile", example.ProfileName), ("endpoint", ApiKeysProfilesRequest)))));
            usage.Children.Add(ApiKeyCode($"X-Profile-Id: {example.ProfileId}", out _));
        }
        var docs = new HyperlinkButton { Content = L("web.settings.apiKeys.usage.docs", "API reference"), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(docs, "ApiKeysDocs");
        if (ExternalBrowserUrlPolicy.TryGetSafeUri(App.Services.GetRequiredService<SiloApiClient>().BaseUrl.TrimEnd('/') + ApiKeysDocsPath, out var docsUri))
            docs.NavigateUri = docsUri;
        usage.Children.Add(docs);
        return usage;
    }

    private async Task ShowRevokeApiKeyDialogAsync(PersonalApiKeysModel model, PersonalApiKey key)
    {
        if (_apiKeyDialog != null) return;
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = L("web.settings.apiKeys.revoke.description", $"{key.Label} stops working at once. This can't be undone.", Args(("label", key.Label))),
            TextWrapping = TextWrapping.Wrap,
        });
        var failed = ApiKeysAlert(L("web.settings.apiKeys.revoke.failed", "Couldn't revoke the key. Reload the page and try again."));
        failed.Visibility = Visibility.Collapsed;
        body.Children.Add(failed);
        var dialog = new ContentDialog
        {
            Title = L("web.settings.apiKeys.revoke.title", "Revoke API key"),
            Content = body,
            PrimaryButtonText = L("web.settings.apiKeys.list.revoke", "Revoke"),
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = L("common.cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        var pending = false;
        dialog.Closing += (_, args) => { if (pending) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (pending) return;
            var deferral = args.GetDeferral();
            pending = true;
            failed.Visibility = Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.CloseButtonText = "";
            dialog.PrimaryButtonText = L("web.settings.apiKeys.revoke.working", "Revoking…");
            PersonalApiKeyRevokeStatus status;
            try { status = await model.RevokeAsync(key.Id); }
            finally { pending = false; }
            try
            {
                if (status == PersonalApiKeyRevokeStatus.Revoked)
                {
                    Toast(L("web.settings.apiKeys.revoke.done", "API key revoked"));
                    args.Cancel = false;
                }
                else if (status == PersonalApiKeyRevokeStatus.Dropped) args.Cancel = false;
                else
                {
                    failed.Visibility = Visibility.Visible;
                    dialog.IsPrimaryButtonEnabled = true;
                    dialog.CloseButtonText = L("common.cancel", "Cancel");
                    dialog.PrimaryButtonText = L("web.settings.apiKeys.list.revoke", "Revoke");
                }
            }
            finally { deferral.Complete(); }
        };
        _apiKeyDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_apiKeyDialog, dialog)) _apiKeyDialog = null; }
    }
}
