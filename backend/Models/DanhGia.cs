using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("danh_gia")]
    [BsonIgnoreExtraElements]
    public class DanhGia
    {
        [BsonExtraElements, System.Text.Json.Serialization.JsonIgnore]
        public BsonDocument ExtraElements { get; set; } = new();

        [Key, BsonId]
        [Column("danhgiaid")]
        public long DanhGiaID { get; set; }

        [Required]
        [Column("taikhoanid")]
        [BsonElement("accountId")] public int TaiKhoanID { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("noidungid")]
        [BsonElement("contentId")] public string NoiDungID { get; set; } = string.Empty;

        [Required]
        [Range(1, 5)]
        [Column("sosao")]
        [BsonElement("stars")] public int SoSao { get; set; }

        [Column("nhanxet")]
        [BsonElement("comment")] public string? NhanXet { get; set; }

        [Column("thoigian")]
        [BsonElement("createdAt")] public DateTime ThoiGian { get; set; }

        [ForeignKey("TaiKhoanID")]
        [BsonIgnore]
        public TaiKhoan? TaiKhoan { get; set; }

        [ForeignKey("NoiDungID")]
        [BsonIgnore]
        public NoiDungGiaiTri? NoiDungGiaiTri { get; set; }
    }
}
