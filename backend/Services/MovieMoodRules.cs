namespace mood_recommendation.Services;

// Initial application heuristics, not validated emotional labels or AI predictions.
public static class MovieMoodRules
{
    public const string Version = "genre-rule-v1";
    private static readonly (string Name, decimal V, decimal A, string[] Aliases)[] Rules = [
        ("Action", .1m, .8m, ["hành động"]),
        ("Adventure", .4m, .6m, ["phiêu lưu"]),
        ("Comedy", .7m, .4m, ["hài", "hài hước"]),
        ("Drama", -.2m, .1m, ["chính kịch", "tâm lý"]),
        ("Horror", -.7m, .8m, ["kinh dị"]),
        ("Thriller", -.4m, .7m, ["giật gân"]),
        ("Romance", .6m, .1m, ["lãng mạn", "tình cảm"]),
        ("Animation", .6m, .4m, ["hoạt hình"]),
        ("Family", .6m, .2m, ["gia đình"]),
        ("Fantasy", .3m, .5m, ["kỳ ảo", "giả tưởng"]),
        ("Sci-Fi", .1m, .6m, ["khoa học viễn tưởng"]),
        ("Crime", -.4m, .5m, ["tội phạm"]),
        ("Mystery", -.1m, .4m, ["bí ẩn"]),
        ("War", -.6m, .7m, ["chiến tranh"]),
        ("Documentary", 0m, -.3m, ["tài liệu"]),
        ("Biography", .1m, 0m, ["tiểu sử"]),
        ("History", -.1m, .1m, ["lịch sử"]),
        ("Music", .5m, .4m, ["âm nhạc"]),
        ("Musical", .6m, .5m, ["nhạc kịch"]),
        ("Sport", .5m, .6m, ["thể thao"]),
        ("Western", .0m, .5m, ["cao bồi"])
    ];
    public sealed record Suggestion(decimal? Valence, decimal? Arousal, string Source, string[] MatchedGenres, string[] UnknownGenres);
    public static Suggestion Suggest(string? genres)
    {
        var matched = new Dictionary<string, (decimal V, decimal A)>();
        var unknown = new List<string>();
        foreach (var part in (genres ?? "").Split([',',';','/','|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var rule = Rules.FirstOrDefault(r => r.Name.Equals(part, StringComparison.OrdinalIgnoreCase) || r.Aliases.Contains(part, StringComparer.OrdinalIgnoreCase));
            if (rule.Name == null) unknown.Add(part);
            else matched[rule.Name] = (rule.V, rule.A);
        }
        return new(matched.Count == 0 ? null : Math.Round(matched.Values.Average(x => x.V), 4),
            matched.Count == 0 ? null : Math.Round(matched.Values.Average(x => x.A), 4),
            Version, matched.Keys.ToArray(), unknown.ToArray());
    }
}
