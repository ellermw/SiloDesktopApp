using CommunityToolkit.Mvvm.ComponentModel;

namespace SiloPlayer.ViewModels;

public partial class CollectionEditorViewModel
{
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private string? _previewError;
    private CancellationTokenSource? _previewCancellation;
    private int _previewGeneration;

    public void CancelPreview()
    {
        ++_previewGeneration;
        _previewCancellation?.Cancel(); _previewCancellation?.Dispose(); _previewCancellation = null;
        IsPreviewing = false;
    }

    public async Task RefreshPreviewAsync()
    {
        CancelPreview();
        var generation = _previewGeneration;
        var editorGeneration = _editorLoadGeneration;
        var context = _catalogApi.CaptureContext();
        var cancellation = _previewCancellation = new();
        bool Current() => generation == _previewGeneration && editorGeneration == _editorLoadGeneration && !cancellation.IsCancellationRequested && _catalogApi.IsCurrentContext(context);
        IsPreviewing = true; PreviewError = null;
        try
        {
            var response = await _collectionsApi.PreviewCollectionAsync(new() { QueryDefinition = BuildQueryDefinition(), Limit = 24 }, cancellation.Token);
            if (!Current()) return;
            PreviewItems.Clear();
            foreach (var item in response.Items) PreviewItems.Add(item);
            PreviewTotal = response.Total; HasPreview = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (Current())
            {
                PreviewError = "The preview didn't load. You can still save.";
                SiloPlayer.Core.Services.LocalLog.AppendLine("collection_preview_error.txt", $"Smart preview failed; scope={RuleDefinition.MediaScope ?? "all"}; groups={RuleDefinition.Groups.Count}; status={(ex as SiloPlayer.Core.Api.ApiException)?.StatusCode}; location={(ex as SiloPlayer.Core.Api.ApiException)?.ErrorLocation}; {ex}");
            }
        }
        finally
        {
            if (generation == _previewGeneration) IsPreviewing = false;
            if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
            cancellation.Dispose();
        }
    }
}
