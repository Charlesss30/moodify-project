using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;

namespace mood_recommendation.Controllers;

[ApiController, Route("api/Discovery"), Authorize]
public sealed class DiscoveryController(MongoStore db, IMemoryCache cache) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery, RegularExpression("^(popular|updated)$")] string mode = "popular",
        [FromQuery, RegularExpression("^(Movie|Music)$")] string? type = null,
        [FromQuery, Range(1, 50)] int limit = 12, CancellationToken ct = default)
    {
        var key = $"discovery:{mode}:{type}:{limit}";
        if (cache.TryGetValue<object>(key, out var found)) return Ok(found);
        var now = DateTime.UtcNow;
        var contentType = type == "Music" ? "Song" : type;
        List<NoiDungGiaiTri> contents;
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        if (mode == "popular")
        {
            var pipeline = new List<BsonDocument>
            {
                new("$match", new BsonDocument {
                    ["action"] = "detail_open",
                    ["createdAt"] = new BsonDocument { ["$gte"] = now.AddDays(-7), ["$lte"] = now }
                }),
                // Repeated opens by one account count once per content.
                new("$group", new BsonDocument("_id", new BsonDocument {
                    ["content"] = "$contentId", ["account"] = "$accountId"
                })),
                new("$group", new BsonDocument {
                    ["_id"] = "$_id.content", ["count"] = new BsonDocument("$sum", 1)
                }),
                new("$lookup", new BsonDocument {
                    ["from"] = "contents", ["localField"] = "_id",
                    ["foreignField"] = "_id", ["as"] = "content"
                }),
                new("$unwind", "$content"),
                new("$match", new BsonDocument("content.deleted", new BsonDocument("$ne", true)))
            };
            if (contentType is not null)
                pipeline.Add(new("$match", new BsonDocument("content.contentType", contentType)));
            pipeline.Add(new("$sort", new BsonDocument { ["count"] = -1, ["_id"] = 1 }));
            pipeline.Add(new("$limit", limit));
            var rows = await db.Database.GetCollection<BsonDocument>("interactions")
                .Aggregate<BsonDocument>(pipeline).ToListAsync(ct);
            foreach (var row in rows) counts[row["_id"].AsString] = row["count"].ToInt64();
            var ids = counts.Keys.ToArray();
            contents = ids.Length == 0 ? [] : await db.Hydrate(
                await db.NoiDungGiaiTris.Where(x => ids.Contains(x.NoiDungID)).ToListAsync(ct));
            contents = contents.OrderByDescending(x => counts[x.NoiDungID])
                .ThenBy(x => x.NoiDungID, StringComparer.Ordinal).ToList();
        }
        else
        {
            var query = db.NoiDungGiaiTris.Where(x => x.UpdatedAt != null);
            if (contentType is not null) query = query.Where(x => x.LoaiNoiDung == contentType);
            contents = await db.Hydrate(await query.OrderByDescending(x => x.UpdatedAt)
                .ThenBy(x => x.NoiDungID).Take(limit).ToListAsync(ct));
        }
        var result = new {
            mode, windowDays = mode == "popular" ? (int?)7 : null, generatedAt = now,
            source = "Moodify",
            note = mode == "popular"
                ? "Distinct accounts opening content details during the last 7 days, not completed views/listens or global charts."
                : "Latest catalog update time, not release date.",
            items = contents.Select(item => new {
                content = item,
                uniqueVisitors = counts.TryGetValue(item.NoiDungID, out var count) ? (long?)count : null,
                updatedAt = item.UpdatedAt
            }).ToArray()
        };
        cache.Set(key, result, TimeSpan.FromMinutes(1));
        return Ok(result);
    }
}
