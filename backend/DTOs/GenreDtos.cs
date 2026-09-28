using System.ComponentModel.DataAnnotations;

namespace mood_recommendation.DTOs
{
    public class GenreRequestDto : IValidatableObject
    {
        [RegularExpression("^(Movie|Song|Both)$")] public string? ContentType { get; set; }
        [Range(-1,1)] public decimal? DefaultValence { get; set; }
        [Range(-1,1)] public decimal? DefaultArousal { get; set; }
        public bool ClearMoodDefaults { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if(DefaultValence.HasValue != DefaultArousal.HasValue) yield return new("Enter both default coordinates.");
            if(ClearMoodDefaults && (DefaultValence.HasValue || DefaultArousal.HasValue)) yield return new("Cannot clear and set defaults in the same request.");
        }
        [Required]
        [StringLength(100)]
        public string TenTheLoai { get; set; } = string.Empty;
    }
}