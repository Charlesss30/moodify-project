using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

public sealed class GenreMoodService(MongoStore db)
{
    public sealed record Suggestion(decimal? Valence, decimal? Arousal, string Source,
        string[] MatchedGenres, string[] UnknownGenres, string? RuleVersion);

    public async Task<Suggestion> Suggest(string? names, string type)
    {
        var parts = (names ?? "").Split([',',';','/','|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var normalized = parts.Select(MongoStore.Normalize).ToArray();
        var genres = await db.TheLoais.Where(g => normalized.Contains(g.TenTheLoaiNormalized)).ToListAsync();
        var matched = genres.Where(g => (g.ContentType == type || g.ContentType == "Both") && g.DefaultValence.HasValue && g.DefaultArousal.HasValue).ToArray();
        var unknown = parts.Where(p => !matched.Any(g => MongoStore.Normalize(p) == g.TenTheLoaiNormalized)).ToArray();
        return new(matched.Length == 0 ? null : Math.Round(matched.Average(g => g.DefaultValence!.Value),4),
            matched.Length == 0 ? null : Math.Round(matched.Average(g => g.DefaultArousal!.Value),4), "genre-default",
            matched.Select(g => g.TenTheLoai).ToArray(), unknown,
            matched.Length == 0 ? null : string.Join(";",matched.OrderBy(g => g.TheLoaiID).Select(g => $"{g.TheLoaiID}:{g.MoodVersion}")));
    }

    public async Task<string?> Apply(NoiDungGiaiTri content, string? genres, decimal? valence, decimal? arousal, string? source, bool creating)
    {
        if(valence.HasValue != arousal.HasValue) return "Enter both Valence and Arousal.";
        if(!creating && !valence.HasValue) return null;
        var previousSource = content.Phim?.MoodSource ?? content.Nhac?.MoodSource;
        // Reopening/saving an item never rewrites a previously reviewed annotation.
        if(!creating && source == previousSource && valence == content.Valence && arousal == content.Arousal) return null;
        var suggestion = await Suggest(genres,content.LoaiNoiDung);
        if(!valence.HasValue && suggestion.Valence == null) return "No configured genre. Enter Valence and Arousal or configure the genre first.";
        var automatic = !valence.HasValue || source == "genre-default" && valence == suggestion.Valence && arousal == suggestion.Arousal;
        content.Valence = valence ?? suggestion.Valence!.Value;
        content.Arousal = arousal ?? suggestion.Arousal!.Value;
        var finalSource = automatic ? "genre-default" : "manual";
        if(content.Phim != null) content.Phim.MoodSource = finalSource;
        if(content.Nhac != null) content.Nhac.MoodSource = finalSource;
        content.MoodRuleVersion = automatic ? suggestion.RuleVersion : null;
        content.ReviewStatus = automatic ? "pending" : "reviewed";
        content.ModelVersion = null;
        return null;
    }
}
