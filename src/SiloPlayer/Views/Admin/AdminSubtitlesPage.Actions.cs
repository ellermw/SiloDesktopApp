using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Windows.Storage.Pickers;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Row actions: the Edit-subtitle side panel (WebUI <c>AdminSubtitleEditSheet</c>),
/// the delete confirmation (<c>AdminSubtitleDeleteDialog</c>), the bounded
/// download and the unsaved-edit leave confirmation.
/// </summary>
public sealed partial class AdminSubtitlesPage
{
    private AdminSubtitleEditorSession? _editor;
    private EditorView? _editorView;
    private AdminSubtitleDeletionSession? _deletion;
    private ContentDialog? _deleteDialog;
    private ContentDialog? _discardDialog;
    private CancellationTokenSource? _download;

    /// <summary>The open editor session (fixtures).</summary>
    public AdminSubtitleEditorSession? Editor => _editor;

    // ── Edit ─────────────────────────────────────────────────────────────

    private void BeginEdit(AdminStoredSubtitle row)
    {
        var controller = _controller;
        if (controller == null || _editor != null || _deleteDialog != null) return;
        AdminSubtitleEditorSession session;
        try { session = controller.BeginEdit(row); }
        catch (OperationCanceledException) { _feedback.Show(S("web.admin.common.subtitles.table.reloadBeforeEdit"), null); return; }
        _editor = session;
        _editorView = new EditorView(this, session);
        session.Changed += QueueEditorRender;
        _sheet.Show(S("web.admin.common.subtitles.edit.title"), S("web.admin.common.subtitles.edit.loadHint"), _editorView.Root);
        _editorView.Render(session.State);
        _ = session.LoadAsync();
    }

    private void QueueEditorRender()
    {
        var session = _editor;
        EnqueueUi(() =>
        {
            if (!IsLoaded || session == null || !ReferenceEquals(session, _editor) || _editorView == null) return;
            var state = session.State;
            if (state.Saved || state.Retired) { CloseEditor(); return; }
            _editorView.Render(state);
            _sheet.Update(S("web.admin.common.subtitles.edit.title"),
                S(state.Current == null ? "web.admin.common.subtitles.edit.loadHint" : "web.admin.common.subtitles.edit.description"), _editorView.Root);
        });
    }

    private void CloseEditor()
    {
        if (_sheet.Visibility == Visibility.Visible) _sheet.Close();
        else DisposeEditor();
    }

    private void OnSheetClosed() => DisposeEditor();

    private void DisposeEditor()
    {
        var session = _editor;
        _editor = null; _editorView = null;
        if (session == null) return;
        session.Changed -= QueueEditorRender;
        // A save that was already sent settles first; its outcome still reaches the list and toast.
        session.Dispose();
    }

    /// <summary>The editor's controls, built once so typing keeps focus and caret while state re-renders.</summary>
    private sealed class EditorView
    {
        private readonly AdminSubtitlesPage _page;
        private readonly AdminSubtitleEditorSession _session;
        private readonly TextBlock _status = Text("", 14, true);
        private readonly Button _retry;
        private readonly StackPanel _form = Stack(24);
        private readonly TextBlock _mediaTitle = Text("", 14);
        private readonly StackPanel _chips = Inline(8);
        private readonly ComboBox _language;
        private readonly TextBlock _languageHint = Text(S("web.admin.common.subtitles.edit.languageChanged"), 12, true);
        private readonly TextBox _release;
        private readonly TextBlock _validation = Text("", 12);
        private readonly ToggleSwitch _hearing = new() { OnContent = "", OffContent = "", MinWidth = 40 };
        private readonly TextBlock _failure = Text("", 14);
        private readonly Button _reload;
        private readonly Button _save;
        private readonly Button _cancel;
        private bool _syncing;

        internal StackPanel Root { get; } = Stack(16);

        internal EditorView(AdminSubtitlesPage page, AdminSubtitleEditorSession session)
        {
            _page = page; _session = session;
            AutomationProperties.SetAutomationId(Root, "SubtitlesEditor");
            AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            AutomationProperties.SetAutomationId(_status, "SubtitlesEditStatus");
            Root.Children.Add(_status);
            _retry = Button(S("common.retry"), "SubtitlesEditRetry");
            _retry.HorizontalAlignment = HorizontalAlignment.Left; _retry.Click += (_, _) => _ = _session.LoadAsync();
            Root.Children.Add(_retry);

            var summary = Stack(12); _mediaTitle.FontWeight = FontWeights.SemiBold; summary.Children.Add(_mediaTitle); summary.Children.Add(_chips);
            var summaryPanel = Panel(summary, 12, 16); summaryPanel.Background = Tint("MutedBrush", 0.25); summaryPanel.BorderThickness = new(0);
            _form.Children.Add(summaryPanel);

            _language = Select("SubtitlesEditLanguage", AdminSubtitlesText.LanguageOptions().Select(option => (option.Code, AdminSubtitlesText.LanguageCode(option.Code, option.Label))), 320);
            _language.Width = double.NaN; _language.HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(_language, S("web.admin.common.subtitles.edit.language"));
            _language.SelectionChanged += (_, _) => { if (!_syncing && Selected(_language) is { Length: > 0 } code) _session.SetLanguage(code); };
            _form.Children.Add(Field(S("web.admin.common.subtitles.edit.language"), _language, _languageHint));

            _release = Input("", "SubtitlesEditRelease", 320, mono: true);
            _release.Width = double.NaN; _release.HorizontalAlignment = HorizontalAlignment.Stretch; _release.FontSize = 12;
            AutomationProperties.SetName(_release, S("web.admin.common.subtitles.edit.releaseName"));
            _release.TextChanged += (_, _) => { if (!_syncing) _session.SetReleaseName(_release.Text); };
            _validation.Foreground = Brush("ErrorBrush");
            _form.Children.Add(Field(S("web.admin.common.subtitles.edit.releaseName"), _release, _validation));

            var hearingText = Stack(4);
            var hearingLabel = Text(S("web.admin.common.subtitles.edit.hearingImpaired"), 14); hearingLabel.FontWeight = FontWeights.Medium;
            hearingText.Children.Add(hearingLabel); hearingText.Children.Add(Text(S("web.admin.common.subtitles.edit.hearingImpairedHelp"), 12, true));
            AutomationProperties.SetName(_hearing, S("web.admin.common.subtitles.edit.hearingImpaired")); AutomationProperties.SetAutomationId(_hearing, "SubtitlesEditHearing");
            _hearing.Toggled += (_, _) => { if (!_syncing) _session.SetHearingImpaired(_hearing.IsOn); };
            var hearingRow = new Grid { ColumnSpacing = 12 }; hearingRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); hearingRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            hearingRow.Children.Add(hearingText); Grid.SetColumn(_hearing, 1); _hearing.VerticalAlignment = VerticalAlignment.Center; hearingRow.Children.Add(_hearing);
            _form.Children.Add(new Border { Child = hearingRow, BorderBrush = Tint("BorderBrush", 0.6), BorderThickness = new(1), CornerRadius = new(12), Padding = new(16, 12, 16, 12) });
            Root.Children.Add(_form);

            _failure.Foreground = Brush("ErrorBrush");
            AutomationProperties.SetLiveSetting(_failure, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
            AutomationProperties.SetAutomationId(_failure, "SubtitlesEditFailure");
            Root.Children.Add(_failure);

            var footer = Inline(8); footer.HorizontalAlignment = HorizontalAlignment.Right; footer.Margin = new(0, 8, 0, 0);
            _cancel = Button(S("common.cancel"), "SubtitlesEditCancel"); _cancel.Click += (_, _) => _page.CloseEditor(); footer.Children.Add(_cancel);
            _reload = Button(S("admin.native.subtitles.reload"), "SubtitlesEditReload"); _reload.Click += (_, _) => _ = _session.LoadAsync(); footer.Children.Add(_reload);
            _save = Button(S("web.admin.common.subtitles.edit.saveChanges"), "SubtitlesEditSave", ""); _save.Click += (_, _) => _ = _session.SaveAsync(); footer.Children.Add(_save);
            Root.Children.Add(footer);
        }

        private static StackPanel Field(string label, FrameworkElement control, TextBlock hint)
        {
            var field = Stack(8); var title = Text(label, 14); title.FontWeight = FontWeights.Medium;
            field.Children.Add(title); field.Children.Add(control); hint.Visibility = Visibility.Collapsed; field.Children.Add(hint);
            return field;
        }

        internal void Render(AdminSubtitleEditorState state)
        {
            var loaded = state.Current != null;
            _status.Visibility = loaded && !state.Loading && state.LoadError == null ? Visibility.Collapsed : Visibility.Visible;
            _status.Text = state.LoadError ?? S("web.admin.common.subtitles.edit.loading");
            _status.Foreground = Brush(state.LoadError != null ? "ErrorBrush" : "SecondaryTextBrush");
            _retry.Visibility = !loaded && state.LoadError != null ? Visibility.Visible : Visibility.Collapsed;
            _retry.IsEnabled = !state.Loading;
            _form.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
            if (!loaded) { _failure.Visibility = Visibility.Collapsed; _save.Visibility = _reload.Visibility = Visibility.Collapsed; return; }
            var current = state.Current!.Value;

            _syncing = true;
            try
            {
                _mediaTitle.Text = string.IsNullOrEmpty(state.Row.MediaTitle) ? S("web.admin.common.subtitles.unknownMedia") : state.Row.MediaTitle;
                _chips.Children.Clear();
                var language = Text(AdminSubtitlesText.LanguageCode(current.Language), 12); language.FontWeight = FontWeights.Medium;
                _chips.Children.Add(Chip(language, Tint("AccentBrush", 0.10), Tint("AccentBrush", 0.25)));
                var provider = Text(AdminSubtitlesText.ProviderLabel(current.Provider), 12); provider.FontWeight = FontWeights.Medium;
                var tone = AdminSubtitlesText.ProviderTone(current.Provider);
                _chips.Children.Add(tone == 0 ? Chip(provider, Tint("MutedBrush", 0.40), Tint("BorderBrush", 0.70)) : Chip(provider, ToneBrush(tone, 0.12), ToneBrush(tone, 0.32)));

                if (Selected(_language) != state.Language)
                {
                    if (!_language.Items.Cast<ComboBoxItem>().Any(item => (string)item.Tag == state.Language))
                        _language.Items.Add(new ComboBoxItem { Content = AdminSubtitlesText.LanguageCode(state.Language), Tag = state.Language });
                    SetSelected(_language, state.Language);
                }
                if (_release.Text != state.ReleaseName) _release.Text = state.ReleaseName;
                if (_hearing.IsOn != state.HearingImpaired) _hearing.IsOn = state.HearingImpaired;
            }
            finally { _syncing = false; }

            var editable = !state.Saving && !state.Saved && !state.Retired;
            _language.IsEnabled = _release.IsEnabled = _hearing.IsEnabled = editable;
            _languageHint.Visibility = state.LanguageChanged ? Visibility.Visible : Visibility.Collapsed;
            _validation.Text = state.Validation ?? "";
            _validation.Visibility = state.Validation != null ? Visibility.Visible : Visibility.Collapsed;
            var failure = state.Failure ?? state.LoadError;
            _failure.Text = failure == null ? "" : S("web.admin.common.subtitles.edit.failureHint", ("error", failure));
            _failure.Visibility = failure != null ? Visibility.Visible : Visibility.Collapsed;
            _save.Visibility = Visibility.Visible;
            _save.IsEnabled = state.CanSave;
            _reload.Visibility = state.CanReload ? Visibility.Visible : Visibility.Collapsed;
            _reload.IsEnabled = state.CanReload;
        }
    }

    // ── Delete ───────────────────────────────────────────────────────────

    private async Task ConfirmDeleteAsync(AdminStoredSubtitle row)
    {
        var controller = _controller;
        if (controller == null || _deleteDialog != null || _discardDialog != null || _editor != null || XamlRoot == null) return;
        AdminSubtitleDeletionSession session;
        try { session = controller.BeginDelete(row); }
        catch (OperationCanceledException) { _feedback.Show(S("admin.native.subtitles.retired"), null); return; }
        _deletion = session;
        var description = Text(S("web.admin.common.subtitles.delete.loading"), 14, true);
        AutomationProperties.SetAutomationId(description, "SubtitlesDeleteDescription");
        var error = Text("", 14); error.Foreground = Brush("ErrorBrush"); error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(error, "SubtitlesDeleteError");
        AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
        var content = Stack(12); content.Children.Add(description); content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = S("web.admin.common.subtitles.delete.title"), Content = content,
            PrimaryButtonText = S("web.admin.common.subtitles.delete.confirm"), CloseButtonText = S("web.admin.common.subtitles.delete.close"),
            DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
        };
        _deleteDialog = dialog;
        void Sync()
        {
            var state = session.State;
            description.Text = state.Reviewed is { } reviewed
                ? S("web.admin.common.subtitles.delete.description", ("language", reviewed.Value.Language.ToUpperInvariant()),
                    ("name", string.IsNullOrEmpty(reviewed.Value.ReleaseName) ? reviewed.Value.Id : reviewed.Value.ReleaseName))
                : S("web.admin.common.subtitles.delete.loading");
            error.Text = state.Error == null ? "" : S("web.admin.common.subtitles.delete.errorHint", ("error", state.Error));
            error.Visibility = state.Error == null ? Visibility.Collapsed : Visibility.Visible;
            dialog.IsPrimaryButtonEnabled = state.CanConfirm;
            if (state.Deleted || state.Retired) dialog.Hide();
        }
        session.Changed += () => EnqueueUi(() => { if (ReferenceEquals(_deletion, session)) Sync(); });
        // The dialog stays open while the one DELETE runs; it closes on confirmed deletion.
        dialog.PrimaryButtonClick += (sender, args) => { args.Cancel = true; _ = session.ConfirmAsync(); };
        using var hide = session.ActionLease.Token.Register(() => EnqueueUi(() => dialog.Hide()));
        Sync();
        _ = session.PrepareAsync();
        try { await dialog.ShowAsync(); }
        catch (Exception) { }
        finally
        {
            if (ReferenceEquals(_deleteDialog, dialog)) _deleteDialog = null;
            if (ReferenceEquals(_deletion, session)) _deletion = null;
            session.Dispose();
        }
    }

    // ── Download ─────────────────────────────────────────────────────────

    private async Task DownloadAsync(AdminStoredSubtitle row)
    {
        var controller = _controller;
        if (controller == null || _download != null || controller.State.DownloadingId != null) return;
        MonitorAuthorityLease action;
        try { action = controller.CaptureAction(); }
        catch (OperationCanceledException) { _feedback.Show(S("admin.native.subtitles.retired"), null); return; }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(action.Token);
        _download = cancellation;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(AdminSubtitleIds.DownloadFileName(row)) };
            picker.FileTypeChoices.Add(S("admin.native.subtitles.fileType"), ["." + AdminSubtitleIds.DownloadExtension(row.Format)]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var file = await picker.PickSaveFileAsync();
            if (file == null || !ReferenceEquals(controller, _controller) || !controller.IsActionCurrent(action)) return;
            var outcome = await controller.DownloadAsync(action, row, file.Path, cancellation.Token);
            if (!IsLoaded || !ReferenceEquals(controller, _controller) || !controller.IsActionCurrent(action)) return;
            if (outcome.Success) _feedback.Show(null, S("web.admin.common.subtitles.table.downloaded"));
            else if (outcome.Error is { } failure) _feedback.Show(failure, null);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (ReferenceEquals(controller, _controller) && controller.IsActionCurrent(action)) _feedback.Show(S("web.admin.common.subtitles.table.downloadFailed"), null);
        }
        finally
        {
            if (ReferenceEquals(_download, cancellation)) _download = null;
            cancellation.Dispose();
            action.Dispose();
        }
    }

    // ── Leave guard ──────────────────────────────────────────────────────

    private async Task<bool?> ConfirmDiscardAsync(MonitorAuthorityLease owner)
    {
        if (_discardDialog != null || _deleteDialog != null || XamlRoot == null) return null;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = S("web.shell.unsavedChanges.title"), Content = S("web.shell.unsavedChanges.description"),
            PrimaryButtonText = S("web.shell.unsavedChanges.discard"), CloseButtonText = S("common.cancel"), DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
        };
        _discardDialog = dialog;
        using var hide = owner.Token.Register(() => EnqueueUi(() => dialog.Hide()));
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        catch (Exception) { return null; }
        finally { if (ReferenceEquals(_discardDialog, dialog)) _discardDialog = null; }
    }
}
