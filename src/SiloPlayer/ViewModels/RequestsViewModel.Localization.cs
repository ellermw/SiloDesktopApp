namespace SiloPlayer.ViewModels;

public partial class RequestsViewModel
{
    private readonly Dictionary<string, Action> _localizedMessages = new(StringComparer.Ordinal);

    private void SetLocalizedMessage(string property, Func<string?> text, Action<string?> assign)
    {
        void Refresh() => assign(text());
        _localizedMessages[property] = Refresh;
        Refresh();
    }

    private static string RequestProblem(Exception exception, string key, string fallback)
    {
        var message = SiloPlayer.Core.Localization.AppStrings.Get(key, fallback: fallback) + ": " + exception.Message;
        return exception is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem, message) : message;
    }

    internal void RefreshLocalization()
    {
        foreach (var refresh in _localizedMessages.Values.ToArray()) refresh();
        OnPropertyChanged(string.Empty);
    }
}
