using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    private readonly SettingsApi _stateSettings = App.Services.GetRequiredService<SettingsApi>();
    private ApiRequestContext? _pageStateContext;
    private DispatcherTimer? _stateSaveTimer;
    private (int Id, string Search)? _pendingStateSave;

    private async Task<LibraryViewState> RestoreLibraryStateAsync(int libraryId)
    {
        _pageStateContext = _stateSettings.CaptureContext();
        try
        {
            var search = await new LibraryPageStateStore(_stateSettings).ReadAsync(libraryId);
            if (!_isNavigated || _pageStateContext != _stateSettings.CaptureContext()) return new();
            return ParseLibrarySearch(search);
        }
        catch (OperationCanceledException) { return new(); }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error($"Could not restore library view: {ex.Message}"); return new(); }
    }

    private void ScheduleLibraryStateSave(int id, LibraryViewState state)
    {
        if (_pageStateContext != _stateSettings.CaptureContext()) return;
        _pendingStateSave = (id, FormatLibrarySearch(state));
        _stateSaveTimer ??= CreateStateSaveTimer();
        _stateSaveTimer.Stop(); _stateSaveTimer.Start();
    }

    private DispatcherTimer CreateStateSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => FlushLibraryStateSave(); return timer;
    }

    private async void FlushLibraryStateSave()
    {
        _stateSaveTimer?.Stop(); var pending = _pendingStateSave; _pendingStateSave = null;
        if (pending == null || _pageStateContext != _stateSettings.CaptureContext()) return;
        try { await new LibraryPageStateStore(_stateSettings).WriteAsync(pending.Value.Id, pending.Value.Search); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error($"Could not remember library view: {ex.Message}"); }
    }

    private static string FormatLibrarySearch(LibraryViewState state)
    {
        var parts = new List<string>();
        void Param(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}"); }
        Param("tab", state.Tab.ToLowerInvariant()); Param("sort", state.Sort); Param("order", state.Order); Param("match", state.Match);
        Param("type", state.Axis == "author" ? "authors" : state.Axis == "narrator" ? "narrators" : state.Axis != "books" ? state.Axis : state.MediaType);
        var groups = state.QueryGroups;
        if (groups.Count == 0)
        {
            var rules = new List<QueryRule>();
            void Rule(string field, object? value, string op = "is") { if (QueryRuleValues.HasValue(value)) rules.Add(new() { Field = field, Op = op, Value = value }); }
            foreach (var genre in state.Genres.Count > 0 ? state.Genres : [state.Genre ?? ""]) Rule("genre", genre);
            foreach (var (field, value) in new[] { ("type", state.MediaType), ("content_rating", state.ContentRating), ("studio", state.Studio), ("country", state.Country), ("resolution", state.Resolution), ("audio_language", state.AudioLanguage), ("actor", state.Actor), ("director", state.Director), ("writer", state.Writer), ("producer", state.Producer), ("author", state.Author), ("narrator", state.Narrator), ("series", state.Series), ("network", state.Network), ("status", state.MatchStatus) }) Rule(field, value);
            Rule("year", state.YearMin, "gte"); Rule("year", state.YearMax, "lte"); Rule("rating_imdb", state.MinimumRating, "gte");
            Rule("added_at", state.AddedInLast, "in_last"); Rule("release_date", state.ReleasedInLast, "in_last");
            if (state.FourK) Rule("resolution", "2160p"); if (state.Hdr) Rule("hdr", true); if (state.DolbyVision) Rule("dolby_vision", true);
            if (state.WatchStatus == "watched") Rule("watched", true);
            else if (state.WatchStatus == "in_progress") Rule("in_progress", true);
            else if (state.WatchStatus == "unwatched") { Rule("watched", false); Rule("in_progress", false); }
            groups = [new() { Rules = rules }];
            var languages = state.OriginalLanguages.Count > 0 ? state.OriginalLanguages : [state.OriginalLanguage ?? ""];
            var languageRules = languages.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => new QueryRule { Field = "original_language", Op = "is", Value = value }).ToList();
            if (languageRules.Count > 0) groups.Add(new() { Match = "any", Rules = languageRules });
        }
        for (var i = 0; i < groups.Count; i++)
        {
            Param($"groups[{i}][match]", groups[i].Match);
            for (var j = 0; j < groups[i].Rules.Count; j++)
            {
                var rule = groups[i].Rules[j]; var prefix = $"groups[{i}][rules][{j}]";
                Param(prefix + "[field]", rule.Field); Param(prefix + "[op]", rule.Op);
                var json = JsonSerializer.SerializeToElement(rule.Value);
                if (json.ValueKind == JsonValueKind.Array) { var k = 0; foreach (var value in json.EnumerateArray()) Param(prefix + $"[value][{k++}]", QueryRuleValues.Format(value)); }
                else Param(prefix + "[value]", QueryRuleValues.Format(rule.Value));
            }
        }
        return "?" + string.Join("&", parts);
    }

    private static LibraryViewState ParseLibrarySearch(string? search)
    {
        var state = new LibraryViewState(); if (string.IsNullOrWhiteSpace(search)) return state;
        var values = search.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(pair => pair.Length == 2).GroupBy(pair => Uri.UnescapeDataString(pair[0]), StringComparer.Ordinal).ToDictionary(group => group.Key, group => Uri.UnescapeDataString(group.Last()[1].Replace('+', ' ')), StringComparer.Ordinal);
        string? Value(string key) => values.GetValueOrDefault(key);
        state.Tab = Value("tab") switch { "library" => "Library", "collections" => "Collections", _ => "Recommended" };
        state.Sort = Value("sort") ?? "title"; state.Order = Value("order") == "desc" ? "desc" : "asc"; state.Match = Value("match") == "any" ? "any" : "all";
        state.Axis = Value("type") switch { "authors" => "author", "narrators" => "narrator", "books" => "books", "series" => "series", _ => "books" };
        state.MediaType = Value("type") is "authors" or "narrators" or "books" ? null : Value("type");
        state.Genre = Value("genre"); state.YearMin = Value("year_min"); state.YearMax = Value("year_max"); state.ContentRating = Value("content_rating");
        var groupIndexes = values.Keys.Select(key => Regex.Match(key, @"^groups\[(\d+)\]")).Where(match => match.Success).Select(match => int.Parse(match.Groups[1].Value)).Where(index => index < 128).Distinct().Order();
        foreach (var i in groupIndexes)
        {
            var group = new QueryGroup { Match = Value($"groups[{i}][match]") == "any" ? "any" : "all" };
            var ruleIndexes = values.Keys.Select(key => Regex.Match(key, $@"^groups\[{i}\]\[rules\]\[(\d+)\]\[field\]$")).Where(match => match.Success).Select(match => int.Parse(match.Groups[1].Value)).Where(index => index < 512).Distinct().Order();
            foreach (var j in ruleIndexes)
            {
                var prefix = $"groups[{i}][rules][{j}]"; var field = Value(prefix + "[field]")!; var op = Value(prefix + "[op]") ?? "is";
                var indexed = values.Where(pair => pair.Key.StartsWith(prefix + "[value][", StringComparison.Ordinal)).OrderBy(pair => int.Parse(pair.Key[(prefix.Length + 8)..^1])).Select(pair => pair.Value).ToArray();
                try { group.Rules.Add(new() { Field = field, Op = op, Value = QueryRuleValues.Parse(field, op, indexed.Length > 0 ? string.Join(",", indexed) : Value(prefix + "[value]") ?? "") }); }
                catch (FormatException) { group.Rules.Add(new() { Field = field, Op = op, Value = Value(prefix + "[value]") }); }
            }
            state.QueryGroups.Add(group);
        }
        return state;
    }
}
