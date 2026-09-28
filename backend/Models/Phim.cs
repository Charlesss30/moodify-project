using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace mood_recommendation.Models
{
    [Table("phim")]
    [BsonIgnoreExtraElements]
    public class Phim
    {
        [BsonExtraElements, System.Text.Json.Serialization.JsonIgnore]
        public BsonDocument ExtraElements { get; set; } = new();

        [Key, BsonId]
        [MaxLength(100)]
        [Column("noidungid")]
        public string NoiDungID { get; set; } = string.Empty;

        [MaxLength(255)]
        [Column("theloai")]
        [BsonIgnore] public string? TheLoai { get; set; }
        [BsonElement("genreIds")] public string[] TheLoaiIDs { get; set; } = [];

        [Column("diemdanhgiatb", TypeName = "decimal(4,2)")]
        [BsonElement("averageRating")] public decimal? DiemDanhGiaTB { get; set; }

        [MaxLength(30)]
        [Column("imdbid")]
        [BsonElement("imdbId")] public string? IMDBID { get; set; }

        [MaxLength(30)]
        [Column("tmdbid")]
        [BsonElement("tmdbId")] public string? TMDBID { get; set; }

        [MaxLength(40), Column("moodsource")]
        [BsonElement("moodSource")] public string? MoodSource { get; set; } = "manual";

        // Foreign Key
        [ForeignKey("NoiDungID")]
        [JsonIgnore]
        [BsonIgnore]
        public NoiDungGiaiTri? NoiDungGiaiTri { get; set; }
    }
}
