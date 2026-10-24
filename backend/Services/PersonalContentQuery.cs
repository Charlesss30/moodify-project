using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;

namespace mood_recommendation.Services;

// List pages use local metadata and saved snapshots; they never fan out to remote APIs.
public static class PersonalContentQuery
{
    public static async Task<Dictionary<string, NoiDungGiaiTri>> Contents(MongoStore db, string[] ids)
    {
        var rows = await db.NoiDungGiaiTris.Where(x => ids.Contains(x.NoiDungID)).ToListAsync();
        return (await db.Hydrate(rows)).ToDictionary(x => x.NoiDungID);
    }

    public static async Task<HashSet<string>> References(ExternalCatalog external, string[] ids, CancellationToken ct)
        => (await external.References.Find(x => ids.Contains(x.Id)).ToListAsync(ct))
            .Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

    public static object Card(NoiDungGiaiTri? current, string id, string? title, string? imageUrl, string? type)
        => new {
            noiDungID = id,
            tieuDe = current?.TieuDe ?? title ?? "Nội dung không còn khả dụng",
            hinhAnh = current?.HinhAnh ?? imageUrl,
            loaiNoiDung = current?.LoaiNoiDung ?? type ?? (id.StartsWith("SPOTIFY_", StringComparison.Ordinal) ? "Song" : "Movie"),
            phim = current?.Phim,
            nhac = current?.Nhac
        };
}
