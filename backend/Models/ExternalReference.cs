using MongoDB.Bson.Serialization.Attributes;
namespace mood_recommendation.Models;

// The source is encoded in the ID; no provider metadata or media is persisted.
public sealed class ExternalReference
{
    [BsonId] public string Id { get; set; } = "";
}
