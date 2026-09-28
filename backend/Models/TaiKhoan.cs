using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("tai_khoan")]
    [BsonIgnoreExtraElements]
    public class TaiKhoan
    {
        [BsonElement("updatedAt")] public DateTime? UpdatedAt { get; set; }
        [BsonExtraElements, System.Text.Json.Serialization.JsonIgnore]
        public BsonDocument ExtraElements { get; set; } = new();

        [Key, BsonId]
        [Column("taikhoanid")]
        public int TaiKhoanID { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("tendangnhap")]
        [BsonElement("username")] public string TenDangNhap { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        [Column("email")]
        [BsonElement("email")] public string Email { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore] [BsonElement("emailNormalized")] public string EmailNormalized { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore] [BsonElement("usernameNormalized")] public string TenDangNhapNormalized { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        [Column("matkhau")]
        [BsonElement("passwordHash")] public string MatKhau { get; set; } = string.Empty;

        [Required]
        [MaxLength(30)]
        [Column("vaitro")]
        [BsonElement("role")] public string VaiTro { get; set; } = "User";

        [Column("trangthai")]
        [BsonElement("isActive")] public bool TrangThai { get; set; } = true;

        [BsonIgnore]
        public ICollection<LichSuTamTrang> LichSuTamTrangs { get; set; } = new List<LichSuTamTrang>();

        [BsonIgnore]
        public ICollection<DanhGia> DanhGias { get; set; } = new List<DanhGia>();
    }
}
