using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.Models;
using mood_recommendation.Services;

namespace mood_recommendation.Controllers;

[ApiController, Route("api/SavedContent"), Authorize]
public sealed class SavedContentController(MongoStore db, ExternalCatalog external) : ControllerBase
{
    private int AccountId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IMongoCollection<SavedContent> Collection => db.Database.GetCollection<SavedContent>("savedContents");
    private static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 100;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] string? type = null, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000 || type is not (null or "Movie" or "Music")) return BadRequest();
        var filter = Builders<SavedContent>.Filter.Eq(x => x.AccountId, AccountId);
        if (type is not null) filter &= Builders<SavedContent>.Filter.Eq(x => x.ContentType, type == "Music" ? "Song" : "Movie");
        var rows = await Collection.Find(filter).SortByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * 12).Limit(12).ToListAsync(ct);
        var ids = rows.Select(x => x.ContentId).ToArray();
        var contents = await PersonalContentQuery.Contents(db, ids);
        var references = await PersonalContentQuery.References(external, ids, ct);
        return Ok(new {
            page, pageSize = 12, total = await Collection.CountDocumentsAsync(filter, cancellationToken: ct),
            items = rows.Select(x => new {
                x.ContentId, x.CreatedAt,
                available = contents.ContainsKey(x.ContentId) || references.Contains(x.ContentId),
                content = PersonalContentQuery.Card(contents.GetValueOrDefault(x.ContentId), x.ContentId, x.Title, x.ImageUrl, x.ContentType)
            })
        });
    }

    [HttpGet("{contentId}")]
    public async Task<IActionResult> Status(string contentId, CancellationToken ct)
    {
        if (!ValidId(contentId)) return BadRequest();
        return Ok(new { contentId, saved = await Collection.Find(x => x.AccountId == AccountId && x.ContentId == contentId).AnyAsync(ct) });
    }

    [HttpPut("{contentId}")]
    public async Task<IActionResult> Save(string contentId, CancellationToken ct)
    {
        if (!ValidId(contentId)) return BadRequest();
        var id = AccountId + ":" + contentId;
        // Repeated requests preserve both the original timestamp and list order.
        if (await Collection.Find(x => x.Id == id).AnyAsync(ct)) return Ok(new { contentId, saved = true });
        var content = await db.GetContent(contentId) ?? await external.Get(contentId, ct);
        if (content is null) return NotFound();
        var update = Builders<SavedContent>.Update
            .SetOnInsert(x => x.AccountId, AccountId).SetOnInsert(x => x.ContentId, contentId)
            .SetOnInsert(x => x.Title, content.TieuDe).SetOnInsert(x => x.ImageUrl, content.HinhAnh)
            .SetOnInsert(x => x.ContentType, content.LoaiNoiDung).SetOnInsert(x => x.CreatedAt, DateTime.UtcNow);
        await Collection.UpdateOneAsync(x => x.Id == id, update, new UpdateOptions { IsUpsert = true }, ct);
        return Ok(new { contentId, saved = true });
    }

    [HttpDelete("{contentId}")]
    public async Task<IActionResult> Remove(string contentId, CancellationToken ct)
    {
        if (!ValidId(contentId)) return BadRequest();
        await Collection.DeleteOneAsync(x => x.AccountId == AccountId && x.ContentId == contentId, ct);
        return NoContent();
    }
}
