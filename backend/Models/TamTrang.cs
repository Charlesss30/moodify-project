using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mood_recommendation.Models
{
    [Table("tam_trang")]
    public class TamTrang
    {
        [Key]
        [Column("tamtrangid")]
        public int TamTrangID { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("tentamtrang")]
        public string TenTamTrang { get; set; } = string.Empty;

        [Column("minvalence", TypeName = "decimal(5,4)")]
        public decimal MinValence { get; set; }

        [Column("maxvalence", TypeName = "decimal(5,4)")]
        public decimal MaxValence { get; set; }

        [Column("minarousal", TypeName = "decimal(5,4)")]
        public decimal MinArousal { get; set; }

        [Column("maxarousal", TypeName = "decimal(5,4)")]
        public decimal MaxArousal { get; set; }

        public ICollection<LichSuTamTrang> LichSuTamTrangs { get; set; } = new List<LichSuTamTrang>();
    }
}
