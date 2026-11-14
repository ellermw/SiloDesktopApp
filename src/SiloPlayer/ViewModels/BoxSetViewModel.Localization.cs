namespace SiloPlayer.ViewModels;

public partial class BoxSetViewModel
{
    private readonly Dictionary<string, Action> _localizedMessages = new(StringComparer.Ordinal);

    private void SetLocalizedMessage(string property, Func<string?> text, Action<string?> assign)
    {
        void Refresh() => assign(text());
        _localizedMessages[property] = Refresh;
        Refresh();
    }

    internal void RefreshLocalization()
    {
        foreach (var refresh in _localizedMessages.Values.ToArray()) refresh();
        OnPropertyChanged(string.Empty);
    }
}
