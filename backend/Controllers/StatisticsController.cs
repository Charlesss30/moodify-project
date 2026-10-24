using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using mood_recommendation.Data;
namespace mood_recommendation.Controllers;

[ApiController, Route("api/Statistics"), Authorize]
public class StatisticsController(MongoStore db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30, [FromQuery] int offsetMinutes = 420, CancellationToken ct = default)
    {
        if (days is not (7 or 30 or 90) || offsetMinutes is < -840 or > 840) return BadRequest();
        var account = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = DateTime.UtcNow;
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var first = now.Add(offset).Date.AddDays(1 - days).Subtract(offset);
        var previous = first.AddDays(-days);
        var timezone = (offsetMinutes < 0 ? "-" : "+") + Math.Abs(offsetMinutes / 60).ToString("00") + ":" + (Math.Abs(offsetMinutes) % 60).ToString("00");
        BsonDocument DateKey(string format) => new("$dateToString", new BsonDocument { ["format"] = format, ["date"] = "$createdAt", ["timezone"] = timezone });
        async Task<List<BsonDocument>> Group(string collection, DateTime from, DateTime until, BsonValue key, bool va = false)
        {
            var group = new BsonDocument { ["_id"] = key, ["count"] = new BsonDocument("$sum", 1) };
            if (va) { group["valence"] = new BsonDocument("$avg", "$valence"); group["arousal"] = new BsonDocument("$avg", "$arousal"); }
            return await db.Database.GetCollection<BsonDocument>(collection).Aggregate<BsonDocument>(new BsonDocument[] {
                new("$match", new BsonDocument { ["accountId"] = account, ["createdAt"] = new BsonDocument { ["$gte"] = from, ["$lt"] = until } }),
                new("$group", group), new("$sort", new BsonDocument("_id", 1))
            }).ToListAsync(ct);
        }
        var moodDaily = await Group("moodHistory", first, now, DateKey("%Y-%m-%d"), true);
        var activityDaily = await Group("interactions", first, now, DateKey("%Y-%m-%d"));
        var moods = await Group("moodHistory", first, now, "$moodId");
        var types = await Group("interactions", first, now, "$contentType");
        var hours = await Group("interactions", first, now, DateKey("%H"));
        var rating = await Group("ratings", first, now, "$stars");
        var oldMoods = await Group("moodHistory", previous, first, BsonNull.Value);
        var oldActivity = await Group("interactions", previous, first, BsonNull.Value);
        var moodNames = (await db.Collection<mood_recommendation.Models.TamTrang>().Find(_ => true).ToListAsync(ct)).ToDictionary(x => x.TamTrangID, x => x.TenTamTrang);
        long Count(IEnumerable<BsonDocument> rows) => rows.Sum(x => x["count"].ToInt64());
        double? Number(BsonDocument? row, string field) => row is not null && row.TryGetValue(field, out var value) && value.IsNumeric ? value.ToDouble() : null;
        var trend = Enumerable.Range(0, days).Select(i => {
            var date = first.Add(offset).AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var m = moodDaily.FirstOrDefault(x => x["_id"] == date);
            var a = activityDaily.FirstOrDefault(x => x["_id"] == date);
            return new { date, moodCount = m?["count"].ToInt64() ?? 0, interactions = a?["count"].ToInt64() ?? 0, valence = Number(m, "valence"), arousal = Number(m, "arousal") };
        }).ToArray();
        // Aggregate genres in Mongo; no pagination or raw text/account identifiers returned.
        var genres = await db.Database.GetCollection<BsonDocument>("interactions").Aggregate<BsonDocument>(new BsonDocument[] {
            new("$match", new BsonDocument { ["accountId"] = account, ["createdAt"] = new BsonDocument { ["$gte"] = first, ["$lt"] = now } }),
            new("$lookup", new BsonDocument { ["from"] = "movies", ["localField"] = "contentId", ["foreignField"] = "_id", ["as"] = "movie" }),
            new("$lookup", new BsonDocument { ["from"] = "music", ["localField"] = "contentId", ["foreignField"] = "_id", ["as"] = "song" }),
            new("$project", new BsonDocument("genres", new BsonDocument("$setUnion", new BsonArray {
                new BsonDocument("$ifNull", new BsonArray { new BsonDocument("$arrayElemAt", new BsonArray { "$movie.genreIds", 0 }), new BsonArray() }), "$song.genreId" }))),
            new("$unwind", "$genres"), new("$match", new BsonDocument("genres", new BsonDocument("$type", "string"))),
            new("$group", new BsonDocument { ["_id"] = "$genres", ["count"] = new BsonDocument("$sum", 1) }),
            new("$sort", new BsonDocument { ["count"] = -1, ["_id"] = 1 }), new("$limit", 10),
            new("$lookup", new BsonDocument { ["from"] = "genres", ["localField"] = "_id", ["foreignField"] = "_id", ["as"] = "genre" })
        }).ToListAsync(ct);
        var opened = await db.Database.GetCollection<BsonDocument>("interactions").Aggregate<BsonDocument>(new BsonDocument[] {
            new("$match", new BsonDocument {
                ["accountId"] = account, ["action"] = "detail_open",
                ["createdAt"] = new BsonDocument { ["$gte"] = first, ["$lt"] = now }
            }),
            new("$group", new BsonDocument("_id", "$contentId")),
            new("$count", "total")
        }).FirstOrDefaultAsync(ct);
        var totalRatings = Count(rating);
        return Ok(new { days, offsetMinutes, from = first, until = now,
            note = "Counts describe recorded interactions, not listening/viewing duration. Current period includes today; previous period contains full days. Genre labels use current catalog metadata.",
            summary = new { moodEntries = Count(moods), interactions = Count(types), activeDays = activityDaily.Count,
                moodDays = moodDaily.Count, openedContents = opened?["total"].ToInt64() ?? 0,
                ratings = totalRatings, averageStars = totalRatings == 0 ? (double?)null : rating.Sum(x => x["_id"].ToDouble() * x["count"].ToInt64()) / totalRatings },
            previous = new { moodEntries = Count(oldMoods), interactions = Count(oldActivity) }, trend,
            moods = moods.Select(x => new { name = x["_id"].IsNumeric ? moodNames.GetValueOrDefault(x["_id"].ToInt32(), "Mood đã xóa") : "Không rõ", count = x["count"].ToInt64() }),
            contentTypes = types.Select(x => new { name = x["_id"].IsString ? x["_id"].AsString : "Unknown", count = x["count"].ToInt64() }),
            hours = Enumerable.Range(0,24).Select(h => new { name = h.ToString("00") + ":00", count = hours.FirstOrDefault(x => x["_id"] == h.ToString("00"))?["count"].ToInt64() ?? 0 }),
            genres = genres.Select(x => new { name = x["genre"].AsBsonArray.FirstOrDefault()?.AsBsonDocument.GetValue("name", x["_id"]).AsString ?? x["_id"].AsString, count = x["count"].ToInt64() })
        });
    }
}
