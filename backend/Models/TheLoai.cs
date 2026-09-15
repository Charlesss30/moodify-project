using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("theloai")]
    public class TheLoai
    {
        [Key]
        [Column("theloaiid")]
        public int TheLoaiID { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("tentheloai")]
        public string TenTheLoai { get; set; } = string.Empty;
    }
}