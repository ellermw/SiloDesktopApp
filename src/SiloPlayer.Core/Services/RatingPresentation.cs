using System.Globalization;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Services;

public static class RatingPresentation
{
    public static DisplayRating? PrimaryCardRating(double? imdb, double? tmdb)
        => Valid(imdb) ? OutOfTen("imdb", imdb!.Value) : TmdbRating(tmdb);

    public static DisplayRating? TmdbRating(double? value)
        => Valid(value) ? OutOfTen("tmdb", value!.Value) : null;

    private static bool Valid(double? value) => value is > 0 and <= 10 && double.IsFinite(value.Value);

    private static DisplayRating OutOfTen(string source, double value) => new()
    {
        Source = source, Name = source == "imdb" ? "IMDb" : "TMDB", Score = value * 10,
        Display = (Math.Round(value * 10, MidpointRounding.AwayFromZero) / 10).ToString("F1", CultureInfo.InvariantCulture),
    };
}
