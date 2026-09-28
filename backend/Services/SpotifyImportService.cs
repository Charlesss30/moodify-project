using MongoDB.Driver.Linq;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.Models;
namespace mood_recommendation.Services;

public sealed record SpotifyImportResult(string ExternalId, string Status, string? ContentId, string Message);
public sealed class SpotifyImportService(MongoStore db, ISpotifyClient spotify)
{
    private static string? Fit(string? value, int length) => value == null ? null : value[..Math.Min(value.Length, length)];
    public async Task<SpotifyImportResult> Import(string id, TamTrang? mood, string moodSource, CancellationToken ct, TheLoai? genre = null)
    {
        var refs=db.Database.GetCollection<ExternalReference>("externalReferences");
        if(await refs.Find(x=>x.Id=="SPOTIFY_"+id).AnyAsync(ct)) return new(id,"existing","SPOTIFY_"+id,"This song is already in the library.");
        var existing = await db.Nhacs.FirstOrDefaultAsync(x => x.Source == "Spotify" && x.ExternalId == id, ct);
        if (existing != null) return new(id, "existing", existing.NoiDungID, "This song is already in the library.");
        var track = await spotify.Info(id, ct);
        var canonicalId = track.ExternalId;
        if(await refs.Find(x=>x.Id=="SPOTIFY_"+canonicalId).AnyAsync(ct)) return new(id,"existing","SPOTIFY_"+canonicalId,"This song is already in the library.");
        existing = await db.Nhacs.FirstOrDefaultAsync(x => x.Source == "Spotify" && x.ExternalId == canonicalId, ct);
        if (existing != null) return new(id, "existing", existing.NoiDungID, "This song is already in the library.");
        var contentId = "SPOTIFY_" + canonicalId;
        var content = new NoiDungGiaiTri {
            ReviewStatus = "reviewed", NoiDungID = contentId, TieuDe = Fit(track.Title, 255)!, LoaiNoiDung = "Song",
            HinhAnh = track.ImageUrl, MoTa = track.Album == null ? null : "Album: " + track.Album,
            Valence = mood == null ? 0 : (mood.MinValence + mood.MaxValence) / 2,
            Arousal = mood == null ? 0 : (mood.MinArousal + mood.MaxArousal) / 2,
            Nhac = new Nhac {
                TenNgheSi = Fit(track.Artists, 255), Album = Fit(track.Album, 255), Genre = genre?.TenTheLoai,
                Duration = track.Duration, Source = "Spotify", ExternalId = canonicalId, SourceUrl = track.SourceUrl,
                ReleaseDate = Fit(track.ReleaseDate, 40), LastSyncedAt = DateTime.UtcNow, MoodSource = moodSource
            }
        };

        if(genre != null)
        {
            var error = await new GenreMoodService(db).Apply(content,genre.TenTheLoai,null,null,null,true);
            if(error != null) throw new System.ComponentModel.DataAnnotations.ValidationException(error);
        }
        try { await db.SaveContent(content, create: true); }
        catch (MongoWriteException)
        {

            existing = await db.Nhacs.FirstOrDefaultAsync(x => x.Source == "Spotify" && x.ExternalId == canonicalId, ct);
            if (existing != null) return new(id, "existing", existing.NoiDungID, "This song was imported by another request.");
            throw;
        }
        return new(id, "imported", contentId, "Song imported and mood mapped.");
    }
}
