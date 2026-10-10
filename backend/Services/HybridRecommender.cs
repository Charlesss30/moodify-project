using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

public sealed record RankedContent(NoiDungGiaiTri Content, double Score, double MoodScore, double? ContentScore, double? CollaborativeScore);

// Transparent baseline: explicit/weak implicit feedback updates the profile at request time.
// This is a feedback loop, not reinforcement learning or a pretrained neural recommender.
public sealed class HybridRecommender(MongoStore db)
{
    public async Task<IReadOnlyList<RankedContent>> RankAsync(List<NoiDungGiaiTri> candidates, int userId, decimal v, decimal a, string mode)
    {
        var ratings = await db.DanhGias.OrderByDescending(x => x.ThoiGian).ThenByDescending(x => x.DanhGiaID).Take(20000).ToListAsync();
        var events = await db.Database.GetCollection<BsonDocument>("interactions").Find(new BsonDocument("accountId", userId))
            .Sort(new BsonDocument("createdAt", -1)).Limit(500).ToListAsync();
        var implicitIds = events.Where(x => x.GetValue("action", "").AsString is "external_open" or "trailer_open")
            .Select(x => x["contentId"].AsString).Distinct().ToHashSet();
        // Profile must not change merely because the request filters Movie vs Song.
        var library = await db.Hydrate(await db.NoiDungGiaiTris.ToListAsync());
        return Rank(candidates, library, ratings, implicitIds, userId, v, a, mode);
    }

    public static IReadOnlyList<RankedContent> Rank(IReadOnlyList<NoiDungGiaiTri> candidates,
        IReadOnlyList<NoiDungGiaiTri> library, IReadOnlyList<DanhGia> ratings, IReadOnlySet<string> implicitIds,
        int userId, decimal v, decimal a, string mode)
    {
        var features = library.Concat(candidates).GroupBy(x => x.NoiDungID).ToDictionary(g => g.Key, g => Features(g.First()));
        var latest = ratings.OrderByDescending(x => x.ThoiGian).ThenByDescending(x => x.DanhGiaID)
            .GroupBy(x => (x.TaiKhoanID, x.NoiDungID)).Select(g => g.First()).ToArray();
        var users = latest.GroupBy(x => x.TaiKhoanID).ToDictionary(g => g.Key,
            g => g.ToDictionary(x => x.NoiDungID, x => (x.SoSao - 3d) / 2d));
        var own = users.GetValueOrDefault(userId) ?? new Dictionary<string, double>();
        var profile = new Dictionary<string, double>();
        foreach (var id in own.Keys.Concat(implicitIds).Distinct())
        {
            if (!features.TryGetValue(id, out var vector)) continue;
            var weight = own.TryGetValue(id, out var stars) ? stars : 0.1; // One bounded weak signal per item.
            foreach (var (key, value) in vector) profile[key] = profile.GetValueOrDefault(key) + weight * value;
        }
        var profileNorm = Math.Sqrt(profile.Values.Sum(x => x * x));
        var neighbors = users.Where(x => x.Key != userId).Select(x => {
            var shared = own.Keys.Intersect(x.Value.Keys).ToArray();
            if (shared.Length < 2) return (Values: x.Value, Similarity: 0d);
            var dot = shared.Sum(k => own[k] * x.Value[k]);
            var norm = Math.Sqrt(shared.Sum(k => own[k] * own[k]) * shared.Sum(k => x.Value[k] * x.Value[k]));
            return (Values: x.Value, Similarity: norm > 0 ? Math.Max(0, dot / norm) * shared.Length / (shared.Length + 2d) : 0);
        }).Where(x => x.Similarity > 0).OrderByDescending(x => x.Similarity).Take(20).ToArray();
        var results = new List<RankedContent>();
        foreach (var item in candidates)
        {
            var mood = MoodScoring.MatchPercent(MoodScoring.DistanceSquared(item.Valence, item.Arousal, v, a)) / 100d;
            var vector = features[item.NoiDungID];
            double? content = profileNorm > 0 && vector.Count > 0
                ? Math.Clamp((vector.Sum(x => x.Value * profile.GetValueOrDefault(x.Key)) / profileNorm + 1) / 2, 0, 1) : null;
            var votes = neighbors.Where(x => x.Values.ContainsKey(item.NoiDungID)).ToArray();
            double? collaborative = votes.Length > 0
                ? Math.Clamp((votes.Sum(x => x.Similarity * x.Values[item.NoiDungID]) / votes.Sum(x => x.Similarity) + 1) / 2, 0, 1) : null;
            var score = mood;
            if (mode == "content") score = content ?? mood;
            if (mode == "hybrid") {
                var numerator = .55 * mood; var denominator = .55;
                if (content.HasValue) { numerator += .30 * content.Value; denominator += .30; }
                if (collaborative.HasValue) { numerator += .15 * collaborative.Value; denominator += .15; }
                score = numerator / denominator;
            }
            // Explicit dislike wins over weak implicit events and discourages repeated disliked items.
            if (mode != "mood" && own.GetValueOrDefault(item.NoiDungID) < 0) score *= .5;
            results.Add(new(item, score, mood, content, collaborative));
        }
        return results.OrderByDescending(x => x.Score).ThenBy(x => MoodScoring.DistanceSquared(x.Content.Valence, x.Content.Arousal, v, a)).ThenBy(x => x.Content.NoiDungID, StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, double> Features(NoiDungGiaiTri item)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        void Add(string kind, string? text, bool split = false) {
            foreach (var part in (split ? (text ?? "").Split([',', ';', '|']) : new[] { text ?? "" }))
            { var normalized = part.Trim().ToUpperInvariant(); if (normalized.Length > 0) keys.Add(item.LoaiNoiDung + ":" + kind + ":" + normalized); }
        }
        Add("genre", item.Phim?.TheLoai ?? item.Nhac?.Genre, true);
        Add("director", item.Phim?.Director, true);
        Add("artist", item.Nhac?.TenNgheSi, true);
        Add("album", item.Nhac?.Album);
        var norm = Math.Sqrt(keys.Count);
        return keys.ToDictionary(x => x, _ => 1 / norm);
    }
}
