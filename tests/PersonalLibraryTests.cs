using System.Text.Json.Nodes;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.Models;

public static class PersonalLibraryTests
{
    public static async Task Run(MongoStore db, Func<string, string, object?, int, string?, Task<JsonNode?>> call,
        Action<bool, string> check, int userId, string user, string other)
    {
        await call("GET", "/SavedContent", null, 401, null);
        await call("PUT", "/SavedContent/personal-song", null, 401, null);
        await call("DELETE", "/SavedContent/personal-song", null, 401, null);
        await call("GET", "/History/ratings", null, 401, null);
        await call("GET", "/SavedContent?page=0", null, 400, user);
        await call("GET", "/SavedContent?type=invalid", null, 400, user);
        await call("PUT", "/SavedContent/personal-missing", null, 404, user);
        var song = new NoiDungGiaiTri {
            NoiDungID = "personal-song", TieuDe = "Personal song", LoaiNoiDung = "Song",
            HinhAnh = "https://images.test.invalid/song.jpg", Valence = 0, Arousal = 0,
            Nhac = new Nhac { TenNgheSi = "Test artist" }
        };
        await db.SaveContent(song, create: true);
        for (var i = 0; i < 13; i++) {
            await db.SaveContent(new NoiDungGiaiTri {
                NoiDungID = $"personal-film-{i:00}", TieuDe = $"Personal film {i}", LoaiNoiDung = "Movie",
                HinhAnh = $"https://images.test.invalid/{i}.jpg", Phim = new Phim { Director = "Test director" }
            }, create: true);
            await call("PUT", $"/SavedContent/personal-film-{i:00}", null, 200, user);
        }
        check((await call("GET", "/SavedContent/personal-song", null, 200, user))!["saved"]!.GetValue<bool>() == false, "Unexpected initial saved state.");
        await call("PUT", "/SavedContent/personal-song", null, 200, user);
        var collection = db.Database.GetCollection<SavedContent>("savedContents");
        var first = await collection.Find(x => x.AccountId == userId && x.ContentId == song.NoiDungID).SingleAsync();
        await call("PUT", "/SavedContent/personal-song", null, 200, user);
        var repeat = await collection.Find(x => x.AccountId == userId && x.ContentId == song.NoiDungID).SingleAsync();
        check(first.CreatedAt == repeat.CreatedAt, "Repeated saves changed the timestamp.");
        check(await collection.CountDocumentsAsync(x => x.AccountId == userId && x.ContentId == song.NoiDungID) == 1, "Repeated save created duplicates.");
        check((await call("GET", "/SavedContent/personal-song", null, 200, other))!["saved"]!.GetValue<bool>() == false, "Saved state leaked across accounts.");
        await call("DELETE", "/SavedContent/personal-song", null, 204, other);
        check((await call("GET", "/SavedContent/personal-song", null, 200, user))!["saved"]!.GetValue<bool>(), "Another account removed an own bookmark.");
        var page = await call("GET", "/SavedContent", null, 200, user);
        check(page!["items"]!.AsArray().Count == 12 && page["total"]!.GetValue<long>() == 14, "Saved pagination or total is incorrect.");
        var next = await call("GET", "/SavedContent?page=2", null, 200, user);
        check(next!["items"]!.AsArray().Count == 2, "Saved second page is incorrect.");
        var firstIds = page["items"]!.AsArray().Select(x => x!["contentId"]!.GetValue<string>()).ToHashSet();
        check(next["items"]!.AsArray().All(x => !firstIds.Contains(x!["contentId"]!.GetValue<string>())), "Saved pages overlap.");
        page = await call("GET", "/SavedContent?type=Music", null, 200, user);
        check(page!["total"]!.GetValue<long>() == 1 && page["items"]![0]!["content"]!["nhac"]!["tenNgheSi"]!.GetValue<string>() == "Test artist", "Saved music filter or metadata missing.");
        check((await call("GET", "/SavedContent?type=Movie", null, 200, user))!["total"]!.GetValue<long>() == 13, "Saved movie filter incorrect.");
        check((await call("GET", "/SavedContent?accountId=" + userId, null, 200, other))!["total"]!.GetValue<long>() == 0, "Query string overrode bookmark ownership.");
        await call("PUT", "/SavedContent/personal-song", null, 200, other);
        check((await call("GET", "/SavedContent", null, 200, other))!["total"]!.GetValue<long>() == 1, "Independent account bookmark missing.");

        await call("POST", "/History/ratings", new { noiDungID = song.NoiDungID, soSao = 3, nhanXet = "Own feedback" }, 200, user);
        await call("POST", "/History/ratings", new { noiDungID = song.NoiDungID, soSao = 1, nhanXet = "Other feedback" }, 200, other);
        var ratings = await call("GET", "/History/ratings", null, 200, user);
        var row = ratings!["items"]!.AsArray().First(x => x!["noiDungID"]!.GetValue<string>() == song.NoiDungID)!;
        check(row["soSao"]!.GetValue<int>() == 3 && row["nhanXet"]!.GetValue<string>() == "Own feedback", "Ratings ownership or feedback was lost.");
        check(row["content"]!["tieuDe"]!.GetValue<string>() == song.TieuDe && row["content"]!["hinhAnh"]!.GetValue<string>() == song.HinhAnh, "Rating poster/title metadata missing.");
        check(!ratings["items"]!.AsArray().Any(x => x!["nhanXet"]?.GetValue<string>() == "Other feedback"), "Another account's rating leaked.");

        // The list must refresh metadata from the catalog, while retaining a snapshot for unavailable items.
        song.TieuDe = "Updated personal song";
        await db.Update(song);
        page = await call("GET", "/SavedContent?type=Music", null, 200, user);
        check(page!["items"]![0]!["content"]!["tieuDe"]!.GetValue<string>() == song.TieuDe, "Saved card did not use current metadata.");
        await db.Collection<NoiDungGiaiTri>().UpdateOneAsync(x => x.NoiDungID == song.NoiDungID,
            Builders<NoiDungGiaiTri>.Update.Set(x => x.Deleted, true));
        page = await call("GET", "/SavedContent?type=Music", null, 200, user);
        check(!page!["items"]![0]!["available"]!.GetValue<bool>(), "Deleted saved content was marked available.");
        check(page["items"]![0]!["content"]!["tieuDe"]!.GetValue<string>() == "Personal song", "Saved snapshot missing.");
        ratings = await call("GET", "/History/ratings", null, 200, user);
        row = ratings!["items"]!.AsArray().First(x => x!["noiDungID"]!.GetValue<string>() == song.NoiDungID)!;
        check(!row["available"]!.GetValue<bool>() && row["content"]!["tieuDe"]!.GetValue<string>() == "Personal song", "Unavailable rating snapshot missing.");
        await call("DELETE", "/SavedContent/personal-song", null, 204, user);
        await call("DELETE", "/SavedContent/personal-song", null, 204, user);
        check(!(await call("GET", "/SavedContent/personal-song", null, 200, user))!["saved"]!.GetValue<bool>(), "Unsave did not persist.");
        check((await call("GET", "/SavedContent/personal-song", null, 200, other))!["saved"]!.GetValue<bool>(), "Unsave affected another account.");
        await db.Collection<NoiDungGiaiTri>().UpdateOneAsync(x => x.NoiDungID == song.NoiDungID,
            Builders<NoiDungGiaiTri>.Update.Set(x => x.Deleted, false));

        var before = await call("GET", "/Statistics?days=7&offsetMinutes=0", null, 200, user);
        var oldOpened = before!["summary"]!["openedContents"]!.GetValue<long>();
        for (var i = 0; i < 2; i++)
            await call("POST", "/Interactions", new { contentId = song.NoiDungID, action = "detail_open", eventId = Guid.NewGuid().ToString("N") }, 200, user);
        await call("POST", "/Interactions", new { contentId = song.NoiDungID, action = "player_loaded", eventId = Guid.NewGuid().ToString("N") }, 200, user);
        var after = await call("GET", "/Statistics?days=7&offsetMinutes=0", null, 200, user);
        check(after!["summary"]!["openedContents"]!.GetValue<long>() == oldOpened + 1, "Overview counted repeated opens or player loads as distinct content.");
        check(after["summary"]!["moodDays"]!.GetValue<int>() == after["trend"]!.AsArray().Count(x => x!["moodCount"]!.GetValue<long>() > 0), "Overview mood days do not match recorded mood dates.");
    }
}
