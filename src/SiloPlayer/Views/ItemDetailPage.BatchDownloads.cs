using SiloPlayer.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Downloads;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage
{
    private CancellationTokenSource? _batchDownloadAttempt;

    private async Task ShowBatchDownloadDialogAsync(string seriesContentId, int? seasonNumber, string title)
    {
        // Acquire before discovery: More may be reopened while its HTTP read is held.
        if (_batchDownloadAttempt != null || !CanCurrentUserDownload() || ViewModel.Item is not { } item || XamlRoot == null) return;
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var authority = client.CaptureContext();
        var navigation = _navigationCts;
        if (navigation == null || !IsCurrentDetail(item.ContentId)) return;
        var library = ViewModel.LibraryId;
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(navigation.Token);
        _batchDownloadAttempt = owner;
        var ct = owner.Token;
        bool Current() => !ct.IsCancellationRequested && ReferenceEquals(_batchDownloadAttempt, owner)
            && ReferenceEquals(_navigationCts, navigation) && IsCurrentDetail(item.ContentId)
            && ReferenceEquals(ViewModel.Item, item) && ViewModel.LibraryId == library && client.IsCurrentContext(authority);
        var content = new StackPanel { MaxWidth = 420, Spacing = 12 };
        var status = new TextBlock { Text = "Preparing downloads…", TextWrapping = TextWrapping.Wrap };
        content.Children.Add(status);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = seasonNumber.HasValue ? $"Download: {title} — {(seasonNumber == 0 ? "Specials" : $"Season {seasonNumber}")}" : $"Download: {title} — All Seasons",
            CloseButtonText = "Cancel", Content = content,
        };
        void Retire() { if (!Current()) DispatcherQueue.TryEnqueue(() => { if (!owner.IsCancellationRequested) owner.Cancel(); dialog.Hide(); }); }
        var auth = App.Services.GetRequiredService<AuthService>();
        auth.UserChanged += Retire; auth.ProfileChanged += Retire; auth.LoggedOut += Retire;
        dialog.Closed += (_, _) => owner.Cancel();
        using var retirement = ct.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try
        {
            var shown = dialog.ShowAsync();
            // Discovery runs separately so closing during a noncooperative response still retires immediately.
            async Task Populate()
            {
                try
                {
                    var api = new DownloadsApi(client);
                    var capability = await api.GetCapabilityAsync(ct);
                    if (!Current()) return;
                    if (!capability.CanCreateBatch) { status.Text = "Season/series downloads are not available."; return; }
                    var versions = capability.BatchVersion
                        ? await api.GetBatchTemplateVersionsAsync(seriesContentId, seasonNumber, library, ct) : new List<FileVersion>();
                    if (!Current()) return;
                    status.Text = versions.Count > 0 ? "Choose an episode version as a template. Each episode uses its closest matching version." : "Each episode uses its best available version.";
                    ComboBox? versionPicker = null, qualityPicker = null;
                    if (versions.Count > 0)
                    {
                        versionPicker = new ComboBox { Header = "Version template", HorizontalAlignment = HorizontalAlignment.Stretch };
                        versionPicker.Items.Add("Best available for each episode");
                        foreach (var version in versions)
                            versionPicker.Items.Add(string.Join(" · ", new[] { version.EditionRaw, version.Resolution, version.CodecVideo, version.Hdr ? "HDR" : null }.Where(value => !string.IsNullOrWhiteSpace(value))));
                        versionPicker.SelectedIndex = 0;
                        content.Children.Add(versionPicker);
                    }
                    var qualities = capability.BulkQuality ? capability.QualityPresets.Distinct().ToList() : new List<string>();
                    if (qualities.Count > 0)
                    {
                        qualityPicker = new ComboBox { Header = "Quality", HorizontalAlignment = HorizontalAlignment.Stretch };
                        foreach (var quality in qualities) qualityPicker.Items.Add(quality == "original" ? "Original" : quality);
                        qualityPicker.SelectedIndex = Math.Max(0, qualities.IndexOf("original"));
                        content.Children.Add(qualityPicker);
                    }
                    var start = new Button { Content = "Start Download", HorizontalAlignment = HorizontalAlignment.Stretch };
                    var running = false;
                    start.Click += async (_, _) =>
                    {
                        if (running || !Current()) return;
                        running = true; start.IsEnabled = false;
                        if (versionPicker != null) versionPicker.IsEnabled = false;
                        if (qualityPicker != null) qualityPicker.IsEnabled = false;
                        var request = new DownloadRequest { ContentId = seriesContentId, Series = true, SeasonNumber = seasonNumber,
                            FileId = versionPicker is { SelectedIndex: > 0 } ? versions[versionPicker.SelectedIndex - 1].FileId : null,
                            Quality = qualityPicker is { SelectedIndex: >= 0 } ? qualities[qualityPicker.SelectedIndex] : "original" };
                        status.Text = "Registering downloads…";
                        try
                        {
                            var result = await api.CreateDownloadsAsync(request, ct);
                            if (!Current()) return;
                            var created = result.Downloads.Count; var skipped = result.Skipped.Count;
                            status.Text = $"Registered {created} download{(created == 1 ? "" : "s")}." + (skipped > 0 ? $" {skipped} skipped: {string.Join(", ", result.Skipped.Select(s => s.Reason).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())}" : "");
                            App.Services.GetRequiredService<ToastService>().Success(status.Text);
                            dialog.Hide();
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            if (!Current()) return;
                            // A previous page may be accepted. A new attempt requires an explicit reconciliation.
                            status.Text = ex.Message + " Some downloads may already be registered. Check Downloads before starting another batch.";
                            dialog.CloseButtonText = "Close";
                            var inspect = new Button { Content = "View Downloads" };
                            inspect.Click += (_, _) => { if (!Current()) return; dialog.Hide(); App.Services.GetRequiredService<NavigationService>().Navigate<DownloadsPage>(); };
                            content.Children.Add(inspect);
                        }
                    };
                    content.Children.Add(start);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (Current()) status.Text = ex.Message; }
                finally { if (!Current()) dialog.Hide(); }
            }
            var population = Populate();
            await shown;
            owner.Cancel();
            // Populate catches failures itself; it can finish silently after a noncooperative wire settles.
            _ = population;
        }
        finally
        {
            auth.UserChanged -= Retire; auth.ProfileChanged -= Retire; auth.LoggedOut -= Retire;
            owner.Cancel();
            if (ReferenceEquals(_batchDownloadAttempt, owner)) _batchDownloadAttempt = null;
        }
    }
}
