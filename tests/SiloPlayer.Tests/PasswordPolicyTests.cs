using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class PasswordPolicyTests
{
    private static readonly PasswordPolicy Strict = new()
    {
        MinimumLength = 12, RequireUppercase = true, RequireSpecial = true
    };

    [Theory]
    [InlineData("plainpassword", false, null)]
    [InlineData("Short-pass1", true, "Use at least 12 characters.")]
    [InlineData("correct-horse1", true, "Include at least one capital letter.")]
    [InlineData("Correct horse1", true, "Include at least one special character, such as ! or #.")]
    [InlineData("\u03a9correcthorse\u20ac", true, null)]
    [InlineData("\U00010400Correcthorse\U0001f680", true, null)]
    public void ChecksOfficialCases(string password, bool strict, string? message)
        => Assert.Equal(message, NewPassword.Problem(password, password, strict ? Strict : PasswordPolicy.Default));

    [Fact]
    public void CountsScalarsAndUtf8BytesAndChecksConfirmationFirst()
    {
        var supplementary = "\U0001f680";
        Assert.Equal("Passwords do not match.", NewPassword.Problem("a", "b", Strict));
        Assert.Equal("Use at least 8 characters.", NewPassword.Problem(string.Concat(Enumerable.Repeat(supplementary, 7)), PasswordPolicy.Default));
        Assert.Null(NewPassword.Problem(string.Concat(Enumerable.Repeat(supplementary, 18)), PasswordPolicy.Default));
        Assert.Equal("Use no more than 72 bytes.", NewPassword.Problem(string.Concat(Enumerable.Repeat(supplementary, 19)), PasswordPolicy.Default));
        Assert.Equal("At least 12 characters. Include at least one capital letter. Include at least one special character, such as ! or #.", NewPassword.Hint(Strict));
    }

    [Theory]
    [InlineData("password_too_short", "This password is too short.")]
    [InlineData("password_too_long", "This password is too long.")]
    [InlineData("password_needs_uppercase", "Include at least one capital letter.")]
    [InlineData("password_needs_special", "Include at least one special character, such as ! or #.")]
    public void SelectsNamedRuleByFieldAndCode(string code, string message)
    {
        var problem = new ApiException("validation_failed", "Invalid payload.", 422)
        {
            FieldErrors = [new("body.email", code, null), new("body.password", "invalid", null),
                new("body.password", code, null), new("body.new_password", code, null)]
        };
        Assert.Equal(message, NewPassword.FieldProblem(problem, "body.password"));
        Assert.Equal(message, NewPassword.FieldProblem(problem, "body.new_password"));
        Assert.Null(NewPassword.FieldProblem(problem, "body.current_password"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, 12, true)]
    [InlineData(HttpStatusCode.NotFound, 8, false)]
    [InlineData(HttpStatusCode.MethodNotAllowed, 8, false)]
    public async Task PublicReadUsesWireRulesOrOldServerDefault(HttpStatusCode status, int minimum, bool strict)
    {
        var client = new SiloApiClient(new HttpClient(new Handler(status)));
        client.SetBaseUrl("https://fixture.invalid/silo");
        client.SetProfile("synthetic-profile");
        var policy = await new AuthApi(client).GetPasswordPolicyAsync();
        Assert.Equal(minimum, policy.MinimumLength);
        Assert.Equal(72, policy.MaximumBytes);
        Assert.Equal(strict, policy.RequireUppercase);
        Assert.Equal(strict, policy.RequireSpecial);
    }

    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/silo/api/v2/auth/password-policy", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("X-Profile-Id"));
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK
                    ? """{"minimum_length":12,"maximum_bytes":72,"require_uppercase":true,"require_special":true}"""
                    : "{}")
            });
        }
    }
}
