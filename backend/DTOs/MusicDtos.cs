using System.ComponentModel.DataAnnotations;

namespace mood_recommendation.DTOs
{
    public class MusicRequestDto
    {
        [Range(-1,1)] public decimal? Valence { get; set; }
        [Range(-1,1)] public decimal? Arousal { get; set; }
        [RegularExpression("^(manual|genre-default|legacy-unverified|admin-selected|admin-adjusted)$")] public string? MoodSource { get; set; }
        [Required]
        [StringLength(100)]
        public string NoiDungID { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string TieuDe { get; set; } = string.Empty;

        [StringLength(255)]
        public string? TenNgheSi { get; set; }

        [Url]
        public string? HinhAnh { get; set; }

        [StringLength(100)]
        public string? Genre { get; set; }

        [Range(0, int.MaxValue)]
        public int? Duration { get; set; }
    }
}