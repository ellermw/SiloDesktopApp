namespace SiloPlayer.Core.Models.Home;

public sealed class SeasonalThemeCatalog
{
    public List<SeasonalTheme> Themes { get; set; } = [];
    public int MaxCustomHolidays { get; set; } = 12;
}

public sealed class SeasonalTheme
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string When { get; set; } = "";
    public string? Start { get; set; }
    public string? End { get; set; }
    public bool DatesEditable { get; set; }
    public List<SeasonalVibe> Vibes { get; set; } = [];
}

public sealed class SeasonalVibe
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ListUrl { get; set; } = "";
}

public sealed class SeasonalDates
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
}

public sealed class SeasonalCustomHoliday
{
    public string Name { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public string ListUrl { get; set; } = "";
    public string? Title { get; set; }
}
