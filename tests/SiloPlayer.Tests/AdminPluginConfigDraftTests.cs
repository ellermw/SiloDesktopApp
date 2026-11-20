using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminPluginConfigDraftTests
{
    [Fact]
    public void DeclaredStringStaysStringAndNumericClearIsExplicit()
    {
        using var draft = new AdminPluginConfigDraft(new() { Key = "connection", JsonSchema = """{"type":"object","properties":{"postal_code":{"type":"string"},"port":{"type":"integer"},"enabled":{"type":"boolean"}}}""" }, null);
        var fields = draft.Form!.Fields;
        draft.Set(fields.Single(f => f.Key == "postal_code"), "00123"); draft.Set(fields.Single(f => f.Key == "port"), "");
        var body = draft.Build();
        Assert.Equal("00123", body.Value["postal_code"].GetString()); Assert.Equal(JsonValueKind.Null, body.Value["port"].ValueKind);
        Assert.False(body.Value["enabled"].GetBoolean());
    }
    [Fact]
    public void RedactedSecretNeverPopulatesAndClearRequiresExplicitChoice()
    {
        using var draft = new AdminPluginConfigDraft(new() { Key = "secret", JsonSchema = """{"type":"object","properties":{"password":{"type":"string","writeOnly":true}}}""" },
            new() { ConfiguredSecrets = ["password"], Value = new() { ["password"] = JsonSerializer.SerializeToElement("must-not-be-visible") } });
        var field = Assert.Single(draft.Form!.Fields);
        Assert.Equal(JsonValueKind.Undefined, draft.Value(field).ValueKind); Assert.Empty(draft.Build().Value);
        draft.ToggleClear(field); Assert.Equal("password", Assert.Single(draft.Build().ClearSecrets));
        draft.Set(field, "synthetic-replacement"); Assert.Empty(draft.Build().ClearSecrets);
        draft.Dispose(); Assert.False(draft.IsDirty); Assert.Empty(draft.Build().Value);
    }
    [Fact]
    public void RequiredSavedSecretCanBeKeptButCannotBeCleared()
    {
        using var draft = new AdminPluginConfigDraft(new() { Key = "secret", JsonSchema = """{"type":"object","required":["password"],"properties":{"password":{"type":"string","writeOnly":true}}}""" }, new() { ConfiguredSecrets = ["password"] });
        var field = Assert.Single(draft.Form!.Fields); draft.ToggleClear(field);
        Assert.Empty(draft.Build().ClearSecrets); Assert.Empty(draft.Build().Value);
    }
    [Fact]
    public void HiddenConditionalFieldsAreNotSentAndSectionsCanHideFields()
    {
        using var draft = new AdminPluginConfigDraft(new() { Key = "conditional", JsonSchema = "{}", AdminForm = new()
        {
            Fields = [new() { Key = "mode" }, new() { Key = "endpoint", ShowWhen = [new() { Field = "mode", Equals = ["remote"] }] }, new() { Key = "region" }],
            Sections = [new() { Key = "advanced", FieldKeys = ["region"], ShowWhen = [new() { Field = "mode", Equals = ["remote"] }] }],
        } }, new() { Value = new() { ["endpoint"] = JsonSerializer.SerializeToElement("old endpoint"), ["region"] = JsonSerializer.SerializeToElement("old region") } });
        draft.Set(draft.Form!.Fields[0], "local"); Assert.False(draft.Build().Value.ContainsKey("endpoint")); Assert.False(draft.Build().Value.ContainsKey("region"));
        draft.Set(draft.Form.Fields[0], "remote"); Assert.Equal("old endpoint", draft.Build().Value["endpoint"].GetString());
    }
    [Fact]
    public void ExplicitFormAddsMissingScalarFieldsAndUpgradesSecretMetadata()
    {
        using var draft = new AdminPluginConfigDraft(new() { Key = "entry", JsonSchema = """{"type":"object","properties":{"password":{"type":"string","format":"password"},"host":{"type":"string"}}}""",
            AdminForm = new() { Fields = [new() { Key = "password", Control = "TEXT" }] } }, null);
        Assert.Equal(2, draft.Form!.Fields.Count); Assert.True(draft.Form.Fields.Single(f => f.Key == "password").Secret);
    }
    [Fact]
    public void TypedMultiSelectUsesDeclaredItemsAndRetainsNumericLookingStrings()
    {
        var schema = new AdminPluginSchema { Key = "lists", JsonSchema = """{"type":"object","properties":{"numeric":{"type":"array","items":{"type":"integer"}},"strings":{"type":"array","items":{"type":"string"}}}}""",
            AdminForm = new() { Fields = [new() { Key = "numeric", Control = "MULTI_SELECT" }, new() { Key = "strings", Control = "MULTI_SELECT" }] } };
        using var draft = new AdminPluginConfigDraft(schema, null); draft.Set(draft.Form!.Fields[0], new[] { "12", "3" }); draft.Set(draft.Form.Fields[1], new[] { "001" });
        Assert.Equal(12, draft.Build().Value["numeric"][0].GetInt32()); Assert.Equal("001", draft.Build().Value["strings"][0].GetString());
    }
    [Fact]
    public void UnsupportedObjectWithoutDescriptorHasExplicitUnavailableForm()
    {
        using var draft = new AdminPluginConfigDraft(new() { Key = "complex", JsonSchema = """{"type":"object","properties":{"nested":{"type":"object"}}}""" }, null);
        Assert.Null(draft.Form); Assert.Throws<InvalidOperationException>(() => draft.Build());
    }
    [Theory]
    [InlineData("https://fixture.invalid/file?token=secret")]
    [InlineData("https://name:secret@fixture.invalid/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret")]
    public void ResourceLinksNeverOpenCredentialsSignedQueriesOrUnsafeSchemes(string value) => Assert.Null(AdminPluginPresentation.SafeLink(value));
    [Fact]
    public void DiagnosticsOmitSignedLinksAndCredentialAssignments()
    {
        var text = AdminPluginPresentation.SafeText("Failure at https://fixture.invalid/file?token=synthetic password=synthetic api_key:synthetic");
        Assert.DoesNotContain("synthetic", text); Assert.Contains("[redacted]", text);
    }
}
