using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("lich_su_tam_trang")]
    [BsonIgnoreExtraElements]
    public class LichSuTamTrang
    {
        [BsonExtraElements, System.Text.Json.Serialization.JsonIgnore]
        public BsonDocument ExtraElements { get; set; } = new();

        [BsonElement("recommendationId")] public string? RecommendationId { get; set; }
        [BsonElement("recommendedItems")] public List<RecommendationSnapshot>? RecommendedItems { get; set; }
        [BsonElement("algorithm")] public string? Algorithm { get; set; }
        [BsonElement("source")] public string? Source { get; set; }
        [BsonElement("analysisId")] public string? AnalysisId { get; set; }
        [BsonElement("analyzerVersion")] public string? AnalyzerVersion { get; set; }
        [BsonElement("mappingVersion")] public string? MappingVersion { get; set; }
        [BsonElement("analysisSource")] public string? AnalysisSource { get; set; }
        [BsonElement("decisionSource")] public string? DecisionSource { get; set; }
        [BsonElement("moodConfigHash")] public string? MoodConfigHash { get; set; }
        [BsonElement("analysisExpiresAt")] public DateTime? AnalysisExpiresAt { get; set; }
        [BsonElement("recommendationRequestHash")] public string? RecommendationRequestHash { get; set; }
        [BsonElement("recommendationResponseJson")] public string? RecommendationResponseJson { get; set; }

        [Key, BsonId]
        [Column("lichsuid")]
        public long LichSuID { get; set; }

        [Required]
        [Column("taikhoanid")]
        [BsonElement("accountId")] public int TaiKhoanID { get; set; }

        [Required]
        [Column("tamtrangid")]
        [BsonElement("moodId")] public int TamTrangID { get; set; }

        [Column("thoigian")]
        [BsonElement("createdAt")] public DateTime ThoiGian { get; set; }

        [Column("vanbandauvao")]
        [BsonElement("inputText")] public string? VanBanDauVao { get; set; }

        [Column("valence", TypeName = "decimal(6,4)")]
        [BsonElement("valence")] public decimal Valence { get; set; }

        [Column("arousal", TypeName = "decimal(6,4)")]
        [BsonElement("arousal")] public decimal Arousal { get; set; }

        [ForeignKey("TaiKhoanID")]
        [BsonIgnore]
        public TaiKhoan? TaiKhoan { get; set; }

        [ForeignKey("TamTrangID")]
        [BsonIgnore]
        public TamTrang? TamTrang { get; set; }
    }
}

namespace mood_recommendation.Models {
    public sealed class RecommendationSnapshot {
        [BsonElement("contentId")] public string NoiDungID { get; set; } = "";
        [BsonElement("title")] public string TieuDe { get; set; } = "";
        [BsonElement("contentType")] public string LoaiNoiDung { get; set; } = "";
        [BsonElement("imageUrl")] public string? HinhAnh { get; set; }
    }
}
