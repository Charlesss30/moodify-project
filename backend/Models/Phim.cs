using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace mood_recommendation.Models
{
    [Table("phim")]
    public class Phim
    {
        [Key]
        [MaxLength(100)]
        [Column("noidungid")]
        public string NoiDungID { get; set; } = string.Empty;

        [MaxLength(255)]
        [Column("theloai")]
        public string? TheLoai { get; set; }

        [Column("diemdanhgiatb", TypeName = "decimal(3,2)")]
        public decimal? DiemDanhGiaTB { get; set; }

        [MaxLength(30)]
        [Column("imdbid")]
        public string? IMDBID { get; set; }

        [MaxLength(30)]
        [Column("tmdbid")]
        public string? TMDBID { get; set; }

        // Foreign Key
        [ForeignKey("NoiDungID")]
        [JsonIgnore]
        public NoiDungGiaiTri? NoiDungGiaiTri { get; set; }
    }
}
