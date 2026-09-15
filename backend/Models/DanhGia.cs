using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("danh_gia")]
    public class DanhGia
    {
        [Key]
        [Column("danhgiaid")]
        public long DanhGiaID { get; set; }

        [Required]
        [Column("taikhoanid")]
        public int TaiKhoanID { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("noidungid")]
        public string NoiDungID { get; set; } = string.Empty;

        [Required]
        [Range(1, 5)]
        [Column("sosao")]
        public int SoSao { get; set; }

        [Column("nhanxet")]
        public string? NhanXet { get; set; }

        [Column("thoigian")]
        public DateTime ThoiGian { get; set; }

        [ForeignKey("TaiKhoanID")]
        public TaiKhoan? TaiKhoan { get; set; }

        [ForeignKey("NoiDungID")]
        public NoiDungGiaiTri? NoiDungGiaiTri { get; set; }
    }
}
