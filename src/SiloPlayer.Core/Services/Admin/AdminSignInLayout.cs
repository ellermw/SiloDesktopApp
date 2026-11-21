using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A run of fields from one configuration entry, shown as a numbered step or an Advanced section (official <c>SignInFieldGroup</c>).</summary>
public sealed record AdminSignInFieldGroup(string Id, AdminSignInSchema Schema, string Title, string Description, IReadOnlyList<AdminSignInField> Fields, AdminSignInSection? Section);

/// <summary>A field named by its entry and key.</summary>
public sealed record AdminSignInFieldRef(AdminSignInSchema Schema, AdminSignInField Field);

/// <summary>
/// How a sign-in plugin's configuration is laid out as guided setup (official <c>signInSetupLayout</c>): numbered steps in manifest order, the collapsible
/// groups under Advanced, the login-button name in its own step, the connection test's look-up field in the test step, and the entries this page cannot draw.
/// </summary>
public sealed class AdminSignInLayout
{
    public const string LoginNameKey = "display_name", TestUsernameMetadata = "connection_test_username_field";
    public List<AdminSignInFieldGroup> Steps { get; } = [];
    public List<AdminSignInFieldGroup> Advanced { get; } = [];
    public AdminSignInFieldRef? LoginName { get; private set; }
    public AdminSignInFieldRef? TestUsername { get; private set; }
    public List<AdminSignInSchema> Unsupported { get; } = [];

    /// <summary>The entry's title, else its key with underscores read as spaces and each word capitalized (official <c>humanizeConfigKey</c>).</summary>
    public static string SchemaTitle(AdminSignInSchema schema)
    {
        var title = schema.Title?.Trim();
        return !string.IsNullOrEmpty(title) ? title : string.Join(' ', (schema.Key ?? "").Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    public static AdminSignInLayout Of(AdminSignInProvider provider)
    {
        var layout = new AdminSignInLayout();
        var testRef = provider.Auth?.Metadata[TestUsernameMetadata]?.ToString() is { } named && named.IndexOf('.') is var dot && dot > 0 && dot < named.Length - 1 ? (Schema: named[..dot], Field: named[(dot + 1)..]) : default;
        foreach (var schema in provider.GlobalConfigSchema)
        {
            var form = schema.AdminForm;
            if (form == null) { layout.Unsupported.Add(schema); continue; }
            // A plugin's form is passed through unchecked: it can repeat a key. The page draws one row per key, so the first occurrence stands and the later ones are ignored.
            var fields = new List<AdminSignInField>(); var byKey = new Dictionary<string, AdminSignInField>();
            foreach (var f in form.Fields) if (f != null && byKey.TryAdd(f.Key ?? "", f)) fields.Add(f);
            // The login-button name is never a secret field: what is typed into it is shown in the live preview.
            if (schema.Key == LoginNameKey && fields.Count == 1 && fields[0].Key == "value" && !fields[0].IsSecret) { layout.LoginName = new(schema, fields[0]); continue; }
            string? hidden = null;
            if (testRef.Schema == schema.Key && fields.FirstOrDefault(f => f.Key == testRef.Field) is { } look)
            {
                layout.TestUsername = new(schema, look); hidden = look.Key;
            }
            var sectioned = form.Sections.Where(s => s != null).SelectMany(s => s.FieldKeys).ToHashSet();
            var loose = fields.Where(f => !sectioned.Contains(f.Key) && f.Key != hidden).ToList();
            var described = false;
            if (loose.Count > 0)
            {
                layout.Steps.Add(new(schema.Key, schema, SchemaTitle(schema), schema.Description, loose, null)); described = true;
            }
            foreach (var section in form.Sections.Where(s => s != null))
            {
                // A key listed twice in a section is still one row.
                var inSection = section.FieldKeys.Where(k => k != hidden).Distinct().Select(k => k == null ? null : byKey.GetValueOrDefault(k)).OfType<AdminSignInField>().ToList();
                if (inSection.Count == 0) continue;
                var title = section.Title?.Trim();
                var group = new AdminSignInFieldGroup(schema.Key + "." + section.Key, schema, string.IsNullOrEmpty(title) ? SchemaTitle(schema) : title,
                    !string.IsNullOrEmpty(section.Description) ? section.Description : described ? "" : schema.Description, inSection, section);
                described = true;
                (section.Collapsible ? layout.Advanced : layout.Steps).Add(group);
            }
        }
        return layout;
    }
}
