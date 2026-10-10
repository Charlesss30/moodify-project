using MongoDB.Bson;
using MongoDB.Driver;
using mood_recommendation.Data;

namespace mood_recommendation.Services;

public sealed record RegistryOperation(int StatusCode, object Body);

public sealed class AiModelRegistryService
{
    private readonly IMongoCollection<BsonDocument> _models;
    private readonly IMongoCollection<BsonDocument> _state;
    private readonly IMongoCollection<BsonDocument> _audit;
    private readonly MoodAnalysisService _analysis;
    private readonly IConfiguration _configuration;

    public AiModelRegistryService(MongoStore db, MoodAnalysisService analysis, IConfiguration configuration)
    {
        _models = db.Database.GetCollection<BsonDocument>("aiModels");
        _state = db.Database.GetCollection<BsonDocument>("aiModelState");
        _audit = db.Database.GetCollection<BsonDocument>("aiAudit");
        _analysis = analysis;
        _configuration = configuration;
    }

    public async Task<object> StatusAsync(CancellationToken cancellationToken)
    {
        var runtime = await _analysis.GetRuntimeSnapshotAsync(cancellationToken);
        var state = await GetStateAsync(cancellationToken);
        var registered = await _models.Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync(cancellationToken);
        var items = ConfiguredCandidates().Select(candidate =>
        {
            var entry = registered.FirstOrDefault(x => x["_id"] == candidate.Version);
            return new
            {
                modelVersion = candidate.Version,
                candidate.DisplayName,
                candidate.Task,
                candidate.Language,
                artifactHash = candidate.ArtifactHash,
                configHash = entry?.GetValue("configHash", BsonNull.Value).IsString == true
                    ? entry["configHash"].AsString
                    : null,
                validationStatus = entry?.GetValue("validationStatus", "not_validated").AsString
                    ?? "not_validated",
                validatedAt = entry?.GetValue("validatedAt", BsonNull.Value).ToNullableUniversalTime(),
                isActive = state.ActiveVersion == candidate.Version,
                runtimeStatus = runtime.Ready && runtime.ModelVersion == candidate.Version
                    ? "Ready"
                    : "Unavailable"
            };
        }).ToArray();
        return new
        {
            items,
            activeVersion = state.ActiveVersion,
            previousVersion = state.PreviousVersion,
            revision = state.Revision,
            runtime = new
            {
                modelVersion = runtime.ModelVersion,
                runtimeStatus = runtime.Ready ? "Ready" : "Unavailable",
                enabled = runtime.Enabled,
                language = runtime.Language,
                artifactHash = runtime.ArtifactHash,
                configHash = runtime.ConfigHash,
                inferenceDevice = runtime.InferenceDevice,
                deviceName = runtime.DeviceName,
                mappingVersion = runtime.MappingVersion
            }
        };
    }

    public async Task<RegistryOperation> ValidateAsync(
        string version, int actorId, string actorRole, CancellationToken cancellationToken)
    {
        var candidate = FindCandidate(version);
        if (candidate is null)
            return await RejectAsync(404, "validate", version, actorId, actorRole,
                "Model version is not in the server-side allowlist.", "rejected", cancellationToken);

        var runtime = await _analysis.GetRuntimeSnapshotAsync(cancellationToken);
        if (!runtime.Ready
            || runtime.ModelVersion != candidate.Version
            || !string.Equals(runtime.ArtifactHash, candidate.ArtifactHash, StringComparison.OrdinalIgnoreCase)
            || !IsSha256(runtime.ConfigHash))
            return await RejectAsync(409, "validate", version, actorId, actorRole,
                "Runtime must be ready with the allowlisted model and verified artifact before validation.",
                "rejected", cancellationToken);

        var smoke = await _analysis.AnalyzeAsync(
            "I feel joyful and calm.", "en", actorId, false, cancellationToken);
        if (smoke.Source != "ai"
            || smoke.Status is "unavailable" or "unsupported_language"
            || smoke.ModelVersion != candidate.Version
            || smoke.Candidates.Count != 5)
            return await RejectAsync(409, "validate", version, actorId, actorRole,
                "The live inference smoke test did not return a valid five-candidate response.",
                "rejected", cancellationToken);

        var entry = new BsonDocument
        {
            ["_id"] = candidate.Version,
            ["displayName"] = candidate.DisplayName,
            ["task"] = candidate.Task,
            ["language"] = candidate.Language,
            ["artifactHash"] = candidate.ArtifactHash.ToLowerInvariant(),
            ["configHash"] = runtime.ConfigHash!.ToLowerInvariant(),
            ["validationStatus"] = "validated",
            ["validatedAt"] = DateTime.UtcNow,
            ["validatedBy"] = actorId
        };
        await _models.ReplaceOneAsync(
            x => x["_id"] == candidate.Version, entry, new ReplaceOptions { IsUpsert = true },
            cancellationToken);
        await WriteAuditAsync(actorId, actorRole, "validate", version, "succeeded",
            "Artifact/config hashes, runtime readiness and a live five-candidate inference smoke test verified.", cancellationToken);
        return new RegistryOperation(200, new { validationStatus = "validated", version, configHash = runtime.ConfigHash });
    }

    public async Task<RegistryOperation> ActivateAsync(
        string version, long expectedRevision, int actorId, string actorRole, CancellationToken cancellationToken)
    {
        var candidate = FindCandidate(version);
        var validated = await _models.Find(x => x["_id"] == version).FirstOrDefaultAsync(cancellationToken);
        var runtime = await _analysis.GetRuntimeSnapshotAsync(cancellationToken);
        if (candidate is null
            || validated is null
            || validated.GetValue("validationStatus", "").AsString != "validated"
            || !runtime.Ready
            || runtime.ModelVersion != version
            || !string.Equals(runtime.ArtifactHash, candidate.ArtifactHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(runtime.ConfigHash, validated.GetValue("configHash", "").AsString, StringComparison.OrdinalIgnoreCase))
            return await RejectAsync(409, "activate", version, actorId, actorRole,
                "Activation requires a previously validated candidate that is currently ready with matching hashes.",
                "rejected", cancellationToken);

        var current = await GetStateAsync(cancellationToken, initialize: true);
        var updated = await CompareAndSetStateAsync(current, expectedRevision, version, actorId, cancellationToken);
        if (!updated)
            return await RejectAsync(409, "activate", version, actorId, actorRole,
                "Registry revision changed. Reload model status and retry.", "conflict", cancellationToken);
        await WriteAuditAsync(actorId, actorRole, "activate", version, "succeeded",
            $"Active model set at registry revision {expectedRevision + 1}.", cancellationToken);
        return new RegistryOperation(200, new { activeVersion = version, revision = expectedRevision + 1 });
    }

    public async Task<RegistryOperation> DeactivateAsync(
        long expectedRevision, int actorId, string actorRole, CancellationToken cancellationToken)
    {
        var current = await GetStateAsync(cancellationToken, initialize: true);
        if (current.ActiveVersion is null)
            return await RejectAsync(409, "deactivate", null, actorId, actorRole,
                "There is no active model to deactivate.", "rejected", cancellationToken);
        if (!await CompareAndSetStateAsync(current, expectedRevision, null, actorId, cancellationToken))
            return await RejectAsync(409, "deactivate", current.ActiveVersion, actorId, actorRole,
                "Registry revision changed. Reload model status and retry.", "conflict", cancellationToken);
        await WriteAuditAsync(actorId, actorRole, "deactivate", current.ActiveVersion, "succeeded",
            $"Model deactivated at registry revision {expectedRevision + 1}.", cancellationToken);
        return new RegistryOperation(200, new { activeVersion = (string?)null, revision = expectedRevision + 1 });
    }

    public async Task<RegistryOperation> RollbackAsync(
        long expectedRevision, int actorId, string actorRole, CancellationToken cancellationToken)
    {
        var current = await GetStateAsync(cancellationToken, initialize: true);
        if (current.PreviousVersion is not { } previous)
            return await RejectAsync(409, "rollback", null, actorId, actorRole,
                "No previously active model is available for rollback.", "rejected", cancellationToken);
        var validated = await _models.Find(x => x["_id"] == previous).FirstOrDefaultAsync(cancellationToken);
        var runtime = await _analysis.GetRuntimeSnapshotAsync(cancellationToken);
        if (validated is null
            || validated.GetValue("validationStatus", "").AsString != "validated"
            || !runtime.Ready
            || runtime.ModelVersion != previous
            || !string.Equals(runtime.ArtifactHash, validated.GetValue("artifactHash", "").AsString, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(runtime.ConfigHash, validated.GetValue("configHash", "").AsString, StringComparison.OrdinalIgnoreCase))
            return await RejectAsync(409, "rollback", previous, actorId, actorRole,
                "Previous model is not validated and ready with matching hashes.", "rejected", cancellationToken);
        if (!await CompareAndSetStateAsync(current, expectedRevision, previous, actorId, cancellationToken))
            return await RejectAsync(409, "rollback", previous, actorId, actorRole,
                "Registry revision changed. Reload model status and retry.", "conflict", cancellationToken);
        await WriteAuditAsync(actorId, actorRole, "rollback", previous, "succeeded",
            $"Rolled back at registry revision {expectedRevision + 1}.", cancellationToken);
        return new RegistryOperation(200, new { activeVersion = previous, revision = expectedRevision + 1 });
    }

    public async Task<IReadOnlyList<object>> AuditAsync(CancellationToken cancellationToken)
    {
        var events = await _audit.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(new BsonDocument("createdAt", -1)).Limit(100).ToListAsync(cancellationToken);
        return events.Select(item => (object)new
        {
            eventId = item["_id"].AsString,
            actorId = item["actorId"].ToInt32(),
            actorRole = item["actorRole"].AsString,
            action = item["action"].AsString,
            modelVersion = item.GetValue("modelVersion", BsonNull.Value).IsString
                ? item["modelVersion"].AsString
                : null,
            outcome = item["outcome"].AsString,
            detail = item["detail"].AsString,
            createdAt = item["createdAt"].ToUniversalTime()
        }).ToArray();
    }

    private async Task<bool> CompareAndSetStateAsync(
        RegistryState current, long expectedRevision, string? newActive, int actorId,
        CancellationToken cancellationToken)
    {
        if (current.Revision != expectedRevision) return false;
        var nextRevision = expectedRevision + 1;
        BsonValue activeValue = newActive is null ? BsonNull.Value : new BsonString(newActive);
        BsonValue previousValue = current.ActiveVersion is null
            ? current.PreviousVersion is null ? BsonNull.Value : new BsonString(current.PreviousVersion)
            : new BsonString(current.ActiveVersion);
        var updates = Builders<BsonDocument>.Update;
        var update = updates.Combine(
            updates.Set<BsonValue>("activeVersion", activeValue),
            updates.Set<BsonValue>("previousVersion", previousValue),
            updates.Set("revision", nextRevision),
            updates.Set("updatedAt", DateTime.UtcNow),
            updates.Set("updatedBy", actorId));
        var result = await _state.UpdateOneAsync(
            x => x["_id"] == "active" && x["revision"] == expectedRevision,
            update, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    private async Task<RegistryState> GetStateAsync(
        CancellationToken cancellationToken, bool initialize = false)
    {
        var state = await _state.Find(x => x["_id"] == "active").FirstOrDefaultAsync(cancellationToken);
        if (state is null && initialize)
        {
            try
            {
                await _state.InsertOneAsync(new BsonDocument
                {
                    ["_id"] = "active",
                    ["activeVersion"] = BsonNull.Value,
                    ["previousVersion"] = BsonNull.Value,
                    ["revision"] = 0L,
                    ["updatedAt"] = DateTime.UtcNow,
                    ["updatedBy"] = 0
                }, cancellationToken: cancellationToken);
            }
            catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Another registry operation initialized the singleton state concurrently.
            }
            state = await _state.Find(x => x["_id"] == "active").FirstAsync(cancellationToken);
        }
        if (state is null) return new RegistryState(null, null, 0);
        return new RegistryState(
            ReadNullableString(state, "activeVersion"),
            ReadNullableString(state, "previousVersion"),
            state.GetValue("revision", 0).ToInt64());
    }

    private async Task<RegistryOperation> RejectAsync(
        int status, string action, string? version, int actorId, string actorRole, string detail,
        string outcome, CancellationToken cancellationToken)
    {
        await WriteAuditAsync(actorId, actorRole, action, version, outcome, detail, cancellationToken);
        return new RegistryOperation(status, new { message = detail });
    }

    private Task WriteAuditAsync(
        int actorId, string actorRole, string action, string? version, string outcome,
        string detail, CancellationToken cancellationToken) =>
        _audit.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Guid.NewGuid().ToString("N"),
            ["actorId"] = actorId,
            ["actorRole"] = actorRole,
            ["action"] = action,
            ["modelVersion"] = version is null ? BsonNull.Value : new BsonString(version),
            ["outcome"] = outcome,
            ["detail"] = detail,
            ["createdAt"] = DateTime.UtcNow
        }, cancellationToken: cancellationToken);

    private ModelCandidate? FindCandidate(string version) =>
        ConfiguredCandidates().FirstOrDefault(x => x.Version == version);

    private IEnumerable<ModelCandidate> ConfiguredCandidates() =>
        _configuration.GetSection("AI:Registry:Candidates").GetChildren().Select(item =>
            new ModelCandidate(
                item["Version"] ?? "",
                item["DisplayName"] ?? "",
                item["Task"] ?? "",
                item["Language"] ?? "",
                item["ArtifactHash"] ?? ""))
            .Where(item => item.Version.Length > 0 && item.DisplayName.Length > 0);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string? ReadNullableString(BsonDocument source, string key) =>
        source.TryGetValue(key, out var value) && value.IsString ? value.AsString : null;

    private sealed record ModelCandidate(string Version, string DisplayName, string Task, string Language, string ArtifactHash);
    private sealed record RegistryState(string? ActiveVersion, string? PreviousVersion, long Revision);
}
