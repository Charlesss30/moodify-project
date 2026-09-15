using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("lich_su_tam_trang")]
    public class LichSuTamTrang
    {
        [Key]
        [Column("lichsuid")]
        public long LichSuID { get; set; }

        [Required]
        [Column("taikhoanid")]
        public int TaiKhoanID { get; set; }

        [Required]
        [Column("tamtrangid")]
        public int TamTrangID { get; set; }

        [Column("thoigian")]
        public DateTime ThoiGian { get; set; }

        [Column("vanbandauvao")]
        public string? VanBanDauVao { get; set; }

        [Column("valence", TypeName = "decimal(6,4)")]
        public decimal Valence { get; set; }

        [Column("arousal", TypeName = "decimal(6,4)")]
        public decimal Arousal { get; set; }

        [ForeignKey("TaiKhoanID")]
        public TaiKhoan? TaiKhoan { get; set; }

        [ForeignKey("TamTrangID")]
        public TamTrang? TamTrang { get; set; }
    }
}
