namespace ContinuumPlayer.Core.Models.Home;
public class HomeLayoutResponse
{
    public List<HomeSection> Sections { get; set; } = [];
}
public class HomeSection
{
    public string Id { get; set; } = "";
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Featured { get; set; }
    public int ItemLimit { get; set; }
    public bool IsCustom { get; set; }
    public bool Customized { get; set; }
}
