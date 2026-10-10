using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.Text.Json;

namespace mood_recommendation.Services;

public sealed class EmotionMappingEntry
{
    public string Label { get; set; } = "";
    public int? MoodId { get; set; }
    public string? SeedName { get; set; }
}
public sealed class EmotionMappingState
{
    [BsonId] public string Id { get; set; } = "active";
    public long Revision { get; set; }
    public string Version { get; set; } = "";
    public List<EmotionMappingEntry> Labels { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}
public sealed record MappingChoice(string Label, int? MoodId);
public sealed record SaveMappingRequest(long ExpectedRevision, List<MappingChoice>? Labels);
public sealed record MappingDisplay(string label, int? moodId, string? moodName, string status);
public sealed record MappingView(string version, long revision, IReadOnlyList<MappingDisplay> labels, object moods);

public sealed partial class MoodAnalysisService
{
    private readonly SemaphoreSlim _mappingGate = new(1, 1);

    public async Task<EmotionMappingState> GetMappingAsync(CancellationToken ct)
    {
        await _mappingGate.WaitAsync(ct);
        try { return await LoadMappingAsync(ct); }
        finally { _mappingGate.Release(); }
    }

    private async Task<EmotionMappingState> LoadMappingAsync(CancellationToken ct)
    {
        var collection = _db.Database.GetCollection<EmotionMappingState>("aiEmotionMappings");
        var current = await collection.Find(x => x.Id == "active").FirstOrDefaultAsync(ct);
        var moods = await _db.TamTrangs.ToListAsync(ct);
        if (current is null)
        {
            current = new EmotionMappingState { Version = _mappingVersion + "-bindings-1", Revision = 1,
                UpdatedAt = DateTime.UtcNow,
                Labels = _mapping.Select(x => new EmotionMappingEntry { Label = x.Key, SeedName = x.Value }).ToList() };
            BindSeeds(current, moods);
            try { await collection.InsertOneAsync(current, cancellationToken: ct); }
            catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
            { return await LoadMappingAsync(ct); }
        }
        else if (BindSeeds(current, moods))
        {
            var revision = current.Revision;
            current.Revision++;
            current.Version = "emotion-map-" + Guid.NewGuid().ToString("N");
            current.UpdatedAt = DateTime.UtcNow;
            var result = await collection.ReplaceOneAsync(x => x.Id == "active" && x.Revision == revision, current, cancellationToken: ct);
            if (result.ModifiedCount == 0) return await LoadMappingAsync(ct);
        }
        return current;
    }

    private static bool BindSeeds(EmotionMappingState state, IReadOnlyCollection<Models.TamTrang> moods)
    {
        var changed = false;
        foreach (var entry in state.Labels.Where(x => x.MoodId is null && x.SeedName is not null))
        {
            var matches = moods.Where(x => string.Equals(x.TenTamTrang.Trim(), entry.SeedName!.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) continue;
            entry.MoodId = matches[0].TamTrangID;
            entry.SeedName = null; // Once bound, never rebind by display name, including after deletion.
            changed = true;
        }
        return changed;
    }

    public async Task<MappingView> MappingViewAsync(CancellationToken ct)
    {
        var mapping = await GetMappingAsync(ct);
        var moods = await _db.TamTrangs.ToListAsync(ct);
        var labels = mapping.Labels.OrderBy(x => x.Label).Select(x => {
            var mood = moods.FirstOrDefault(m => m.TamTrangID == x.MoodId);
            return new MappingDisplay(x.Label, x.MoodId, mood?.TenTamTrang,
                x.MoodId.HasValue ? mood is null ? "invalid_missing_mood" : "experimental_user_confirmed"
                : x.SeedName is null ? "unmapped" : "unresolved_seed");
        }).ToArray();
        return new MappingView(mapping.Version, mapping.Revision, labels,
            moods.Select(m => new { id = m.TamTrangID, name = m.TenTamTrang }).ToArray());
    }

    public async Task<object> CoverageAsync(CancellationToken ct)
    {
        var moods = await _db.TamTrangs.ToListAsync(ct);
        var contents = await _db.NoiDungGiaiTris.ToListAsync(ct);
        return moods.SelectMany(mood => new[] { "Movie", "Song" }.Select(type => {
            var items = contents.Where(x => x.LoaiNoiDung == type).ToArray();
            var v = (mood.MinValence + mood.MaxValence) / 2;
            var a = (mood.MinArousal + mood.MaxArousal) / 2;
            var nearest = items.OrderBy(x => MoodScoring.DistanceSquared(v, a, x.Valence, x.Arousal)).ThenBy(x => x.NoiDungID).FirstOrDefault();
            return new { moodId = mood.TamTrangID, mood = mood.TenTamTrang, type,
                total = items.Length, reviewed = items.Count(x => x.ReviewStatus == "reviewed"),
                pending = items.Count(x => x.ReviewStatus == "pending"),
                nearestContentId = nearest?.NoiDungID,
                nearestDistanceSquared = nearest is null ? (decimal?)null : MoodScoring.DistanceSquared(v, a, nearest.Valence, nearest.Arousal) };
        })).ToArray();
    }

    public async Task<RegistryOperation> SaveMappingAsync(SaveMappingRequest request, int actorId, string actorRole, CancellationToken ct)
    {
        if (request.Labels is null || request.Labels.Count != ExpectedLabels.Count
            || request.Labels.Any(x => x is null || !ExpectedLabels.Contains(x.Label))
            || request.Labels.Select(x => x.Label).Distinct(StringComparer.Ordinal).Count() != ExpectedLabels.Count)
            return new(400, new { message = "Declare all 28 emotion labels exactly once." });
        await _mappingGate.WaitAsync(ct);
        try
        {
            var current = await LoadMappingAsync(ct);
            if (current.Revision != request.ExpectedRevision)
                return new(409, new { message = "Mapping changed. Reload before approving." });
            var ids = (await _db.TamTrangs.ToListAsync(ct)).Select(x => x.TamTrangID).ToHashSet();
            if (request.Labels.Any(x => x.MoodId.HasValue && !ids.Contains(x.MoodId.Value)))
                return new(400, new { message = "Every mapped mood ID must exist." });
            var collection = _db.Database.GetCollection<EmotionMappingState>("aiEmotionMappings");
            var archive = new EmotionMappingState { Id = current.Version, Version = current.Version,
                Revision = current.Revision, Labels = current.Labels, UpdatedAt = current.UpdatedAt, UpdatedBy = current.UpdatedBy };
            await collection.ReplaceOneAsync(x => x.Id == archive.Id, archive, new ReplaceOptions { IsUpsert = true }, ct);
            var next = new EmotionMappingState { Revision = current.Revision + 1,
                Version = "emotion-map-" + Guid.NewGuid().ToString("N"), UpdatedAt = DateTime.UtcNow, UpdatedBy = actorId,
                Labels = request.Labels.Select(x => new EmotionMappingEntry { Label = x.Label, MoodId = x.MoodId }).ToList() };
            var write = await collection.ReplaceOneAsync(x => x.Id == "active" && x.Revision == request.ExpectedRevision, next, cancellationToken: ct);
            if (write.ModifiedCount == 0) return new(409, new { message = "Mapping changed. Reload before approving." });
            await _db.Database.GetCollection<BsonDocument>("aiAudit").InsertOneAsync(new BsonDocument {
                ["_id"] = Guid.NewGuid().ToString("N"), ["actorId"] = actorId, ["actorRole"] = actorRole, ["action"] = "mapping_approve",
                ["modelVersion"] = _modelVersion, ["outcome"] = "succeeded", ["createdAt"] = DateTime.UtcNow,
                ["detail"] = "Mapping " + current.Version + " -> " + next.Version
            }, cancellationToken: ct);
            return new(200, new { version = next.Version, revision = next.Revision });
        }
        finally { _mappingGate.Release(); }
    }
}

