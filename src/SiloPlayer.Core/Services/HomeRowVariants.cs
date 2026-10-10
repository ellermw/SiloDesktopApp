using System.Text.Json;
namespace SiloPlayer.Core.Services;

public sealed record HomeRowVariant(string Key, string Label, string? Hint, Dictionary<string, object> Values);
public sealed record HomeRowVariantFamily(string Label, string[] Keys, HomeRowVariant[] Options);

/// <summary>Only variant discriminator keys change; custom floors and library filters survive.</summary>
public static class HomeRowVariants
{
    private static HomeRowVariant Option(string key, string label, string? hint, params (string Key, object Value)[] values)
        => new(key, label, hint, values.ToDictionary(pair => pair.Key, pair => pair.Value));
    public static HomeRowVariantFamily? Family(string type) => type switch
    {
        "continue_watching" => new("Kind", ["continue_type"], [Option("continue_watching_default","Watching","Movies and episodes",("continue_type","watching")), Option("continue_listening_default","Listening","Audiobooks",("continue_type","listening"))]),
        "trending_on_server" => new("Time window", ["window"], [Option("tr_24h","Last 24 hours","What's hot today",("window","24h")),Option("tr_7d","Last 7 days","A steady weekly mix",("window","7d")),Option("tr_30d","Last 30 days","Changes slowly",("window","30d"))]),
        "most_watched" => new("Period", ["window"], [Option("mw_week","This week","The last 7 days",("window","week")),Option("mw_month","This month","The last 30 days",("window","month"))]),
        "trending_discover" => new("Period", ["source","window"], [Option("tdisc_tmdb_day","Today","Changes every day",("source","tmdb"),("window","day")),Option("tdisc_tmdb_week","This week","A steadier weekly list",("source","tmdb"),("window","week"))]),
        "mood_collection" => new("Mood", ["mood"], [Option("mood_feel_good","Feel-good",null,("mood","feel_good")),Option("mood_mind_bending","Mind-bending",null,("mood","mind_bending")),Option("mood_comfort","Comfort rewatches",null,("mood","comfort")),Option("mood_edge_of_seat","Edge of your seat",null,("mood","edge_of_seat")),Option("mood_tearjerker","Tearjerkers",null,("mood","tearjerker")),Option("mood_quiet_sunday","Quiet Sunday",null,("mood","quiet_sunday")),Option("mood_date_night","Date night",null,("mood","date_night")),Option("mood_after_midnight","After midnight",null,("mood","after_midnight"))]),
        "seasonal_themed" => new("Theme", ["enabled_themes","theme"], [Option("se_auto","Holidays","Changes with the calendar",("enabled_themes",new[]{"halloween","christmas","valentines","st_patricks","thanksgiving","summer_blockbuster","saturday_morning"})),Option("se_family_movie_night","Family movie night","Friday and Saturday evenings",("enabled_themes",new[]{"family_movie_night"}))]),
        "editorial_spotlight" => new("Spotlight on", ["subject_type","subject"], [Option("es_director_auto","Director","Films by one director",("subject_type","director")),Option("es_actor","Actor","Titles with one actor",("subject_type","actor")),Option("es_studio","Studio","Titles from one studio",("subject_type","studio")),Option("es_era_80s","The 80s","Films from the 1980s",("subject_type","era"),("subject","1980s"))]),
        "format_showcase" => new("Format", ["format","sort"], [Option("fs_4k","4K","Everything in 4K",("format","4k")),Option("fs_4k_recent","New in 4K","The latest 4K additions",("format","4k"),("sort","recent")),Option("fs_dv","Dolby Vision","Dolby Vision titles",("format","dolby_vision")),Option("fs_hdr","HDR","HDR-mastered titles",("format","hdr"))]),
        _ => null
    };
    private static string? Text(Dictionary<string,object> config, string key) => config.TryGetValue(key,out var value) ? value?.ToString() : null;
    private static bool Equal(Dictionary<string,object> config, Dictionary<string,object> values, string key)
    {
        object? Normal(Dictionary<string,object> source) => source.TryGetValue(key,out var value) && value is not null && value.ToString() != "" && value is not JsonElement { ValueKind: JsonValueKind.Null } ? value : null;
        return HomeSectionWritePolicy.EqualConfig(new(){[key]=Normal(config)!},new(){[key]=Normal(values)!});
    }
    public static string? Selected(string type, Dictionary<string,object> config)
    {
        var family=Family(type);if(family==null)return null;
        if(type=="seasonal_themed")
        {
            if(config.TryGetValue("enabled_themes",out var value))
            { var themes=JsonSerializer.SerializeToElement(value);return themes.ValueKind==JsonValueKind.Array ? themes.GetArrayLength()==1 && themes[0].ValueKind==JsonValueKind.String && themes[0].GetString()=="family_movie_night" ? "se_family_movie_night" : "se_auto" : null; }
            return !config.ContainsKey("enabled_themes") && Text(config,"theme")=="family_movie_night" ? "se_family_movie_night" : null;
        }
        if(type=="editorial_spotlight") return Text(config,"subject_type") switch { "director"=>"es_director_auto","actor"=>"es_actor","studio"=>"es_studio","era" when Text(config,"subject")=="1980s"=>"es_era_80s",_=>null };
        return family.Options.FirstOrDefault(option=>family.Keys.All(key=>Equal(config,option.Values,key)))?.Key;
    }
    public static void Apply(string type, Dictionary<string,object> config,string key)
    {
        var family=Family(type);var option=family?.Options.FirstOrDefault(option=>option.Key==key);if(family==null||option==null)return;
        var rotating=type=="editorial_spotlight" && Text(config,"subject_type") is "director" or "actor" or "studio" && config.TryGetValue("auto_rotate",out var value) && JsonSerializer.SerializeToElement(value).ValueKind==JsonValueKind.True;
        foreach(var discriminator in family.Keys)config.Remove(discriminator);
        foreach(var pair in option.Values)config[pair.Key]=pair.Value;
        if(type!="editorial_spotlight")return;
        if(key=="es_era_80s"){config.Remove("auto_rotate");config.Remove("rotation_cadence");}
        else if(!rotating){config["auto_rotate"]=true;config["rotation_cadence"]="weekly";}
    }
}
