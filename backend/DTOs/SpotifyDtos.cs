using System.ComponentModel.DataAnnotations;
namespace mood_recommendation.DTOs;
public sealed class SpotifyImportDto : IValidatableObject
{
    [StringLength(100)] public string? GenreId { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if(TamTrangID.HasValue == !string.IsNullOrWhiteSpace(GenreId)) yield return new("Select either a reviewed mood or a configured music genre.");
    }
    [Required, MinLength(1), MaxLength(20)] public string[] SongIds { get; set; } = [];
    [Range(1, int.MaxValue)] public int? TamTrangID { get; set; }
}
