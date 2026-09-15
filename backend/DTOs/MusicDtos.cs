using System.ComponentModel.DataAnnotations;

namespace mood_recommendation.DTOs
{
    public class MusicRequestDto
    {
        [Required]
        [StringLength(100)]
        public string NoiDungID { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string TieuDe { get; set; } = string.Empty;

        [StringLength(255)]
        public string? TenNgheSi { get; set; }

        public string? HinhAnh { get; set; }

        [StringLength(100)]
        public string? Genre { get; set; }

        [Range(0, int.MaxValue)]
        public int? Duration { get; set; }
    }
}