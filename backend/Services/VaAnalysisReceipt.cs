using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace mood_recommendation.Services;

// Five-minute retention; never contains the user's raw mood text or credentials.
[BsonIgnoreExtraElements]
public sealed class VaAnalysisReceipt
{
    [BsonId] public string Id { get; set; } = "";
    [BsonElement("accountId")] public int UserId { get; set; }
    [BsonElement("language")] public string Language { get; set; } = "";
    [BsonElement("modelType")] public string ModelType { get; set; } = "";
    [BsonElement("modelVersion")] public string ModelVersion { get; set; } = "";
    [BsonElement("mappingVersion")] public string MappingVersion { get; set; } = "";
    [BsonElement("routingVersion")] public string RoutingVersion { get; set; } = "";
    [BsonElement("moodSetHash")] public string MoodSetHash { get; set; } = "";
    [BsonElement("suggestedMoodId")] public int SuggestedMoodId { get; set; }
    [BsonElement("valence")] public decimal Valence { get; set; }
    [BsonElement("arousal")] public decimal Arousal { get; set; }
    [BsonElement("inputScope")] public string InputScope { get; set; } = "full_text";
    [BsonElement("accepted")] public bool Accepted { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
    [BsonElement("expiresAt")] public DateTime ExpiresAt { get; set; }
    [BsonElement("leaseId")] public string? LeaseId { get; set; }
    [BsonElement("leaseUntil")] public DateTime? LeaseUntil { get; set; }
}

public sealed class VaAnalysisLease(IMongoCollection<VaAnalysisReceipt> collection, VaAnalysisReceipt receipt, string leaseId) : IAsyncDisposable
{
    public VaAnalysisReceipt Receipt { get; } = receipt;

    public async ValueTask DisposeAsync()
    {
        await collection.UpdateOneAsync(x => x.Id == Receipt.Id && x.LeaseId == leaseId,
            Builders<VaAnalysisReceipt>.Update.Set(x => x.LeaseId, null).Set(x => x.LeaseUntil, null));
    }
}
