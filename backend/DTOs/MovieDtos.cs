using System.ComponentModel.DataAnnotations;

namespace mood_recommendation.DTOs
{
    public class MovieRequestDto : IValidatableObject
    {
        [Required]
        [StringLength(100)]
        public string NoiDungID { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string TieuDe { get; set; } = string.Empty;

        [Url]
        public string? HinhAnh { get; set; }

        [StringLength(2048)] public string? TrailerUrl { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext context) {
            if (Cast is not null && Cast.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 120))
                yield return new ValidationResult("Cast names must contain 1–120 characters.", new[] { nameof(Cast) });
            if (!string.IsNullOrWhiteSpace(TrailerUrl) && !mood_recommendation.Services.TrailerLookup.ValidUrl(TrailerUrl))
                yield return new ValidationResult("Use a valid HTTPS YouTube video URL.", new[] { nameof(TrailerUrl) });
        }
        [Range(1800, 2100)] public int? ReleaseYear { get; set; }
        [Range(1, 1000)] public int? RuntimeMinutes { get; set; }
        [MaxLength(30)] public string[]? Cast { get; set; }
        public string? MoTa { get; set; }
        [RegularExpression(@"^[1-9][0-9]{0,8}$")] public string? TMDBID { get; set; }
        [RegularExpression("^(TMDB|IMDb|Manual|Legacy)$")] public string? RatingSource { get; set; }
        [RegularExpression("^(TMDB|OMDb|Manual|Legacy)$")] public string? MetadataSource { get; set; }
        [StringLength(500)] public string? Director { get; set; }

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