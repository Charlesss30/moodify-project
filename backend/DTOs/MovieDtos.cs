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

        public string? HinhAnh { get; set; }

        public string? MoTa { get; set; }

        [StringLength(255)]
        public string? TheLoai { get; set; }

        [Range(0, 10)]
        public decimal? DiemDanhGiaTB { get; set; }
    }
}