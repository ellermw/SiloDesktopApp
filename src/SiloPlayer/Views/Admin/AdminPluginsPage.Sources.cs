using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using P = SiloPlayer.Core.Services.Admin.AdminPluginPresentation;
using T = SiloPlayer.Core.Services.Admin.AdminPluginsText;
using Ui = SiloPlayer.Views.Admin.AdminPluginsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>A file the admin chose to install. Only its name and size are known until the upload opens it.</summary>
public sealed record AdminPluginUploadChoice(string Name, long Size, Func<Task<Stream>> Open);

public sealed partial class AdminPluginsPage
{
    private AdminPluginUploadChoice? _upload;
    private bool _picking;
    private bool _uploading;
    private int _uploadPercent;
    private string _uploadNote = "";
    private ProgressBar? _progressBar;
    private bool _repoFormOpen;
    private string _repoName = "";
    private string _repoUrl = "";
    private int _repoShown = 50;

    /// <summary>The OS file picker. Everything behind it runs through <see cref="UploadChoice"/>, so a fixture can supply a file without a dialog.</summary>
    public Func<Task<AdminPluginUploadChoice?>> FilePicker { get; set; } = PickFileAsync;

    private static async Task<AdminPluginUploadChoice?> PickFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
        var file = await picker.PickSingleFileAsync();
        if (file == null) return null;
        var properties = await file.GetBasicPropertiesAsync();
        return new(file.Name, properties.Size > long.MaxValue ? long.MaxValue : (long)properties.Size, async () => await file.OpenStreamForReadAsync());
    }

    private void ClearUpload()
    {
        _upload = null; _uploading = false; _uploadPercent = 0; _uploadNote = ""; _progressBar = null; _picking = false;
        _repoFormOpen = false; _repoName = ""; _repoUrl = ""; _repoShown = 50;
    }

    // ── install from a file ────────────────────────────────────────────────

    private FrameworkElement BuildUploadPanel()
    {
        var state = _controller!.State;
        var body = Stack(10);
        body.Children.Add(Ui.Heading(T.T("web.admin.plugins.upload.heading"), 2, 15)); body.Children.Add(Text(T.T("web.admin.plugins.upload.description"), 13, true));
        var row = new LogsWrapPanel { Spacing = 10, RowSpacing = 8 };
        var busy = state.Busy || state.Loading || _uploading;
        var choose = AdminSettingsUi.Action(T.T("web.admin.plugins.upload.choose"), "PluginsChooseFile"); choose.IsEnabled = !busy && !_picking;
        AutomationProperties.SetName(choose, T.T("web.admin.plugins.upload.fileLabel"));
        var chosen = Text(_upload?.Name ?? "", 13, true); chosen.VerticalAlignment = VerticalAlignment.Center; AutomationProperties.SetAutomationId(chosen, "PluginsChosenFile");
        var upload = AdminSettingsUi.Action(_uploading ? T.T("web.admin.plugins.upload.uploading") : T.T("web.admin.plugins.upload.submit"), "PluginsUpload", primary: true);
        upload.IsEnabled = _upload != null && !busy;
        choose.Click += (_, _) => _ = RunAsync(() => ChooseAsync(choose, chosen, upload));
        upload.Click += (_, _) => _ = RunAsync(UploadChosenAsync);
        row.Children.Add(choose); row.Children.Add(chosen); row.Children.Add(upload); body.Children.Add(row);
        if (_uploadNote.Length > 0) { var note = Text(_uploadNote, 13); note.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Warning); AutomationProperties.SetLiveSetting(note, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); body.Children.Add(note); }
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = _uploadPercent, Height = 4, Visibility = _uploading ? Visibility.Visible : Visibility.Collapsed };
        AutomationProperties.SetName(bar, T.T("web.admin.plugins.upload.progress")); AutomationProperties.SetAutomationId(bar, "PluginsUploadProgress");
        _progressBar = bar; body.Children.Add(bar);
        var panel = Panel(body, 12, 16, border: true); AutomationProperties.SetAutomationId(panel, "PluginsUploadPanel"); return panel;
    }

    private async Task ChooseAsync(Button choose, TextBlock chosen, Button upload)
    {
        var controller = _controller; var owner = controller?.Owner;
        if (_picking || _uploading || owner is not { IsCurrent: true }) return;
        _picking = true; choose.IsEnabled = false; var rebuild = false;
        try
        {
            var choice = await FilePicker();
            if (_closed || !ReferenceEquals(controller, _controller) || !ReferenceEquals(owner, controller?.Owner) || !owner.IsCurrent) return;
            _upload = null; _uploadNote = "";
            if (choice != null)
            {
                // Type and size are judged from the picker's own answer; the file itself is not opened until Upload.
                var check = AdminPluginsApi.CheckUpload(choice.Name, choice.Size);
                if (check == AdminPluginUploadCheck.Accepted) _upload = choice;
                else _uploadNote = check switch
                {
                    AdminPluginUploadCheck.Empty => "The chosen file is empty.",
                    AdminPluginUploadCheck.TooLarge => "The chosen file is larger than 256 MiB, the most the server accepts.",
                    _ => "The chosen file's name cannot be used.",
                };
            }
            chosen.Text = _upload?.Name ?? ""; upload.IsEnabled = _upload != null && controller?.State is { Busy: false, Loading: false }; rebuild = true;
        }
        catch (Exception) { if (!_closed) { _uploadNote = "The file picker could not be opened."; rebuild = true; } }
        finally { _picking = false; if (!_closed) choose.IsEnabled = _controller?.State is { Busy: false, Loading: false }; }
        if (rebuild && !_closed) RebuildNow();
    }

    /// <summary>Uploads the chosen file once. The admin confirms first, because the server will run or unpack what it is sent.</summary>
    private async Task UploadChosenAsync()
    {
        var controller = _controller; var owner = controller?.Owner; var choice = _upload;
        if (controller == null || owner is not { IsCurrent: true } || choice == null || _uploading) return;
        var tier = "unverified"; var content = Stack(10);
        content.Children.Add(Text(T.T("web.admin.plugins.tile.confirmDescription", new { source = P.SourceLabel(tier), notice = P.TierNotice(tier) ?? "" }).Trim(), 14));
        content.Children.Add(Text(choice.Name, 13, true));
        if (!await ConfirmAsync(T.T("web.admin.plugins.upload.heading"), content, T.T("web.admin.plugins.upload.submit"), "PluginsUploadConfirmation")) return;
        if (_closed || !ReferenceEquals(controller, _controller) || !ReferenceEquals(owner, controller.Owner) || !owner.IsCurrent || !ReferenceEquals(choice, _upload)) return;
        // Type and size are checked again immediately before anything is read.
        if (AdminPluginsApi.CheckUpload(choice.Name, choice.Size) != AdminPluginUploadCheck.Accepted) return;
        _uploading = true; _uploadPercent = 0; _uploadNote = ""; RebuildNow();
        Stream? stream = null;
        try
        {
            stream = await choice.Open();
            if (_closed || !ReferenceEquals(controller, _controller) || !owner.IsCurrent) return;
            if (stream.CanSeek && stream.Length != choice.Size) { _uploadNote = "The chosen file changed. Choose it again."; return; }
            var progress = new Progress<int>(percent =>
            {
                if (_closed || !owner.IsCurrent || !ReferenceEquals(owner, controller.Owner)) return;
                _uploadPercent = Math.Clamp(percent, 0, 100); if (_progressBar != null) _progressBar.Value = _uploadPercent;
            });
            await controller.UploadAsync(stream, choice.Name, choice.Size, progress);
        }
        catch (Exception) { if (!_closed && owner.IsCurrent) _uploadNote = "The plugin file could not be read."; }
        finally
        {
            // Nothing about the file outlives the upload: the stream is closed and the choice forgotten whatever the outcome.
            if (stream != null) await stream.DisposeAsync();
            _upload = null; _uploading = false; _uploadPercent = 0;
            if (!_closed) RebuildNow();
        }
    }

    // ── repositories ───────────────────────────────────────────────────────

    private FrameworkElement BuildRepositoriesPanel(AdminPluginsState state)
    {
        var body = Stack(10);
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titles = Stack(2); titles.Children.Add(Ui.Heading(T.T("web.admin.plugins.repositories.heading"), 2, 15)); titles.Children.Add(Text(T.T("web.admin.plugins.repositories.description"), 13, true));
        head.Children.Add(titles);
        var add = Ui.Command(_repoFormOpen ? T.T("common.cancel") : T.T("web.admin.plugins.repositories.add"), "PluginsRepositoryAdd", () => { _repoFormOpen = !_repoFormOpen; RebuildNow(); });
        Grid.SetColumn(add, 1); head.Children.Add(add); body.Children.Add(head);
        if (state.Failures.TryGetValue(AdminPluginsSection.Repositories, out var failure))
        {
            var error = Text(failure, 13); error.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
            body.Children.Add(error);
            var retry = Ui.Command(T.T("common.retry"), "PluginsRepositoriesReload", () => ReloadSection(AdminPluginsSection.Repositories)); retry.HorizontalAlignment = HorizontalAlignment.Left; retry.IsEnabled = !state.Loading; body.Children.Add(retry);
        }
        if (_repoFormOpen) body.Children.Add(RepositoryForm(state));
        var idle = !state.Busy && !state.Loading;
        foreach (var repository in state.Repositories.Take(_repoShown))
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var texts = Stack(0);
            // The two managed repositories are a fixed set: show their label rather than whatever display name they carry.
            var name = repository.Managed && repository.SourceKind != "external" ? P.SourceLabel(repository.SourceKind) : repository.DisplayName;
            var title = new LogsWrapPanel { Spacing = 8 };
            title.Children.Add(Ui.StatusLabel(name, repository.Enabled ? AdminPluginTone.Success : AdminPluginTone.Muted));
            texts.Children.Add(title);
            var url = Text(repository.Managed ? T.T("web.admin.plugins.repositories.managed") : repository.Url, 12, true); url.TextTrimming = TextTrimming.CharacterEllipsis; url.TextWrapping = TextWrapping.NoWrap;
            ToolTipService.SetToolTip(url, repository.Managed ? T.T("web.admin.plugins.repositories.managed") : repository.Url); texts.Children.Add(url);
            row.Children.Add(texts);
            if (!repository.Managed)
            {
                var actions = new LogsWrapPanel { Spacing = 6 };
                var toggle = Ui.Command(repository.Enabled ? T.T("web.admin.plugins.repositories.disable") : T.T("web.admin.plugins.repositories.enable"), "PluginRepositoryToggle_" + repository.Id,
                    () => _ = RunAsync(() => ChangeRepositoryAsync(repository, false)));
                AutomationProperties.SetName(toggle, (repository.Enabled ? T.T("web.admin.plugins.repositories.disable") : T.T("web.admin.plugins.repositories.enable")) + ": " + repository.DisplayName); toggle.IsEnabled = idle;
                var remove = Ui.Command("✕", "PluginRepositoryRemove_" + repository.Id, () => _ = RunAsync(() => ChangeRepositoryAsync(repository, true)));
                AutomationProperties.SetName(remove, T.T("web.admin.plugins.repositories.remove", new { name = repository.DisplayName })); remove.IsEnabled = idle; remove.MinWidth = 32; remove.Padding = new(0);
                actions.Children.Add(toggle); actions.Children.Add(remove); Grid.SetColumn(actions, 1); row.Children.Add(actions);
            }
            body.Children.Add(row);
        }
        if (state.Repositories.Count > _repoShown)
            body.Children.Add(Ui.Command("+" + Math.Min(50, state.Repositories.Count - _repoShown), "PluginRepositoriesMore", () => { _repoShown += 50; RebuildNow(); }));
        if (state.Repositories.Count == 0 && !_repoFormOpen && !state.Failures.ContainsKey(AdminPluginsSection.Repositories))
            body.Children.Add(Text(T.T("web.admin.plugins.repositories.empty"), 13, true));
        var panel = Panel(body, 12, 16, border: true); AutomationProperties.SetAutomationId(panel, "PluginsRepositoriesPanel"); return panel;
    }

    private FrameworkElement RepositoryForm(AdminPluginsState state)
    {
        var form = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        var name = Input(T.T("web.admin.plugins.repositories.name"), "PluginsRepositoryName", 200); name.Text = _repoName;
        var url = Input("https://plugins.example.test/index.json", "PluginsRepositoryUrl", 320); url.Text = _repoUrl; AutomationProperties.SetName(url, T.T("web.admin.plugins.repositories.url"));
        var submit = AdminSettingsUi.Action(T.T("web.admin.plugins.repositories.submit"), "PluginsRepositorySubmit", primary: true);
        void Validate() { submit.IsEnabled = !state.Busy && !state.Loading && name.Text.Trim().Length > 0 && url.Text.Trim().Length > 0; }
        // Starting text is reported as a change; this only ever updates the remembered draft.
        name.TextChanged += (_, _) => { _repoName = name.Text; Validate(); }; url.TextChanged += (_, _) => { _repoUrl = url.Text; Validate(); }; Validate();
        submit.Click += (_, _) => _ = RunAsync(async () =>
        {
            var controller = _controller; if (controller == null || name.Text.Trim().Length == 0 || url.Text.Trim().Length == 0) return;
            // What was typed stays until the server accepts it, so a refused URL can be fixed.
            var outcome = await controller.AddRepositoryAsync(name.Text, url.Text);
            if (outcome == AdminPluginWriteOutcome.Done) { _repoName = ""; _repoUrl = ""; _repoFormOpen = false; }
            RebuildNow();
        });
        form.Children.Add(name); form.Children.Add(url); form.Children.Add(submit);
        return form;
    }

    private async Task ChangeRepositoryAsync(AdminPluginRepository repository, bool remove)
    {
        var controller = _controller; if (controller == null) return;
        var outcome = remove ? await controller.RemoveRepositoryAsync(repository) : await controller.SetRepositoryEnabledAsync(repository, !repository.Enabled);
        if (outcome != AdminPluginWriteOutcome.Done) RebuildNow();
    }
}
