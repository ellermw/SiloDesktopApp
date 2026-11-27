using System.Globalization;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

/// <summary>Password rules shared by every form that chooses a new password.</summary>
public static class NewPassword
{
    private const string Prefix = "web.auth.newPassword.";
    private const string NeedsUppercase = "Include at least one capital letter.";
    private const string NeedsSpecial = "Include at least one special character, such as ! or #.";

    public static string? Problem(string password, string confirmation, PasswordPolicy policy)
        => password != confirmation ? AppStrings.Get(Prefix + "mismatch") : Problem(password, policy);

    public static string? Problem(string password, PasswordPolicy policy)
    {
        if (password.EnumerateRunes().Count() < policy.MinimumLength)
            return AppStrings.Get(Prefix + "tooShortMinimum", MinimumArguments(policy), $"Use at least {MinimumText(policy)}.");
        if (Encoding.UTF8.GetByteCount(password) > policy.MaximumBytes)
            return AppStrings.Get(Prefix + "tooLong");
        if (policy.RequireUppercase && !password.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) == UnicodeCategory.UppercaseLetter))
            return AppStrings.Get(Prefix + "needsUppercase", fallback: NeedsUppercase);
        if (policy.RequireSpecial && !password.EnumerateRunes().Any(IsSpecial))
            return AppStrings.Get(Prefix + "needsSpecial", fallback: NeedsSpecial);
        return null;
    }

    private static bool IsSpecial(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or
        UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or
        UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or
        UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;

    public static string Hint(PasswordPolicy policy, bool includeByteLimit = false)
    {
        var length = AppStrings.Get(includeByteLimit ? "web.settings.account.password.requirements" : Prefix + "minLengthHint",
            MinimumArguments(policy), includeByteLimit
                ? $"At least {MinimumText(policy)} and no more than {policy.MaximumBytes.ToString("N0", AppStrings.FormatCulture)} bytes."
                : $"At least {MinimumText(policy)}.");
        var rules = RuleHint(policy);
        return rules.Length == 0 ? length : length + " " + rules;
    }

    private static string RuleHint(PasswordPolicy policy) => string.Join(" ", new[]
    {
        policy.RequireUppercase ? AppStrings.Get(Prefix + "needsUppercase", fallback: NeedsUppercase) : null,
        policy.RequireSpecial ? AppStrings.Get(Prefix + "needsSpecial", fallback: NeedsSpecial) : null,
    }.OfType<string>());

    private static string MinimumText(PasswordPolicy policy) =>
        $"{policy.MinimumLength.ToString("N0", AppStrings.FormatCulture)} {(policy.MinimumLength == 1 ? "character" : "characters")}";

    private static Dictionary<string, object?> MinimumArguments(PasswordPolicy policy) => new()
    {
        ["minimum"] = policy.MinimumLength,
        ["minimum_text"] = policy.MinimumLength.ToString("N0", AppStrings.FormatCulture),
        ["maximum"] = policy.MaximumBytes.ToString("N0", AppStrings.FormatCulture)
    };

    public static string? FieldProblem(ApiException problem, string location)
    {
        foreach (var field in problem.FieldErrors)
        {
            if (field.Location != location) continue;
            var fallback = field.Code switch
            {
                "password_too_short" => "This password is too short.",
                "password_too_long" => "This password is too long.",
                "password_needs_uppercase" => NeedsUppercase,
                "password_needs_special" => NeedsSpecial,
                _ => null
            };
            if (fallback is not null) return AppStrings.Get("error.field." + field.Code, fallback: fallback);
        }
        return null;
    }
}
