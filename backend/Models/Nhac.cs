using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace mood_recommendation.Models
{
    [Table("nhac")]
    [BsonIgnoreExtraElements]
    public class Nhac
    {
        [BsonExtraElements, System.Text.Json.Serialization.JsonIgnore]
        public BsonDocument ExtraElements { get; set; } = new();

        [Key, BsonId]
        [MaxLength(100)]
        [Column("noidungid")]
        public string NoiDungID { get; set; } = string.Empty;

        [MaxLength(255)]
        [Column("tennghesi")]
        [BsonElement("artist")] public string? TenNgheSi { get; set; }

        [MaxLength(255)]
        [Column("album")]
        [BsonElement("album")] public string? Album { get; set; }

        [MaxLength(100)]
        [Column("genre")]
        [BsonIgnore] public string? Genre { get; set; }
        [BsonElement("genreId")] public string? TheLoaiID { get; set; }

        [Column("duration")]
        [BsonElement("duration")] public int? Duration { get; set; }

        [Column("energy", TypeName = "decimal(6,4)")]
        [BsonElement("energy")] public decimal? Energy { get; set; }

        [Column("valence", TypeName = "decimal(6,4)")]
        [BsonElement("valence")] public decimal? Valence { get; set; }

        [Column("danceability", TypeName = "decimal(6,4)")]
        [BsonElement("danceability")] public decimal? Danceability { get; set; }

        [Column("acousticness", TypeName = "decimal(6,4)")]
        [BsonElement("acousticness")] public decimal? Acousticness { get; set; }

        [Column("instrumentalness", TypeName = "decimal(6,4)")]
        [BsonElement("instrumentalness")] public decimal? Instrumentalness { get; set; }

        [Column("speechiness", TypeName = "decimal(6,4)")]
        [BsonElement("speechiness")] public decimal? Speechiness { get; set; }

        [Column("tempo", TypeName = "decimal(8,3)")]
        [BsonElement("tempo")] public decimal? Tempo { get; set; }

        [Column("popularity")]
        [BsonElement("popularity")] public int? Popularity { get; set; }

        [Column("source"), MaxLength(20)] [BsonElement("source")] public string? Source { get; set; } = "Local";
        [Column("externalid"), MaxLength(64)] [BsonElement("externalId")] public string? ExternalId { get; set; }
        [Column("sourceurl")] [BsonElement("sourceUrl")] public string? SourceUrl { get; set; }
        [Column("releasedate"), MaxLength(40)] [BsonElement("releaseDate")] public string? ReleaseDate { get; set; }
        [Column("lastsyncedat")] [BsonElement("lastSyncedAt")] public DateTime? LastSyncedAt { get; set; }
        [Column("moodsource"), MaxLength(40)] [BsonElement("moodSource")] public string? MoodSource { get; set; } = "manual";

        [ForeignKey("NoiDungID")]
        [JsonIgnore]
        [BsonIgnore]
        public NoiDungGiaiTri? NoiDungGiaiTri { get; set; }
    }
}
