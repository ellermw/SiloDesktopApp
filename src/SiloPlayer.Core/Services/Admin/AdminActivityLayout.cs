namespace SiloPlayer.Core.Services.Admin;

public enum AdminActivityLayout { Table, Cards }

public static class AdminActivityLayoutRules
{
    // The automatic choice is made from the first successful list on each open.
    public static AdminActivityLayout Resolve(AdminActivityLayout? saved, int? loadedCount) =>
        saved ?? (loadedCount is <= 8 ? AdminActivityLayout.Cards : AdminActivityLayout.Table);

    public static AdminActivityLayout? Parse(string? saved) => saved switch
    {
        "cards" => AdminActivityLayout.Cards,
        "table" => AdminActivityLayout.Table,
        _ => null,
    };

    public static string Wire(this AdminActivityLayout layout) => layout == AdminActivityLayout.Cards ? "cards" : "table";
}
