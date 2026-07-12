namespace SiloPlayer.Core.Models.Admin;

public class InviteCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public int MaxUses { get; set; }
    public int UseCount { get; set; }
    public int CreatedBy { get; set; }
    public bool Enabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class CreateInviteCodeRequest
{
    public string? Code { get; set; }
    public string Label { get; set; } = "";
    public int MaxUses { get; set; }
}

public class UpdateInviteCodeRequest
{
    public string? Label { get; set; }
    public int? MaxUses { get; set; }
    public bool? Enabled { get; set; }
}

public class TopUpInviteCodeRequest
{
    public int AdditionalUses { get; set; }
}
