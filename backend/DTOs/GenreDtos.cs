using System.ComponentModel.DataAnnotations;

namespace mood_recommendation.DTOs
{
    public class GenreRequestDto
    {
        [Required]
        [StringLength(100)]
        public string TenTheLoai { get; set; } = string.Empty;
    }
}