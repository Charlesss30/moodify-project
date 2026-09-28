using System.ComponentModel.DataAnnotations;

namespace mood_recommendation.DTOs
{
    public class MovieRequestDto
    {
        [Required]
        [StringLength(100)]
        public string NoiDungID { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string TieuDe { get; set; } = string.Empty;

        [Url]
        public string? HinhAnh { get; set; }

        public string? MoTa { get; set; }

        [StringLength(255)]
        public string? TheLoai { get; set; }

        [RegularExpression(@"^tt[0-9]{7,10}$")]
        public string? IMDBID { get; set; }

        [Range(-1, 1)] public decimal? Valence { get; set; }
        [Range(-1, 1)] public decimal? Arousal { get; set; }
        [RegularExpression("^(manual|genre-default|genre-rule-v1|legacy-unverified)$")] public string? MoodSource { get; set; }

        [Range(0, 10)]
        public decimal? DiemDanhGiaTB { get; set; }
    }
}