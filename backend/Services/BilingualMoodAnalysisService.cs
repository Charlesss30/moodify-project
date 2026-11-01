using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

public sealed record BilingualMood(int TamTrangID, string TenTamTrang, decimal Valence, decimal Arousal);
public sealed record BilingualAnalysisResponse(
    string Status, string Source, string? Language, string? ModelType, string? ModelVersion,
    string? MappingVersion, string RoutingVersion, string ScoreType, bool Accepted,
    bool RequiresConfirmation, string? AnalysisId, DateTime? ExpiresAt,
    double? Valence, double? Arousal, BilingualMood? Mood,
    IReadOnlyList<ValenceArousalMoodCandidate> MoodCandidates,
    IReadOnlyList<VietnameseEmotionCandidate> EmotionCandidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Emotion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Confidence,
    string? ReasonCode, string Message,
    string? MappingMethod = null, double? RecommendationValence = null, double? RecommendationArousal = null,
    string InputScope = "full_text", int? AnalyzedCharacterCount = null);

/// <summary>One public VI/EN contract; only accepted, validated results receive a server receipt.</summary>
public sealed class BilingualMoodAnalysisService(
    IHttpClientFactory clients, MongoStore db, IConfiguration config, TimeProvider clock, BilingualPolicyService policies)
{
    public const string ReceiptCollection = "aiVaAnalyses";
    public static string PolicyVersion(string language) => language == "vi"
        ? "vsmec-anchors-v1" : "regression";
    public static string MappingVersion(string policyVersion, string method) =>
        policyVersion + "|" + SemanticMoodMapper.Version + "|" + method;
    private static bool CurrentMapping(string value, string policyVersion) =>
        SemanticMoodMapper.Methods.Any(method => value == MappingVersion(policyVersion, method));
    private static readonly HashSet<string> Labels = new(
        ["Anger", "Disgust", "Enjoyment", "Fear", "Other", "Sadness", "Surprise"]);

    public async Task<BilingualAnalysisResponse> AnalyzeAsync(string text, string requestedLanguage, int userId, CancellationToken ct)
    {
        var detectedLanguage = MoodLanguageRouter.Detect(text);
        var language = requestedLanguage == "auto" ? detectedLanguage : requestedLanguage;
        if (detectedLanguage is null || language is not ("vi" or "en"))
            return Failure("unsupported_language", null, "unsupported_language", "Chỉ hỗ trợ mô tả tiếng Việt hoặc tiếng Anh.");
        if (requestedLanguage != "auto" && language != detectedLanguage)
            return Failure("unsupported_language", language, "language_mismatch", "Ngôn ngữ mô tả không khớp model yêu cầu. Hãy dùng nhận diện tự động Việt/Anh.");
        var trimmed=text.Trim();
        if((!trimmed.Any(char.IsLetter)&&!trimmed.EnumerateRunes().Any(r=>System.Text.Rune.GetUnicodeCategory(r)==System.Globalization.UnicodeCategory.OtherSymbol))
            ||Uri.TryCreate(trimmed,UriKind.Absolute,out var link)&&link.Scheme is "http" or "https"&&!trimmed.Any(char.IsWhiteSpace))
            return Failure("unknown",language,"insufficient_text","Hãy viết cảm xúc của bạn bằng tiếng Việt hoặc tiếng Anh; số, dấu câu hoặc liên kết chưa đủ để phân tích.");
        if (!config.GetValue<bool>("AI:Enabled"))
            return Failure("unavailable", language, "disabled", "AI đang tắt. Bạn vẫn có thể chọn tâm trạng thủ công.");

        var modelInput = SemanticMoodMapper.PrepareModelInput(text, language);
        Prediction? prediction;
        try
        {
            using var response = await Client(language).PostAsJsonAsync(
                language == "vi" ? "predict-vi" : "predict-va", new { text = modelInput.Text, language }, ct);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                return Failure("unavailable", language, response.StatusCode == HttpStatusCode.TooManyRequests ? "busy" : "runtime_unavailable",
                    "Model đang bận hoặc chưa sẵn sàng. Hãy thử lại hoặc chọn tâm trạng thủ công.");
            if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
                return Failure("unknown", language, "unsupported_text", "Model chưa xử lý được mô tả này. Hãy diễn đạt lại hoặc chọn tâm trạng.");
            if (!response.IsSuccessStatusCode)
                return Failure("unavailable", language, "runtime_error", "Dịch vụ AI chưa sẵn sàng.");
            prediction = await response.Content.ReadFromJsonAsync<Prediction>(cancellationToken: ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Failure("unavailable", language, "timeout", "Phân tích quá thời gian chờ. Hãy thử lại hoặc chọn tâm trạng."); }
        catch (HttpRequestException)
        { return Failure("unavailable", language, "connection_error", "Không kết nối được model. Hãy chọn tâm trạng hoặc thử lại."); }
        catch (JsonException)
        { return Failure("unavailable", language, "invalid_response", "Model trả kết quả không hợp lệ. Hãy thử lại."); }

        if (!Valid(prediction, language))
            return Failure("unavailable", language, "invalid_response", "Model trả kết quả ngoài hợp đồng được hỗ trợ.");

        var p = prediction!;
        var moods = await db.TamTrangs.ToListAsync(ct);
        if (moods.Count == 0 || moods.Any(m => m.MinValence < -1 || m.MaxValence > 1 || m.MinValence > m.MaxValence
            || m.MinArousal < -1 || m.MaxArousal > 1 || m.MinArousal > m.MaxArousal))
            return Failure("unavailable", language, "invalid_mood_configuration", "Danh mục tâm trạng chưa sẵn sàng.");

        var policy=await policies.ActiveAsync(language,ct);
        var eligible=moods.Where(m=>policy.MoodIds is null||policy.MoodIds.Contains(m.TamTrangID)).ToList();
        if(eligible.Count==0 || policy.MoodIds?.Any(id=>!moods.Any(m=>m.TamTrangID==id))==true)
            return Failure("unavailable",language,"invalid_mood_configuration","Policy references a mood that was removed. Ask the AI engineer to review.");
        var modelAccepted = language == "en" || (p.Accepted == true && p.Confidence>=policy.MinimumConfidence);
        var decision = SemanticMoodMapper.Decide(text, language, p.Emotion, p.Valence!.Value, p.Arousal!.Value, eligible);
        var accepted = modelAccepted && decision.Mood is not null;
        var mood = decision.Mood;
        var mappingVersion = MappingVersion(policy.Version, decision.Method);
        var target = accepted ? SemanticMoodMapper.Project((decimal)p.Valence.Value, (decimal)p.Arousal.Value, mood!) : ((decimal,decimal)?)null;
        var reason = decision.Mood is null ? decision.ReasonCode : modelAccepted ? null : "low_confidence";
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(5);
        var id = accepted ? "va_" + Guid.NewGuid().ToString("N") : null;
        if (id is not null)
            await Receipts.InsertOneAsync(new VaAnalysisReceipt {
                Id = id, UserId = userId, Language = language, ModelType = ModelType(language), ModelVersion = p.ModelVersion!,
                MappingVersion = mappingVersion, RoutingVersion = MoodLanguageRouter.Version,
                MoodSetHash = MoodSetHash(moods), SuggestedMoodId = mood!.TamTrangID,
                Valence = (decimal)p.Valence!.Value, Arousal = (decimal)p.Arousal!.Value,
                InputScope = modelInput.Scope,
                CreatedAt = now, ExpiresAt = expires, Accepted = true
            }, cancellationToken: ct);

        return new(accepted ? "matched" : "unknown", "ai_va", language, ModelType(language), p.ModelVersion,
            mappingVersion, MoodLanguageRouter.Version, ScoreType(language), accepted, true,
            id, accepted ? expires : null, p.Valence, p.Arousal,
            accepted ? new(mood!.TamTrangID, mood.TenTamTrang, (mood.MinValence + mood.MaxValence) / 2m,
                (mood.MinArousal + mood.MaxArousal) / 2m) : null,
            accepted ? [new ValenceArousalMoodCandidate(mood!.TamTrangID, mood.TenTamTrang)] : [],
            language == "vi" ? p.EmotionCandidates! : [], language == "vi" ? p.Emotion : null,
            language == "vi" ? p.Confidence : null, reason,
            accepted ? decision.Method == "explicit_self_description"
                ? "Đã chọn tâm trạng theo cảm xúc bạn mô tả. Hãy xác nhận hoặc chọn lại trước khi nhận gợi ý."
                : decision.Method == "model_negation"
                    ? "Model nhận diện cảm xúc phù hợp với mô tả phủ định của bạn. Hãy xác nhận hoặc chọn lại trước khi nhận gợi ý."
                    : "Đây là tâm trạng gợi ý từ model. Hãy xác nhận hoặc chọn lại trước khi nhận gợi ý."
                : reason switch {
                    "model_context_conflict" => "Model và mô tả trực tiếp chưa thống nhất. Hãy xác nhận cảm xúc hiện tại bằng lựa chọn bên dưới.",
                    "ambiguous_statement" => "Mô tả này có thể được hiểu theo nhiều cách. Hãy nói trực tiếp cảm xúc hiện tại hoặc chọn tâm trạng.",
                    "uncertain_statement" => "Bạn đang mô tả một giả định hoặc chưa rõ cảm xúc. Hãy nói cảm xúc hiện tại hoặc chọn tâm trạng.",
                    "mixed_emotions" => "Bạn đang mô tả nhiều cảm xúc. Hãy cho biết cảm xúc rõ nhất lúc này hoặc chọn tâm trạng.",
                    "negated_emotion" or "negation_model_conflict" => "Model chưa xác định được cảm xúc khác phù hợp với mô tả phủ định. Hãy nói rõ hơn cảm xúc hiện tại hoặc chọn tâm trạng.",
                    "ambiguous_negation" => "Câu có nhiều lớp phủ định. Hãy nói trực tiếp cảm xúc hiện tại để tránh hiểu sai.",
                    "unsupported_emotion" => "Mô tả có lo lắng hoặc giận dữ; các tâm trạng hiện có chưa biểu diễn phù hợp. Hãy chọn tâm trạng bạn muốn nhận gợi ý.",
                    "not_current_self_description" => "Hãy mô tả cảm xúc hiện tại của bạn; mong muốn nội dung, chuyện quá khứ hoặc cảm xúc người khác chưa xác định được tâm trạng lúc này.",
                    "mood_not_available" => "Tâm trạng được mô tả chưa có trong danh mục được phép. Hãy chọn tâm trạng khác.",
                    "low_confidence" => "Model chưa đạt ngưỡng tin cậy. Hãy diễn đạt rõ hơn hoặc chọn tâm trạng thủ công.",
                    _ => "Chưa xác định được tâm trạng phù hợp. Hãy mô tả rõ hơn hoặc chọn tâm trạng."
                }, decision.Method, target is null ? null : (double)target.Value.Item1,
                target is null ? null : (double)target.Value.Item2, modelInput.Scope, modelInput.Text.Length);
    }

    public IMongoCollection<VaAnalysisReceipt> Receipts => db.Database.GetCollection<VaAnalysisReceipt>(ReceiptCollection);
    private HttpClient Client(string language) => clients.CreateClient("MoodInference");

    // Revalidate serving version, entire mood geometry and policy before both first submission and replay.
    public async Task<VaAnalysisLease?> ReserveAsync(string id, int userId, int moodId, string source, CancellationToken ct)
    {
        if (!config.GetValue<bool>("AI:Enabled")) return null;
        var receipt = await Receipts.Find(x => x.Id == id && x.UserId == userId).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var policy = receipt is null ? null : await policies.ActiveAsync(receipt.Language,ct);
        if (receipt is null || !receipt.Accepted || receipt.ExpiresAt <= now
            || receipt.Language is not ("vi" or "en") || policy is null || !CurrentMapping(receipt.MappingVersion, policy.Version)
            || receipt.RoutingVersion != MoodLanguageRouter.Version
            || (source == "ai_va" && receipt.SuggestedMoodId != moodId) || source is not ("manual" or "ai_va"))
            return null;
        var moods = await db.TamTrangs.ToListAsync(ct);
        if (!moods.Any(x => x.TamTrangID == moodId) || receipt.MoodSetHash != MoodSetHash(moods)) return null;
        try
        {
            using var health = await Client(receipt.Language).GetAsync(
                receipt.Language == "vi" ? "health/vi/ready" : "health/va/ready", ct);
            if (!health.IsSuccessStatusCode) return null;
            var active = await health.Content.ReadFromJsonAsync<Health>(cancellationToken: ct);
            if (active?.Status != "ready" || active.Language != receipt.Language || active.ModelVersion != receipt.ModelVersion)
                return null;
        }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (JsonException) { return null; }

        var leaseId = Guid.NewGuid().ToString("N");
        var filter = Builders<VaAnalysisReceipt>.Filter;
        var reserved = await Receipts.FindOneAndUpdateAsync(
            filter.Eq(x => x.Id, id) & filter.Eq(x => x.UserId, userId)
                & filter.Gt(x => x.ExpiresAt, clock.GetUtcNow().UtcDateTime)
                & (filter.Eq(x => x.LeaseUntil, null) | filter.Lte(x => x.LeaseUntil, now)),
            Builders<VaAnalysisReceipt>.Update.Set(x => x.LeaseId, leaseId).Set(x => x.LeaseUntil, now.AddMinutes(2)),
            new FindOneAndUpdateOptions<VaAnalysisReceipt> { ReturnDocument = ReturnDocument.After }, ct);
        return reserved is null ? null : new VaAnalysisLease(Receipts, reserved, leaseId);
    }

    public static string MoodSetHash(IEnumerable<TamTrang> moods) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("\n", moods.OrderBy(x => x.TamTrangID).Select(MoodAnalysisService.MoodConfigurationHash)))));
    private static string ModelType(string language) => language == "vi" ? "emotion_classification_va_adapter" : "valence_arousal_regression";
    private static string ScoreType(string language) => language == "vi" ? "softmax_not_accuracy" : "regression_coordinates";
    private static BilingualAnalysisResponse Failure(string status, string? language, string reason, string message) =>
        new(status, "ai_va", language, language is null ? null : ModelType(language), null,
            language is null ? null : PolicyVersion(language), MoodLanguageRouter.Version,
            language is null ? "none" : ScoreType(language), false, true, null, null, null, null, null, [], [], null, null, reason, message);
    private static bool VersionValid(string? value) => value is { Length: >= 3 and <= 80 }
        && char.IsAsciiLetterOrDigit(value[0]) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    private static bool Coordinate(double? value) => value.HasValue && double.IsFinite(value.Value) && value is >= -1 and <= 1;
    private static bool Valid(Prediction? p, string language)
    {
        if (p is null || !VersionValid(p.ModelVersion) || p.Language != language || !Coordinate(p.Valence) || !Coordinate(p.Arousal)) return false;
        if (language == "en") return true;
        if (!Labels.Contains(p.Emotion ?? "") || p.Accepted is null || p.Confidence is not (>= 0 and <= 1)
            || p.EmotionCandidates is not { Count: >= 1 and <= 3 } candidates
            || candidates.Any(c => c is null || !Labels.Contains(c.Label) || !double.IsFinite(c.Score) || c.Score is < 0 or > 1)
            || candidates.Select(c => c.Label).Distinct().Count() != candidates.Count
            || candidates[0].Label != p.Emotion || candidates[0].Score != p.Confidence) return false;
        return candidates.Zip(candidates.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score);
    }

    private sealed class Prediction
    {
        [JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
        [JsonPropertyName("language")] public string? Language { get; init; }
        [JsonPropertyName("valence")] public double? Valence { get; init; }
        [JsonPropertyName("arousal")] public double? Arousal { get; init; }
        [JsonPropertyName("emotion")] public string? Emotion { get; init; }
        [JsonPropertyName("confidence")] public double? Confidence { get; init; }
        [JsonPropertyName("accepted")] public bool? Accepted { get; init; }
        [JsonPropertyName("emotion_candidates")] public List<VietnameseEmotionCandidate>? EmotionCandidates { get; init; }
    }
    private sealed class Health
    {
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
        [JsonPropertyName("language")] public string? Language { get; init; }
    }
}
