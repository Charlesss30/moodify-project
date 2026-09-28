using System.ComponentModel.DataAnnotations;
namespace mood_recommendation.DTOs;

public class UserAccessDto
{
    [Required, RegularExpression("^(User|Admin|AIEngineer)$")]
    public string VaiTro { get; set; } = "User";
    public bool TrangThai { get; set; } = true;
}
public class MoodDto : IValidatableObject
{
    [Required, StringLength(50)] public string TenTamTrang { get; set; } = "";
    [Range(-1, 1)] public decimal MinValence { get; set; }
    [Range(-1, 1)] public decimal MaxValence { get; set; }
    [Range(-1, 1)] public decimal MinArousal { get; set; }
    [Range(-1, 1)] public decimal MaxArousal { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(TenTamTrang)) yield return new("Mood name is required.");
        if (MinValence > MaxValence || MinArousal > MaxArousal)
            yield return new("Minimum values must not exceed maximum values.");
    }
}
public class ContentMoodDto
{
    [Range(-1, 1)] public decimal Valence { get; set; }
    [Range(-1, 1)] public decimal Arousal { get; set; }
}
public class RecommendationDto
{
    [Range(1, int.MaxValue)] public int TamTrangID { get; set; }
    [RegularExpression("^(Movie|Music)$")] public string? LoaiNoiDung { get; set; }
    [Range(1, 50)] public int Limit { get; set; } = 12;
}
public class RatingDto
{
    [Required, StringLength(100)] public string NoiDungID { get; set; } = "";
    [Range(1, 5)] public int SoSao { get; set; }
    [StringLength(2000)] public string? NhanXet { get; set; }
}
