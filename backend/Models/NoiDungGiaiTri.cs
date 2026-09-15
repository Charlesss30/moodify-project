using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("noi_dung_giai_tri")]
    public class NoiDungGiaiTri
    {
        [Key]
        [MaxLength(100)]
        [Column("noidungid")]
        public string NoiDungID { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        [Column("tieude")]
        public string TieuDe { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        [Column("loainoidung")]
        public string LoaiNoiDung { get; set; } = string.Empty;

        [Column("valence", TypeName = "decimal(6,4)")]
        public decimal Valence { get; set; }

        [Column("arousal", TypeName = "decimal(6,4)")]
        public decimal Arousal { get; set; }

        [Column("hinhanh")]
        public string? HinhAnh { get; set; }

        [Column("mota")]
        public string? MoTa { get; set; }
        public Phim? Phim { get; set; }
        public Nhac? Nhac { get; set; }
        public ICollection<DanhGia> DanhGias { get; set; } = new List<DanhGia>();
    }
}
