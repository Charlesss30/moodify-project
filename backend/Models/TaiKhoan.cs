using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("tai_khoan")]
    public class TaiKhoan
    {
        [Key]
        [Column("taikhoanid")]
        public int TaiKhoanID { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("tendangnhap")]
        public string TenDangNhap { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        [Column("matkhau")]
        public string MatKhau { get; set; } = string.Empty;

        [Required]
        [MaxLength(30)]
        [Column("vaitro")]
        public string VaiTro { get; set; } = "NguoiDung";

        [Column("trangthai")]
        public bool TrangThai { get; set; } = true;

        public ICollection<LichSuTamTrang> LichSuTamTrangs { get; set; } = new List<LichSuTamTrang>();

        public ICollection<DanhGia> DanhGias { get; set; } = new List<DanhGia>();
    }
}
