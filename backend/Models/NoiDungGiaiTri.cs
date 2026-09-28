using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("noi_dung_giai_tri")]
    [BsonIgnoreExtraElements]
    public class NoiDungGiaiTri
    {
        [BsonElement("updatedAt")] public DateTime? UpdatedAt { get; set; }
        [BsonExtraElements, System.Text.Json.Serialization.JsonIgnore]
        public BsonDocument ExtraElements { get; set; } = new();

        [BsonElement("reviewStatus")] public string ReviewStatus { get; set; } = "pending";
        [BsonElement("moodRuleVersion")] public string? MoodRuleVersion { get; set; }
        [BsonElement("modelVersion")] public string? ModelVersion { get; set; }

        [BsonIgnore] public bool IsExternalReference { get; set; }
        [BsonIgnore] public bool MoodAvailable { get; set; } = true;
        [BsonIgnore] public string? MetadataStatus { get; set; }
        [Key, BsonId]
        [MaxLength(100)]
        [Column("noidungid")]
        public string NoiDungID { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore] [BsonElement("deleted")] public bool Deleted { get; set; }

        [Required]
        [MaxLength(255)]
        [Column("tieude")]
        [BsonElement("title")] public string TieuDe { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        [Column("loainoidung")]
        [BsonElement("contentType")] public string LoaiNoiDung { get; set; } = string.Empty;

        [Column("valence", TypeName = "decimal(6,4)")]
        [BsonElement("valence")] public decimal Valence { get; set; }

        [Column("arousal", TypeName = "decimal(6,4)")]
        [BsonElement("arousal")] public decimal Arousal { get; set; }

        [Column("hinhanh")]
        [BsonElement("imageUrl")] public string? HinhAnh { get; set; }

        [Column("mota")]
        [BsonElement("description")] public string? MoTa { get; set; }
        [BsonIgnore]
        public Phim? Phim { get; set; }
        [BsonIgnore]
        public Nhac? Nhac { get; set; }
        [BsonIgnore]
        public ICollection<DanhGia> DanhGias { get; set; } = new List<DanhGia>();
    }
}
