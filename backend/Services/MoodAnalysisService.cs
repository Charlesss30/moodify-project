using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

public sealed record MoodAnalysisCandidate(
    string Label, double Score, int? MoodId, bool DecisionEligible);

public sealed record MoodAnalysisResponse(
    string? AnalysisId,
    string Status,
    string Source,
    string? ModelVersion,
    string MappingVersion,
    string Language,
    IReadOnlyList<MoodAnalysisCandidate> Candidates,
    IReadOnlyList<MoodAnalysisCandidate> DecisionCandidates,
    int? SuggestedMoodId,
    bool RequiresConfirmation,
    string ScoreType,
    DateTime? ExpiresAt,
    string? Message);

public sealed record AnalysisProvenance(
    string AnalysisId,
    string ModelVersion,
    string MappingVersion,
    DateTime ExpiresAt,
    string MoodConfigurationHash,
    string AnalysisSource,
    string DecisionSource);

public sealed record InferenceRuntimeSnapshot(
    bool Enabled,
    bool Ready,
    string ModelVersion,
    string Language,
    string? ArtifactHash,
    string? ConfigHash,
    string? InferenceDevice,
    string? DeviceName,
    string MappingVersion);

public sealed partial class MoodAnalysisService : IDisposable
{
    public const string ScoreType = "uncalibrated_sigmoid_not_accuracy";
    private static readonly TimeSpan AnalysisLifetime = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> ExpectedLabels = new(StringComparer.Ordinal)
    {
        "admiration", "amusement", "anger", "annoyance", "approval", "caring",
        "confusion", "curiosity", "desire", "disappointment", "disapproval",
        "disgust", "embarrassment", "excitement", "fear", "gratitude", "grief",
        "joy", "love", "nervousness", "optimism", "pride", "realization",
        "relief", "remorse", "sadness", "surprise", "neutral"
    };

    private readonly HttpClient _http;
    private readonly MemoryCache _cache;
    private readonly MongoStore _db;
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyDictionary<string, string?> _mapping;
    private readonly string _mappingVersion;
    private readonly string _modelVersion;

    public MoodAnalysisService(
        IHttpClientFactory clients,
        MongoStore db,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _http = clients.CreateClient("EmotionInference");
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        _db = db;
        _configuration = configuration;
        _modelVersion = configuration["AI:ModelVersion"] ?? "roberta_large_asl_en_01";

        var path = Path.Combine(environment.ContentRootPath, "Data", "emotion-mood-mapping.json");
        using var mappingDocument = JsonDocument.Parse(File.ReadAllText(path));
        var root = mappingDocument.RootElement;
        _mappingVersion = root.GetProperty("version").GetString()
            ?? throw new InvalidDataException("Emotion mapping has no version.");
        var entries = root.GetProperty("labels").EnumerateArray()
            .Select(item => new
            {
                Label = item.GetProperty("label").GetString(),
                MoodName = item.GetProperty("moodName").ValueKind == JsonValueKind.Null
                    ? null
                    : item.GetProperty("moodName").GetString()
            })
            .ToArray();
        if (entries.Length != ExpectedLabels.Count
            || entries.Any(item => item.Label is null || !ExpectedLabels.Contains(item.Label))
            || entries.Select(item => item.Label).Distinct(StringComparer.Ordinal).Count() != ExpectedLabels.Count)
        {
            throw new InvalidDataException("Emotion mapping must declare each model label exactly once.");
        }
        _mapping = entries.ToDictionary(item => item.Label!, item => item.MoodName, StringComparer.Ordinal);
    }

    public async Task<MoodAnalysisResponse> AnalyzeAsync(
        string text,
        string language,
        int userId,
        bool issueAnalysisToken,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return Response("unsupported_language", null, [], null,
                "This experimental model accepts English only.");
        }
        if (!_configuration.GetValue<bool>("AI:Enabled"))
            return Unavailable("English AI inference is disabled until real model validation completes.");
        if (issueAnalysisToken)
        {
            var state = await _db.Database.GetCollection<MongoDB.Bson.BsonDocument>("aiModelState")
                .Find(item => item["_id"] == "active").FirstOrDefaultAsync(cancellationToken);
            if (state?.GetValue("activeVersion", MongoDB.Bson.BsonNull.Value).IsString != true
                || state["activeVersion"].AsString != _modelVersion)
                return Unavailable("No validated English model is active. Choose a mood manually.");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("predict", new { text, language = "en" }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Unavailable("English AI inference timed out. You can choose a mood manually.");
        }
        catch (HttpRequestException)
        {
            return Unavailable("English AI inference is unavailable. You can choose a mood manually.");
        }

        InferenceResponse? inference;
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                return Response("unknown", null, [], null,
                    "The text exceeds model limits or is not supported. Shorten it or choose a mood manually.");
            }
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                return Unavailable("English AI inference is busy or unavailable. You can choose a mood manually.");
            if (!response.IsSuccessStatusCode)
                return Unavailable("English AI inference returned an unavailable response.");
            try
            {
                inference = await response.Content.ReadFromJsonAsync<InferenceResponse>(
                    cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                return Unavailable("English AI inference returned an invalid response.");
            }
        }
        if (!IsValid(inference))
            return Unavailable("English AI inference returned data outside the supported contract.");

        var mapping = await GetMappingAsync(cancellationToken);
        var moods = await _db.TamTrangs.ToListAsync(cancellationToken);
        var moodConfigurationHashes = moods.ToDictionary(
            mood => mood.TamTrangID, MoodConfigurationHash);
        var validInference = inference!;
        var inferenceCandidates = validInference.Candidates!;
        var decisionLabels = validInference.DecisionCandidates!
            .Select(candidate => candidate!.Label!)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = inferenceCandidates.Select(candidate =>
        {
            var validCandidate = candidate!;
            var label = validCandidate.Label!;
            return new MoodAnalysisCandidate(
                label,
                validCandidate.Score,
                ResolveMoodId(label, moods, mapping),
                decisionLabels.Contains(label));
        }).ToArray();
        var decisionCandidates = validInference.DecisionCandidates!
            .Select(candidate =>
            {
                var validCandidate = candidate!;
                return new MoodAnalysisCandidate(
                    validCandidate.Label!,
                    validCandidate.Score,
                    ResolveMoodId(validCandidate.Label!, moods, mapping),
                    true);
            }).ToArray();
        var moodIds = decisionCandidates.Where(candidate => candidate.MoodId.HasValue)
            .Select(candidate => candidate.MoodId!.Value).Distinct().ToArray();
        var leadingMoodId = candidates[0].MoodId;
        var status = !validInference.DecisionEligible || !leadingMoodId.HasValue
            ? "unknown"
            : moodIds.Length > 1
            ? "ambiguous"
            : "matched";
        int? suggestedMoodId = status == "matched" ? leadingMoodId : null;
        string? analysisId = null;
        DateTime? expiresAt = null;
        if (issueAnalysisToken)
        {
            analysisId = Guid.NewGuid().ToString("N");
            expiresAt = DateTime.UtcNow.Add(AnalysisLifetime);
            _cache.Set(CacheKey(analysisId), new AnalysisEntry(userId, validInference.ModelVersion!,
                mapping.Version, expiresAt.Value, suggestedMoodId, moodConfigurationHashes),
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = AnalysisLifetime,
                    Size = 1
                });
        }

        return new MoodAnalysisResponse(
            analysisId, status, "ai", validInference.ModelVersion, mapping.Version, "en",
            candidates, decisionCandidates, suggestedMoodId, true, ScoreType, expiresAt,
            status == "unknown"
                ? !validInference.DecisionEligible
                    ? "No emotion passed the model decision thresholds. Choose a mood manually."
                    : "The leading emotion has no mood mapping. Choose a mood manually."
                : status == "ambiguous"
                    ? "Threshold-qualified emotions map to different moods. Choose a mood manually."
                    : "Experimental English AI suggestion; confirm or change the mood.");
    }

    public async Task<object> RuntimeStatusAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetRuntimeSnapshotAsync(cancellationToken);
        var mapping = await MappingViewAsync(cancellationToken);

        return new
        {
            modelVersion = snapshot.ModelVersion,
            language = snapshot.Language,
            runtimeStatus = snapshot.Ready ? "Ready" : "Unavailable",
            enabled = snapshot.Enabled,
            artifactHash = snapshot.ArtifactHash,
            configHash = snapshot.ConfigHash,
            inferenceDevice = snapshot.InferenceDevice,
            deviceName = snapshot.DeviceName,
            mappingVersion = snapshot.MappingVersion,
            mapping = mapping.labels
        };
    }

    public async Task<InferenceRuntimeSnapshot> GetRuntimeSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var mapping = await GetMappingAsync(cancellationToken);
        var enabled = _configuration.GetValue<bool>("AI:Enabled");
        InferenceHealth? health = null;
        if (enabled)
        {
            try
            {
                using var response = await _http.GetAsync("health/ready", cancellationToken);
                if (response.IsSuccessStatusCode)
                    health = await response.Content.ReadFromJsonAsync<InferenceHealth>(
                        cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException) { }
            catch (JsonException) { }
            catch (OperationCanceledException) { }
        }

        var artifactHash = _configuration["AI:ArtifactHash"];
        var ready = health?.ModelVersion == _modelVersion
            && health.Language == "en"
            && health.ScoreType == ScoreType
            && string.Equals(health.ArtifactHash, artifactHash, StringComparison.OrdinalIgnoreCase)
            && IsSha256(health.ConfigHash)
            && health.InferenceDevice is "cpu" or "cuda"
            && !string.IsNullOrWhiteSpace(health.DeviceName);
        return new InferenceRuntimeSnapshot(
            enabled, ready, _modelVersion, "en", artifactHash,
            ready ? health!.ConfigHash : null,
            ready ? health!.InferenceDevice : null,
            ready ? health!.DeviceName : null,
            mapping.Version);
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public AnalysisReservation? TryReserveRecommendation(
        string? analysisId, int userId, int selectedMoodId, string moodConfigurationHash, string requestHash, string? mappingVersion)
    {
        if (string.IsNullOrWhiteSpace(analysisId))
            return new AnalysisReservation(null, null, null, false);
        if (!_cache.TryGetValue<AnalysisEntry>(CacheKey(analysisId), out var entry)
            || entry is null
            || entry.UserId != userId
            || entry.ExpiresAt <= DateTime.UtcNow
            || entry.MappingVersion != mappingVersion
            || entry.ModelVersion != _modelVersion
            || !entry.MoodConfigurationHashes.TryGetValue(selectedMoodId, out var analyzedMoodHash)
            || analyzedMoodHash != moodConfigurationHash)
            return null;

        if (!entry.RecommendationGate.Wait(0)) return null;
        if (entry.CachedResponse is not null)
        {
            var mayReplay = entry.RecommendedMoodId == selectedMoodId && entry.RequestHash == requestHash;
            var cachedResponse = entry.CachedResponse;
            entry.RecommendationGate.Release();
            return mayReplay
                ? new AnalysisReservation(null, null, cachedResponse, false)
                : null;
        }

        var decisionSource = entry.SuggestedMoodId == selectedMoodId
            ? "ai"
            : "manual";
        var provenance = new AnalysisProvenance(analysisId, entry.ModelVersion,
            entry.MappingVersion, entry.ExpiresAt, moodConfigurationHash, "ai", decisionSource);
        return new AnalysisReservation(entry, provenance, null, true);
    }

    private static int? ResolveMoodId(string label, IReadOnlyCollection<TamTrang> moods, EmotionMappingState mapping)
    {
        var id = mapping.Labels.FirstOrDefault(x => x.Label == label)?.MoodId;
        return id.HasValue && moods.Any(x => x.TamTrangID == id) ? id : null;
    }

    public static string MoodConfigurationHash(TamTrang mood)
    {
        var value = string.Join('|',
            mood.TamTrangID.ToString(CultureInfo.InvariantCulture),
            mood.TenTamTrang.Trim().ToUpperInvariant(),
            mood.MinValence.ToString("G29", CultureInfo.InvariantCulture),
            mood.MaxValence.ToString("G29", CultureInfo.InvariantCulture),
            mood.MinArousal.ToString("G29", CultureInfo.InvariantCulture),
            mood.MaxArousal.ToString("G29", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private bool IsValid(InferenceResponse? result)
    {
        if (result is null
            || result.ModelVersion != _modelVersion
            || result.Language != "en"
            || result.ScoreType != ScoreType
            || result.RequiresConfirmation != true
            || result.DecisionEligible != (result.Status == "accepted"))
            return false;

        var emotionScores = result.EmotionScores;
        var candidates = result.Candidates;
        var decisionCandidates = result.DecisionCandidates;
        if (emotionScores is null
            || emotionScores.Count != ExpectedLabels.Count
            || !ExpectedLabels.SetEquals(emotionScores.Keys)
            || emotionScores.Values.Any(score => !double.IsFinite(score) || score < 0 || score > 1)
            || candidates is null
            || candidates.Count is < 1 or > 5
            || decisionCandidates is null
            || decisionCandidates.Count > ExpectedLabels.Count)
            return false;

        var seenLabels = new HashSet<string>(StringComparer.Ordinal);
        double previousScore = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            if (candidate?.Label is not { } label
                || !ExpectedLabels.Contains(label)
                || !double.IsFinite(candidate.Score)
                || candidate.Score < 0
                || candidate.Score > 1
                || !emotionScores.TryGetValue(label, out var score)
                || score != candidate.Score
                || candidate.Score > previousScore
                || !seenLabels.Add(label))
                return false;
            previousScore = candidate.Score;
        }
        seenLabels.Clear();
        previousScore = double.PositiveInfinity;
        foreach (var candidate in decisionCandidates)
        {
            if (candidate?.Label is not { } label
                || !ExpectedLabels.Contains(label)
                || !double.IsFinite(candidate.Score)
                || candidate.Score < 0
                || candidate.Score > 1
                || !emotionScores.TryGetValue(label, out var score)
                || score != candidate.Score
                || candidate.Score > previousScore
                || !seenLabels.Add(label))
                return false;
            previousScore = candidate.Score;
        }
        if (result.DecisionEligible
            && (decisionCandidates.Count == 0
                || decisionCandidates[0]?.Label != result.SuggestedEmotion
                || !decisionCandidates.Any(candidate => candidate?.Label == candidates[0]?.Label)))
            return false;
        var topScore = emotionScores.Values.Max();
        return result.Status is "accepted" or "needs_confirmation"
            && result.SuggestedEmotion == candidates[0]!.Label
            && result.Score == topScore
            && result.Score == candidates[0]!.Score;
    }

    private MoodAnalysisResponse Unavailable(string message) =>
        Response("unavailable", null, [], null, message);

    private MoodAnalysisResponse Response(string status, string? analysisId,
        IReadOnlyList<MoodAnalysisCandidate> candidates, int? moodId, string message) =>
        new(analysisId, status, "ai", null, _mappingVersion, "en", candidates, [], moodId,
            true, ScoreType, null, message);

    private static string CacheKey(string analysisId) => $"mood-analysis:{analysisId}";

    public void Dispose() => _cache.Dispose();

    internal sealed class AnalysisEntry
    {
        public AnalysisEntry(
            int userId,
            string modelVersion,
            string mappingVersion,
            DateTime expiresAt,
            int? suggestedMoodId,
            IReadOnlyDictionary<int, string> moodConfigurationHashes)
        {
            UserId = userId;
            ModelVersion = modelVersion;
            MappingVersion = mappingVersion;
            ExpiresAt = expiresAt;
            SuggestedMoodId = suggestedMoodId;
            MoodConfigurationHashes = moodConfigurationHashes;
        }

        public int UserId { get; }
        public string ModelVersion { get; }
        public string MappingVersion { get; }
        public DateTime ExpiresAt { get; }
        public int? SuggestedMoodId { get; }
        public IReadOnlyDictionary<int, string> MoodConfigurationHashes { get; }
        public SemaphoreSlim RecommendationGate { get; } = new(1, 1);
        public object? CachedResponse { get; set; }
        public int? RecommendedMoodId { get; set; }
        public string? RequestHash { get; set; }
    }

    public sealed class AnalysisReservation : IDisposable
    {
        private AnalysisEntry? _entry;
        private bool _ownsGate;

        internal AnalysisReservation(
            AnalysisEntry? entry,
            AnalysisProvenance? provenance,
            object? cachedResponse,
            bool ownsGate)
        {
            _entry = entry;
            Provenance = provenance;
            CachedResponse = cachedResponse;
            _ownsGate = ownsGate;
        }

        public AnalysisProvenance? Provenance { get; }
        public object? CachedResponse { get; }

        public void Complete(object response, int moodId, string requestHash)
        {
            if (_entry is null || !_ownsGate) return;
            _entry.CachedResponse = response;
            _entry.RecommendedMoodId = moodId;
            _entry.RequestHash = requestHash;
        }

        public void Dispose()
        {
            var reservedEntry = Interlocked.Exchange(ref _entry, null);
            if (reservedEntry is not null && _ownsGate)
            {
                _ownsGate = false;
                reservedEntry.RecommendationGate.Release();
            }
        }
    }

    private sealed class InferenceResponse
    {
        [JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
        [JsonPropertyName("language")] public string? Language { get; init; }
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("suggested_emotion")] public string? SuggestedEmotion { get; init; }
        [JsonPropertyName("score")] public double Score { get; init; }
        [JsonPropertyName("requires_confirmation")] public bool? RequiresConfirmation { get; init; }
        [JsonPropertyName("score_type")] public string? ScoreType { get; init; }
        [JsonPropertyName("decision_eligible")] public bool DecisionEligible { get; init; }
        [JsonPropertyName("decision_candidates")] public List<InferenceCandidate?>? DecisionCandidates { get; init; }
        [JsonPropertyName("emotion_scores")] public Dictionary<string, double>? EmotionScores { get; init; }
        [JsonPropertyName("candidates")] public List<InferenceCandidate?>? Candidates { get; init; }
    }

    private sealed class InferenceHealth
    {
        [JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
        [JsonPropertyName("artifact_hash")] public string? ArtifactHash { get; init; }
        [JsonPropertyName("config_hash")] public string? ConfigHash { get; init; }
        [JsonPropertyName("language")] public string? Language { get; init; }
        [JsonPropertyName("score_type")] public string? ScoreType { get; init; }
        [JsonPropertyName("inference_device")] public string? InferenceDevice { get; init; }
        [JsonPropertyName("device_name")] public string? DeviceName { get; init; }
    }

    private sealed class InferenceCandidate
    {
        [JsonPropertyName("label")] public string? Label { get; init; }
        [JsonPropertyName("score")] public double Score { get; init; }
    }
}
