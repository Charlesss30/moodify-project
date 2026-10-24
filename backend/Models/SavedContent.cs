using MongoDB.Bson.Serialization.Attributes;

namespace mood_recommendation.Models;

public sealed class SavedContent
{
    [BsonId] public string Id { get; set; } = "";
    [BsonElement("accountId")] public int AccountId { get; set; }
    [BsonElement("contentId")] public string ContentId { get; set; } = "";
    [BsonElement("title")] public string Title { get; set; } = "";
    [BsonElement("imageUrl")] public string? ImageUrl { get; set; }
    [BsonElement("contentType")] public string ContentType { get; set; } = "";
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
}
