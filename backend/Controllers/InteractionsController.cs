using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Services;

namespace mood_recommendation.Controllers;

public sealed class InteractionRequest
{
    [Required, RegularExpression("^[a-zA-Z0-9-]{16,64}$")] public string EventId { get; set; } = "";
    [Required, StringLength(100)] public string ContentId { get; set; } = "";
    [Required, RegularExpression("^(detail_open|player_loaded|external_open|trailer_open)$")] public string Action { get; set; } = "";
    [StringLength(64)] public string? RecommendationId { get; set; }
}

[ApiController, Route("api/Interactions"), Authorize]
public class InteractionsController(MongoStore db, ExternalCatalog external) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(InteractionRequest request)
    {
        var accountId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var content = await db.GetContent(request.ContentId) ?? await external.Get(request.ContentId, HttpContext.RequestAborted);
        if (content is null) return NotFound();
        if (!await ActivityQuery.ValidRecommendation(db, accountId, request.RecommendationId, request.ContentId))
            return BadRequest(new { message = "Recommendation does not belong to you or does not contain this content." });
        if (request.Action == "player_loaded" && content.LoaiNoiDung != "Song") return BadRequest();
        if (request.Action == "trailer_open" && content.LoaiNoiDung != "Movie") return BadRequest();
        var collection = db.Database.GetCollection<BsonDocument>("interactions");
        var id = accountId + ":" + request.EventId;
        var row = new BsonDocument { ["_id"] = id, ["accountId"] = accountId,
            ["contentId"] = content.NoiDungID, ["action"] = request.Action, ["createdAt"] = DateTime.UtcNow,
            ["title"] = content.TieuDe, ["imageUrl"] = content.HinhAnh is null ? BsonNull.Value : content.HinhAnh,
            ["contentType"] = content.LoaiNoiDung,
            ["recommendationId"] = request.RecommendationId is null ? BsonNull.Value : request.RecommendationId };
        try { await collection.InsertOneAsync(row); }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var previous = await collection.Find(new BsonDocument("_id", id)).FirstAsync();
            if (previous["contentId"] != row["contentId"] || previous["action"] != row["action"] || previous["recommendationId"] != row["recommendationId"])
                return Conflict(new { message = "Event ID was already used for a different interaction." });
        }
        return Ok(new { eventId = request.EventId });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1)
    {
        if (page < 1 || page > 100000) return BadRequest();
        return Ok(await ActivityQuery.List(db, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), "interactions", page));
    }
}

public static class ActivityQuery
{
    public static async Task<bool> ValidRecommendation(MongoStore db, int accountId, string? sessionId, string contentId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return true;
        var history = await db.LichSuTamTrangs.FirstOrDefaultAsync(x => x.TaiKhoanID == accountId && x.RecommendationId == sessionId);
        return history?.RecommendedItems?.Any(x => x.NoiDungID == contentId) == true;
    }
    public static async Task<object> List(MongoStore db, int accountId, string kind, int page)
    {
        var name = kind == "recommendations" ? "moodHistory" : kind == "ratings" ? "ratings" : "interactions";
        var collection = db.Database.GetCollection<BsonDocument>(name);
        var filter = new BsonDocument("accountId", accountId);
        var rows = await collection.Find(filter).Sort(new BsonDocument { ["createdAt"] = -1, ["_id"] = -1 }).Skip((page - 1) * 12).Limit(12).ToListAsync();
        var items = new List<object>();
        foreach (var row in rows)
        {
            string? Text(string key) => row.GetValue(key, BsonNull.Value).IsString ? row[key].AsString : null;
            var contentId = Text("contentId");
            var content = contentId is null ? null : await db.GetContent(contentId);
            items.Add(new { id = row["_id"].ToString(), contentId, title = content?.TieuDe ?? Text("title"),
                imageUrl = content?.HinhAnh ?? Text("imageUrl"), contentType = content?.LoaiNoiDung ?? Text("contentType"),
                action = Text("action"), createdAt = row["createdAt"].ToUniversalTime(),
                stars = row.Contains("stars") ? (int?)row["stars"].ToInt32() : null,
                comment = Text("comment"), algorithm = Text("algorithm"), recommendationId = Text("recommendationId"),
                moodId = row.Contains("moodId") ? (int?)row["moodId"].ToInt32() : null,
                recommendedItems = row.GetValue("recommendedItems", BsonNull.Value).IsBsonArray
                    ? row["recommendedItems"].AsBsonArray.Select(x => new { id = x["contentId"].AsString, title = x["title"].AsString }).ToArray() : null });
        }
        return new { items, page, pageSize = 12, total = await collection.CountDocumentsAsync(filter) };
    }
}
