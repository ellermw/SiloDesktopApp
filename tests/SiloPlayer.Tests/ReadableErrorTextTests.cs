using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

/// <summary>
/// Official #4537 (web/src/hooks/queries/transportFailureToasts.test.tsx): a save or transfer that cannot reach the server shows the
/// action's own message, not developer English; a server problem detail and an error the action threw on purpose keep their text.
/// </summary>
public sealed class ReadableErrorTextTests
{
    private const string Fallback = "Failed to save audio preference";

    public static TheoryData<Exception> DeveloperEnglish => new()
    {
        // The official test's four: a failed fetch, a timeout, a non-JSON body (a proxy page) and a superseded request.
        new HttpRequestException("No connection could be made because the target machine actively refused it."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException()),
        new ApiException("unknown", "HTTP 502", 502),
        new OperationCanceledException("Audio preference owner changed."),
        new SocketException(10061),
        new IOException("Unable to read data from the transport connection."),
        new JsonException("The JSON value could not be converted."),
        new TimeoutException("The operation has timed out."),
        new AggregateException(new HttpRequestException("Name or service not known")),
    };

    [Theory]
    [MemberData(nameof(DeveloperEnglish))]
    public void DeveloperEnglishGetsTheActionsOwnMessage(Exception error) => Assert.Equal(Fallback, ReadableErrorText.Get(error, Fallback));

    [Fact]
    public void AServerProblemDetailKeepsItsText()
    {
        var problem = new ApiException("validation_failed", "Value is out of range.", 422) { ProblemType = "validation_failed", ProblemDetail = "Value is out of range." };
        Assert.Equal("Value is out of range.", ReadableErrorText.Get(problem, Fallback));
        // A refusal whose JSON body only has a message is the server's own words too.
        Assert.Equal("Synthetic write error", ReadableErrorText.Get(new ApiException("synthetic_error", "Synthetic write error", 400), Fallback));
    }

    [Fact]
    public void AnErrorTheActionThrewOnPurposeKeepsItsText()
    {
        Assert.Equal("nav_shortcuts: the value must be valid JSON", ReadableErrorText.Get(new FormatException("nav_shortcuts: the value must be valid JSON"), Fallback));
        Assert.Equal("The selected audio track is unavailable.", ReadableErrorText.Get(new ArgumentException("The selected audio track is unavailable."), Fallback));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnErrorWithoutTextGetsTheActionsOwnMessage(string? message) =>
        Assert.Equal(Fallback, ReadableErrorText.Get(new TextlessError(message), Fallback));

    private sealed class TextlessError(string? text) : Exception { public override string Message => text ?? ""; }

    [Fact]
    public void ANullErrorGetsTheActionsOwnMessage() => Assert.Equal(Fallback, ReadableErrorText.Get(null, Fallback));

    [Fact]
    public void AStatusOnlyResponseIsDeveloperTextOnlyWhenItHasNoProblemBody()
    {
        Assert.True(ReadableErrorText.IsDeveloperText(new ApiException("unknown", "HTTP 503", 503)));
        Assert.False(ReadableErrorText.IsDeveloperText(new ApiException("unknown", "HTTP 503", 503) { ProblemType = "service_unavailable" }));
        Assert.False(ReadableErrorText.IsDeveloperText(new ApiException("unknown", "HTTP 503", 503) { ProblemDetail = "Try again later." }));
        Assert.False(ReadableErrorText.IsDeveloperText(new ApiException("unknown", "HTTP 404", 503)));
    }

    // The native call sites of #4537 pass the catalog's own fallback; guard that none of them shows a raw exception message again.
    [Theory]
    [InlineData("src/SiloPlayer/Controls/SubtitleSearchDialog.xaml.cs")]
    [InlineData("src/SiloPlayer/Views/ItemDetailPage.xaml.cs")]
    public void TheCallSitesDoNotShowARawExceptionMessageForTheseActions(string relative)
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, relative));
        Assert.DoesNotContain("ShowError(ex.Message)", source);
        Assert.DoesNotContain("Language detection failed: {ex.Message}", source);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(ex.Message) ? \"Failed to save audio preference\" : ex.Message", source);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(ex.Message) ? \"Failed to save subtitle preference\" : ex.Message", source);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(ex.Message) ? \"Failed to reset subtitle preference\" : ex.Message", source);
    }
}
