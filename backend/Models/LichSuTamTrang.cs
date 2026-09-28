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
