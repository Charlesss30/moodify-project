using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("theloai")]
    [BsonIgnoreExtraElements]
    public class TheLoai
    {
        [BsonElement("updatedAt")] public DateTime? UpdatedAt { get; set; }
        [BsonExtraElements, System.Text.Json.Serialization.JsonIgnore]
        public BsonDocument ExtraElements { get; set; } = new();

        [BsonElement("contentType")] public string ContentType { get; set; } = "Both";
        [Range(-1,1), BsonElement("defaultValence")] public decimal? DefaultValence { get; set; }
        [Range(-1,1), BsonElement("defaultArousal")] public decimal? DefaultArousal { get; set; }
        [BsonElement("moodVersion")] public int MoodVersion { get; set; } = 1;

        [Key, BsonId]
        [Column("theloaiid")]
        public string TheLoaiID { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        [Column("tentheloai")]
        [BsonElement("name")] public string TenTheLoai { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore] [BsonElement("nameNormalized")] public string TenTheLoaiNormalized { get; set; } = string.Empty;
    }
}