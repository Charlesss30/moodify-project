using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;

namespace mood_recommendation.Services;

public sealed record VietnameseCandidate(
    [property: JsonPropertyName("model_version")] string ModelVersion,
    [property: JsonPropertyName("checkpoint")] string Checkpoint,
    [property: JsonPropertyName("tokenizer_available")] bool TokenizerAvailable);
public sealed record VietnameseCatalog(IReadOnlyList<VietnameseCandidate> Items, string? ActiveVersion);
public sealed record VietnameseRuntime(string Status, bool Ready, string? ModelVersion, string? Language, string? InferenceDevice);
public sealed record VietnameseActivation(int StatusCode, string? Message, string? ActiveVersion, string? Device);
public sealed record VietnameseEmotionCandidate(string Label, double Score);
public sealed record VietnamesePrediction(string ModelVersion, string Emotion, double Confidence, bool Accepted,
    IReadOnlyList<VietnameseEmotionCandidate> EmotionCandidates,
    double Valence, double Arousal, int MoodId, string MoodName, decimal MoodValence, decimal MoodArousal);

public sealed class VietnameseEmotionService(IHttpClientFactory clients, MongoStore db)
{
    public async Task<VietnameseRuntime> RuntimeStatusAsync(CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("VietnameseInference").GetAsync("health/vi/ready", ct);
            if (!response.IsSuccessStatusCode) return new("unavailable", false, null, "vi", null);
            var health = await response.Content.ReadFromJsonAsync<ViHealth>(cancellationToken: ct);
            return new("ready", true, health?.ModelVersion, health?.Language, health?.Device);
        }
        catch (HttpRequestException) { return new("unavailable", false, null, "vi", null); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new("unavailable", false, null, "vi", null); }
    }

    public async Task<VietnameseCatalog> CandidatesAsync(CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("VietnameseInference").GetAsync("models/vi", ct);
            if (!response.IsSuccessStatusCode) return new([], null);
            var catalog = await response.Content.ReadFromJsonAsync<ViCatalog>(cancellationToken: ct);
            return new(catalog?.Items ?? [], catalog?.ActiveVersion);
        }
        catch (HttpRequestException) { return new([], null); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new([], null); }
    }

    public async Task<VietnameseActivation> ActivateAsync(string version, CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("VietnameseInference")
                .PostAsJsonAsync("models/vi/activate", new { version }, ct);
            var result = await response.Content.ReadFromJsonAsync<ViActivation>(cancellationToken: ct);
            return new((int)response.StatusCode, result?.Message ?? result?.Detail, result?.ActiveVersion, result?.Device);
        }
        catch (HttpRequestException) { return new(503, "Vietnamese inference service is unavailable.", null, null); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(503, "Vietnamese model loading timed out.", null, null); }
    }

    public async Task<VietnamesePrediction?> AnalyzeAsync(string text, CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("VietnameseInference")
                .PostAsJsonAsync("predict-vi", new { text, language = "vi" }, ct);
            if (!response.IsSuccessStatusCode) return null;
            var prediction = await response.Content.ReadFromJsonAsync<ViPrediction>(cancellationToken: ct);
            if (prediction is null || string.IsNullOrWhiteSpace(prediction.ModelVersion)
                || prediction.Language != "vi" || !double.IsFinite(prediction.Valence) || prediction.Valence is < -1 or > 1
                || !double.IsFinite(prediction.Arousal) || prediction.Arousal is < -1 or > 1
                || !double.IsFinite(prediction.Confidence) || prediction.Confidence is < 0 or > 1
                || prediction.EmotionCandidates.Count is < 1 or > 3
                || prediction.EmotionCandidates.Any(item => string.IsNullOrWhiteSpace(item.Label)
                    || !double.IsFinite(item.Score) || item.Score is < 0 or > 1)) return null;

            var moods = await db.TamTrangs.ToListAsync(ct);
            if (moods.Count == 0) return null;
            var mood = moods.Select(item => new
                {
                    Mood = item,
                    Valence = (double)((item.MinValence + item.MaxValence) / 2m),
                    Arousal = (double)((item.MinArousal + item.MaxArousal) / 2m)
                })
                .OrderBy(item => Math.Pow(item.Valence - prediction.Valence, 2) + Math.Pow(item.Arousal - prediction.Arousal, 2))
                .ThenBy(item => item.Mood.TamTrangID).First();
            return new(prediction.ModelVersion, prediction.Emotion, prediction.Confidence, prediction.Accepted,
                prediction.EmotionCandidates, prediction.Valence, prediction.Arousal, mood.Mood.TamTrangID, mood.Mood.TenTamTrang,
                (mood.Mood.MinValence + mood.Mood.MaxValence) / 2m,
                (mood.Mood.MinArousal + mood.Mood.MaxArousal) / 2m);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private sealed class ViHealth
    {
        [JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
        [JsonPropertyName("language")] public string? Language { get; init; }
        [JsonPropertyName("inference_device")] public string? Device { get; init; }
    }
    private sealed class ViCatalog
    {
        [JsonPropertyName("items")] public List<VietnameseCandidate> Items { get; init; } = [];
        [JsonPropertyName("active_version")] public string? ActiveVersion { get; init; }
    }
    private sealed class ViActivation
    {
        [JsonPropertyName("message")] public string? Message { get; init; }
        [JsonPropertyName("detail")] public string? Detail { get; init; }
        [JsonPropertyName("active_version")] public string? ActiveVersion { get; init; }
        [JsonPropertyName("device")] public string? Device { get; init; }
    }
    private sealed class ViPrediction
    {
        [JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
        [JsonPropertyName("language")] public string? Language { get; init; }
        [JsonPropertyName("emotion")] public string Emotion { get; init; } = "";
        [JsonPropertyName("confidence")] public double Confidence { get; init; }
        [JsonPropertyName("accepted")] public bool Accepted { get; init; }
        [JsonPropertyName("emotion_candidates")] public List<VietnameseEmotionCandidate> EmotionCandidates { get; init; } = [];
        [JsonPropertyName("valence")] public double Valence { get; init; }
        [JsonPropertyName("arousal")] public double Arousal { get; init; }
    }
}
