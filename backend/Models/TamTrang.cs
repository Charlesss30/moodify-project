using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("tam_trang")]
    [BsonIgnoreExtraElements]
    public class TamTrang
    {
        [BsonElement("updatedAt")] public DateTime? UpdatedAt { get; set; }
        [BsonExtraElements, System.Text.Json.Serialization.JsonIgnore]
        public BsonDocument ExtraElements { get; set; } = new();

        [Key, BsonId]
        [Column("tamtrangid")]
        public int TamTrangID { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("tentamtrang")]
        [BsonElement("name")] public string TenTamTrang { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore] [BsonElement("nameNormalized")] public string TenTamTrangNormalized { get; set; } = string.Empty;

        [Column("minvalence", TypeName = "decimal(5,4)")]
        [BsonElement("minValence")] public decimal MinValence { get; set; }

        [Column("maxvalence", TypeName = "decimal(5,4)")]
        [BsonElement("maxValence")] public decimal MaxValence { get; set; }

        [Column("minarousal", TypeName = "decimal(5,4)")]
        [BsonElement("minArousal")] public decimal MinArousal { get; set; }

        [Column("maxarousal", TypeName = "decimal(5,4)")]
        [BsonElement("maxArousal")] public decimal MaxArousal { get; set; }

        [BsonIgnore]
        public ICollection<LichSuTamTrang> LichSuTamTrangs { get; set; } = new List<LichSuTamTrang>();
    }
}
