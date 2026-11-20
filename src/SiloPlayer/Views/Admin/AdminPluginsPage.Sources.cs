using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.Storage;
using U = SiloPlayer.Controls.AdminSettingsUi;
using P = SiloPlayer.Core.Services.Admin.AdminPluginPresentation;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPluginsPage
{
    private StorageFile? _uploadFile;
    private bool _picking;
    private void BuildUpload()
    {
        var section = Section(_content, "Install from a file", "Upload a plugin file for a plugin that isn't in a repository.");
        var row = new LogsWrapPanel { Spacing = 10 }; var label = Text("Choose plugin file...", 13, true);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(progress, "Plugin upload progress");
        var choose = U.Action("Choose plugin file...", "PluginsChooseFile"); var upload = U.Action("Upload", "PluginsUpload"); upload.IsEnabled = false;
        choose.Click += async (_, _) =>
        {
            var controller = _controller; var owner = controller?.Owner;
            if (_picking || owner is not { IsCurrent: true }) return;
            _picking = true;
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
                var file = await picker.PickSingleFileAsync();
                if (_closed || !ReferenceEquals(controller, _controller) || !ReferenceEquals(owner, controller?.Owner) || !owner.IsCurrent) return;
                _uploadFile = file; label.Text = file?.Name ?? "Choose plugin file..."; upload.IsEnabled = file != null;
            }
            catch { if (owner.IsCurrent) _notice.Text = "The file picker could not be opened."; }
            finally { _picking = false; }
        };
        upload.Click += async (_, _) =>
        {
            var controller = _controller; var owner = controller?.Owner; var file = _uploadFile;
            if (controller == null || owner is not { IsCurrent: true } || file == null) return;
            if (!await ConfirmAsync("Install from a file", "Silo reads the archive manifest, or runs a plugin binary to read its manifest. An existing plugin with the same ID is replaced. Only upload a file if you trust its source.", "Upload", () => ReferenceEquals(file, _uploadFile))) return;
            try
            {
                var properties = await file.GetBasicPropertiesAsync();
                if (_closed || !owner.IsCurrent || !ReferenceEquals(controller, _controller) || !ReferenceEquals(owner, controller.Owner)) return;
                if (properties.Size is 0 or > (ulong)AdminPluginsApi.MaxUploadBytes) { _notice.Text = "Choose a plugin archive of at most 256 MiB."; return; }
                await using var stream = await file.OpenStreamForReadAsync();
                if (!owner.IsCurrent || _closed || !ReferenceEquals(owner, controller.Owner)) return;
                progress.Visibility = Visibility.Visible;
                var reporter = new Progress<int>(percent =>
                {
                    if (!owner.IsCurrent || _closed || !ReferenceEquals(owner, controller.Owner)) return;
                    progress.Value = percent;
                });
                await controller.UploadAsync(stream, file.Name, (long)properties.Size, reporter);
            }
            catch { if (owner.IsCurrent && !_closed) _notice.Text = "The plugin file could not be read or uploaded. Refresh installations before uploading again."; }
            finally { if (ReferenceEquals(controller, _controller)) _uploadFile = null; label.Text = "Choose plugin file..."; upload.IsEnabled = false; progress.Visibility = Visibility.Collapsed; }
        };
        row.Children.Add(choose); row.Children.Add(label); row.Children.Add(upload); section.Children.Add(row); section.Children.Add(progress);
    }
    private void BuildRepositories(AdminPluginsState state)
    {
        var section = Section(_content, "Repositories", "Where Silo finds plugins.");
        section.Children.Add(Command("Add repository", "PluginsRepositoryAdd", () => _ = EditRepositoryAsync(null)));
        if (state.Errors.ContainsKey("Repositories")) { section.Children.Add(Text("Failed to load plugin repositories.")); return; }
        if (state.Repositories.Count == 0) section.Children.Add(Text("No repositories configured. Add one to browse available plugins.", 13, true));
        // Bounded local paging: never create 1,000 native repository rows at once.
        var host = Stack(10); section.Children.Add(host); var page = 0;
        void RenderRepositories()
        {
            host.Children.Clear();
            foreach (var repository in state.Repositories.Skip(page * 25).Take(25))
            {
                var row = Stack(8);
                row.Children.Add(Text((repository.Enabled ? "● " : "○ ") + (repository.Managed ? P.Source(repository.SourceKind) : P.SafeText(repository.DisplayName))));
                row.Children.Add(Text(repository.Managed ? "Managed by Silo" : P.SafeLink(repository.Url)?.ToString() ?? "Repository URL hidden", 12, true));
                if (repository.LastFetchedAt is { } fetched) row.Children.Add(Text("Last fetched: " + fetched.ToLocalTime().ToString("g"), 12, true));
                if (!repository.Managed)
                {
                    var actions = new LogsWrapPanel { Spacing = 8 };
                    actions.Children.Add(Command("Edit", "PluginRepositoryEdit_" + repository.Id, () => _ = EditRepositoryAsync(repository)));
                    actions.Children.Add(Command(repository.Enabled ? "Disable" : "Enable", "PluginRepositoryToggle_" + repository.Id,
                        () => { if (_controller is { } c) _ = c.RepositoryAsync(repository, "", "", !repository.Enabled); }));
                    actions.Children.Add(Command("Remove " + P.SafeText(repository.DisplayName), "PluginRepositoryDelete_" + repository.Id, () => _ = DeleteRepositoryAsync(repository)));
                    row.Children.Add(actions);
                }
                host.Children.Add(Panel(row, 10, 12));
            }
            if (state.Repositories.Count <= 25) return;
            var paging = new LogsWrapPanel { Spacing = 8 };
            var previous = Command("Previous repositories", "PluginRepositoryPrevious", () => { page--; RenderRepositories(); }); previous.IsEnabled = page > 0;
            var next = Command("Next repositories", "PluginRepositoryNext", () => { page++; RenderRepositories(); }); next.IsEnabled = (page + 1) * 25 < state.Repositories.Count;
            paging.Children.Add(previous); paging.Children.Add(next); host.Children.Add(paging);
        }
        RenderRepositories();
    }
    private async Task DeleteRepositoryAsync(AdminPluginRepository repository)
    {
        var controller = _controller;
        if (controller != null && await ConfirmAsync("Remove " + P.SafeText(repository.DisplayName) + "?", "Installed plugins keep working. Without this repository they can no longer receive catalog updates.", "Remove repository", () => controller.IsCurrent(repository) && !repository.Managed))
            await controller.RepositoryAsync(repository, "", "", repository.Enabled, delete: true);
    }
    private async Task EditRepositoryAsync(AdminPluginRepository? repository)
    {
        var controller = _controller; var owner = controller?.Owner;
        if (_dialog != null || owner is not { IsCurrent: true } || repository?.Managed == true || repository != null && controller?.IsCurrent(repository) != true) return;
        var name = Input("Repository name", "PluginsRepositoryName", double.NaN); name.MaxLength = 1024; name.Text = repository == null ? "" : P.SafeText(repository.DisplayName);
        var url = Input("https://plugins.example.test/index.json", "PluginsRepositoryUrl", double.NaN); url.MaxLength = 8192;
        url.Text = repository == null ? "" : P.SafeLink(repository.Url)?.ToString() ?? ""; AutomationProperties.SetName(url, "Repository URL");
        var enabled = new ToggleSwitch { Header = "Enabled", IsOn = repository?.Enabled ?? true };
        var body = Stack(12); body.Children.Add(Text("Repository name")); body.Children.Add(name); body.Children.Add(Text("Repository URL")); body.Children.Add(url); body.Children.Add(enabled);
        if (repository != null) body.Children.Add(Text("Leave the URL blank to keep its stored value.", 12, true));
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = repository == null ? "Add repository" : "Edit repository", Content = body, PrimaryButtonText = repository == null ? "Add" : "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        AutomationProperties.SetAutomationId(dialog, "PluginsRepositoryDialog");
        void Validate() => dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0 && (repository != null || url.Text.Trim().Length > 0);
        name.TextChanged += (_, _) => Validate(); url.TextChanged += (_, _) => Validate(); Validate(); _dialog = dialog;
        using var retire = owner.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed || !owner.IsCurrent || !ReferenceEquals(controller, _controller) || !ReferenceEquals(owner, controller?.Owner)) return;
            await controller!.RepositoryAsync(repository, name.Text.Trim(), url.Text.Trim(), enabled.IsOn);
        }
        catch (System.Runtime.InteropServices.COMException) { }
        finally { name.Text = ""; url.Text = ""; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
}
