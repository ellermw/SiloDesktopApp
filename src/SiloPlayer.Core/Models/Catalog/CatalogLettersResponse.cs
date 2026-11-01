namespace SiloPlayer.Core.Models.Catalog;

public sealed class CatalogLetter
{
    public string Letter { get; set; } = "";
    public int Count { get; set; }
    public int Position { get; set; }
}

public sealed class CatalogLetterRun
{
    public string Letter { get; set; } = "";
    public int Position { get; set; }
}

public sealed class CatalogLettersResponse
{
    public int Total { get; set; }
    public List<CatalogLetter> Letters { get; set; } = [];
    public List<CatalogLetterRun> Runs { get; set; } = [];

    public string? LetterAtIndex(int index)
    {
        string? letter = null;
        foreach (var run in Runs)
        {
            if (run.Position > index) break;
            letter = run.Letter;
        }
        return letter;
    }

    public void Validate()
    {
        static bool ValidLetter(string letter) => letter == "#" || letter is { Length: 1 } && letter[0] is >= 'A' and <= 'Z';
        if (Total < 0 || Letters is null || Runs is null || Letters.Count > 27 ||
            Letters.Any(x => !ValidLetter(x.Letter) || x.Count <= 0 || x.Position < 0 || x.Position >= Total) ||
            Letters.Select(x => x.Letter).Distinct().Count() != Letters.Count || Letters.Sum(x => (long)x.Count) != Total ||
            (Total > 0 && (Runs.Count == 0 || Runs[0].Position != 0)) ||
            (Total == 0 && Runs.Count != 0))
            throw new InvalidDataException("Invalid catalog letter index.");
        var previous = -1;
        foreach (var run in Runs)
        {
            if (!ValidLetter(run.Letter) || run.Position <= previous || run.Position >= Total || !Letters.Any(x => x.Letter == run.Letter))
                throw new InvalidDataException("Invalid catalog letter runs.");
            previous = run.Position;
        }
        foreach (var letter in Letters)
            if (Runs.FirstOrDefault(x => x.Letter == letter.Letter)?.Position != letter.Position)
                throw new InvalidDataException("Catalog letter position disagrees with its runs.");
    }
}
