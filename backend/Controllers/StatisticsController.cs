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
    public async Task<IActionResult> Get([FromQuery] int days = 30, [FromQuery] int offsetMinutes = 420, int? year = null, CancellationToken ct = default)
    {
        if (offsetMinutes is < -840 or > 840) return BadRequest();
        var now = DateTime.UtcNow;
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var localNow = now.Add(offset);
        if ((!year.HasValue && days is not (7 or 30 or 90))
            || (year.HasValue && (year < 2000 || year > localNow.Year))) return BadRequest();
        var account = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var first = year.HasValue
            ? new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc).Subtract(offset)
            : localNow.Date.AddDays(1 - days).Subtract(offset);
        var periodUntil = year.HasValue && year.Value < localNow.Year
            ? first.AddYears(1) : now;
        var previous = year.HasValue ? first.AddYears(-1) : first.AddDays(-days);
        var previousUntil = year.HasValue && year.Value == localNow.Year
            ? periodUntil.AddYears(-1) : first;
        if (year.HasValue)
            days = (periodUntil.Add(offset).Date - first.Add(offset).Date).Days
                + (year.Value == localNow.Year ? 1 : 0);
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
        var moodDaily = await Group("moodHistory", first, periodUntil, DateKey("%Y-%m-%d"), true);
        var activityDaily = await Group("interactions", first, periodUntil, DateKey("%Y-%m-%d"));
        var moods = await Group("moodHistory", first, periodUntil, "$moodId");
        var types = await Group("interactions", first, periodUntil, "$contentType");
        var hours = await Group("interactions", first, periodUntil, DateKey("%H"));
        var rating = await Group("ratings", first, periodUntil, "$stars");
        var oldMoods = await Group("moodHistory", previous, previousUntil, BsonNull.Value);
        var oldActivity = await Group("interactions", previous, previousUntil, BsonNull.Value);
        var moodNames = (await db.Collection<mood_recommendation.Models.TamTrang>().Find(_ => true).ToListAsync(ct)).ToDictionary(x => x.TamTrangID, x => x.TenTamTrang);
        long Count(IEnumerable<BsonDocument> rows) => rows.Sum(x => x["count"].ToInt64());
        double? Number(BsonDocument? row, string field) => row is not null && row.TryGetValue(field, out var value) && value.IsNumeric ? value.ToDouble() : null;
        var moodTrend = year.HasValue
            ? await Group("moodHistory", first, periodUntil, DateKey("%Y-%m-01"), true) : moodDaily;
        var activityTrend = year.HasValue
            ? await Group("interactions", first, periodUntil, DateKey("%Y-%m-01")) : activityDaily;
        var bucketCount = year.HasValue ? (year.Value == localNow.Year ? localNow.Month : 12) : days;
        var trend = Enumerable.Range(0, bucketCount).Select(i => {
            var localStart = first.Add(offset);
            var date = (year.HasValue ? localStart.AddMonths(i) : localStart.AddDays(i))
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var m = moodTrend.FirstOrDefault(x => x["_id"] == date);
            var a = activityTrend.FirstOrDefault(x => x["_id"] == date);
            return new { date, moodCount = m?["count"].ToInt64() ?? 0,
                interactions = a?["count"].ToInt64() ?? 0,
                valence = Number(m, "valence"), arousal = Number(m, "arousal") };
        }).ToArray();
        // Aggregate genres in Mongo; no pagination or raw text/account identifiers returned.
        var genres = await db.Database.GetCollection<BsonDocument>("interactions").Aggregate<BsonDocument>(new BsonDocument[] {
            new("$match", new BsonDocument { ["accountId"] = account, ["createdAt"] = new BsonDocument { ["$gte"] = first, ["$lt"] = periodUntil } }),
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
                ["createdAt"] = new BsonDocument { ["$gte"] = first, ["$lt"] = periodUntil }
            }),
            new("$group", new BsonDocument("_id", "$contentId")),
            new("$count", "total")
        }).FirstOrDefaultAsync(ct);
        var totalRatings = Count(rating);
        return Ok(new { days, year, granularity = year.HasValue ? "month" : "day",
            offsetMinutes, from = first, until = periodUntil,
            note = year.HasValue
                ? "Calendar year in device timezone; monthly averages use recorded entries. Current year compares the same elapsed period last year; past years compare full years. Future months excluded."
                : "Counts describe recorded interactions, not listening/viewing duration. Current period includes today; previous period contains full days. Genre labels use current catalog metadata.",
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
