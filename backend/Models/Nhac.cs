using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace mood_recommendation.Models
{
    [Table("nhac")]
    public class Nhac
    {
        [Key]
        [MaxLength(100)]
        [Column("noidungid")]
        public string NoiDungID { get; set; } = string.Empty;

        [MaxLength(255)]
        [Column("tennghesi")]
        public string? TenNgheSi { get; set; }

        [MaxLength(255)]
        [Column("album")]
        public string? Album { get; set; }

        [MaxLength(100)]
        [Column("genre")]
        public string? Genre { get; set; }

        [Column("duration")]
        public int? Duration { get; set; }

        [Column("energy", TypeName = "decimal(6,4)")]
        public decimal? Energy { get; set; }

        [Column("valence", TypeName = "decimal(6,4)")]
        public decimal? Valence { get; set; }

        [Column("danceability", TypeName = "decimal(6,4)")]
        public decimal? Danceability { get; set; }

        [Column("acousticness", TypeName = "decimal(6,4)")]
        public decimal? Acousticness { get; set; }

        [Column("instrumentalness", TypeName = "decimal(6,4)")]
        public decimal? Instrumentalness { get; set; }

        [Column("speechiness", TypeName = "decimal(6,4)")]
        public decimal? Speechiness { get; set; }

        [Column("tempo", TypeName = "decimal(8,3)")]
        public decimal? Tempo { get; set; }

        [Column("popularity")]
        public int? Popularity { get; set; }

        [ForeignKey("NoiDungID")]
        [JsonIgnore]
        public NoiDungGiaiTri? NoiDungGiaiTri { get; set; }
    }
}
