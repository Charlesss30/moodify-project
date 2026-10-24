using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

public sealed record ValenceArousalMoodCandidate(int MoodId, string MoodName);
public sealed record ValenceArousalMoodResult(
    string ModelVersion,
    double Valence,
    double Arousal,
    int MoodId,
    string MoodName,
    decimal MoodValence,
    decimal MoodArousal,
    IReadOnlyList<ValenceArousalMoodCandidate> MoodCandidates);

public sealed record ValenceArousalCandidate(
    [property: JsonPropertyName("model_version")] string ModelVersion,
    [property: JsonPropertyName("checkpoint")] string Checkpoint,
    [property: JsonPropertyName("tokenizer_available")] bool TokenizerAvailable);
public sealed record ValenceArousalCatalog(IReadOnlyList<ValenceArousalCandidate> Items, string? ActiveVersion);
public sealed record ValenceArousalRuntime(string Status, bool Ready, string? ModelVersion, string? Language, string? InferenceDevice);
public sealed record ValenceArousalActivation(int StatusCode, string? Message, string? ActiveVersion, string? Device);

public sealed class ValenceArousalAnalysisService(
    IHttpClientFactory clients,
    MongoStore db,
    IConfiguration configuration)
{
    public async Task<ValenceArousalRuntime> RuntimeStatusAsync(CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("ValenceArousalInference").GetAsync("health/va/ready", ct);
            if (!response.IsSuccessStatusCode)
                return new("unavailable", false, null, "en", null);
            var health = await response.Content.ReadFromJsonAsync<VaHealth>(cancellationToken: ct);
            return new("ready", true, health?.ModelVersion, health?.Language, health?.Device);
        }
        catch (HttpRequestException) { return new("unavailable", false, null, "en", null); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new("unavailable", false, null, "en", null); }
    }

    public async Task<ValenceArousalCatalog> CandidatesAsync(CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("ValenceArousalInference").GetAsync("models/va", ct);
            if (!response.IsSuccessStatusCode) return new([], null);
            var result = await response.Content.ReadFromJsonAsync<VaCatalog>(cancellationToken: ct);
            return new(result?.Items ?? [], result?.ActiveVersion);
        }
        catch (HttpRequestException) { return new([], null); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new([], null); }
    }

    public async Task<ValenceArousalActivation> ActivateModelAsync(string version, CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("ValenceArousalInference")
                .PostAsJsonAsync("models/va/activate", new { version }, ct);
            var result = await response.Content.ReadFromJsonAsync<VaActivation>(cancellationToken: ct);
            return new((int)response.StatusCode, result?.Message ?? result?.Detail, result?.ActiveVersion, result?.Device);
        }
        catch (HttpRequestException) { return new(503, "Valence/arousal inference service is unavailable.", null, null); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(503, "Valence/arousal inference service timed out.", null, null); }
    }

    public async Task<ValenceArousalMoodResult?> AnalyzeAsync(string text, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("AI:Enabled")) return null;

        var client = clients.CreateClient("ValenceArousalInference");
        VaPrediction? prediction;
        try
        {
            using var response = await client.PostAsJsonAsync("predict-va", new { text, language = "en" }, ct);
            if (!response.IsSuccessStatusCode) return null;
            prediction = await response.Content.ReadFromJsonAsync<VaPrediction>(cancellationToken: ct);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (System.Text.Json.JsonException) { return null; }

        if (prediction is null
            || !IsValidModelVersion(prediction.ModelVersion)
            || prediction.Language != "en"
            || !double.IsFinite(prediction.Valence)
            || prediction.Valence is < -1 or > 1
            || !double.IsFinite(prediction.Arousal)
            || prediction.Arousal is < -1 or > 1) return null;

        var moods = await db.TamTrangs.ToListAsync(ct);
        if (moods.Count == 0) return null;
        var rankedMoods = moods
            .Select(item => new
            {
                Mood = item,
                Valence = (double)((item.MinValence + item.MaxValence) / 2m),
                Arousal = (double)((item.MinArousal + item.MaxArousal) / 2m)
            })
            .OrderBy(item => Math.Pow(item.Valence - prediction.Valence, 2)
                + Math.Pow(item.Arousal - prediction.Arousal, 2))
            .ThenBy(item => item.Mood.TamTrangID)
            .ToArray();
        var mood = rankedMoods[0];

        return new ValenceArousalMoodResult(
            prediction.ModelVersion!,
            prediction.Valence,
            prediction.Arousal,
            mood.Mood.TamTrangID,
            mood.Mood.TenTamTrang,
            (mood.Mood.MinValence + mood.Mood.MaxValence) / 2m,
            (mood.Mood.MinArousal + mood.Mood.MaxArousal) / 2m,
            rankedMoods.Take(3).Select(item => new ValenceArousalMoodCandidate(
                item.Mood.TamTrangID, item.Mood.TenTamTrang)).ToArray());
    }

    private sealed class VaPrediction
    {
        [JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
        [JsonPropertyName("language")] public string? Language { get; init; }
        [JsonPropertyName("valence")] public double Valence { get; init; }
        [JsonPropertyName("arousal")] public double Arousal { get; init; }
    }

    private sealed class VaHealth
    {
        [JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
        [JsonPropertyName("language")] public string? Language { get; init; }
        [JsonPropertyName("inference_device")] public string? Device { get; init; }
    }

    private sealed class VaCatalog
    {
        [JsonPropertyName("items")] public List<ValenceArousalCandidate> Items { get; init; } = [];
        [JsonPropertyName("active_version")] public string? ActiveVersion { get; init; }
    }

    private sealed class VaActivation
    {
        [JsonPropertyName("message")] public string? Message { get; init; }
        [JsonPropertyName("detail")] public string? Detail { get; init; }
        [JsonPropertyName("active_version")] public string? ActiveVersion { get; init; }
        [JsonPropertyName("device")] public string? Device { get; init; }
    }

    private static bool IsValidModelVersion(string? version) =>
        version is { Length: >= 3 and <= 80 }
        && char.IsAsciiLetterOrDigit(version[0])
        && version.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
