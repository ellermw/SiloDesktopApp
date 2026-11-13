using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.ViewModels;

public partial class SignupViewModel
{
    private readonly Dictionary<string, (Func<string> Render, string Last)> _shellText = [];

    private string ShellText(string property, Func<string> render)
    {
        var text = render();
        _shellText[property] = (render, text);
        return text;
    }

    private static string ShellProblem(Exception exception, string english)
        => exception is ApiException problem ? ApiProblemText.Display(problem, english) : english;

    public void RefreshLocalizedText()
    {
        foreach (var (property, text) in _shellText.ToArray())
        {
            switch (property)
            {
                case nameof(ErrorMessage) when ErrorMessage == text.Last: ErrorMessage = ShellText(property, text.Render); break;
                default: _shellText.Remove(property); break;
            }
        }
    }
}
