using mood_recommendation.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver.Linq;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Models;
namespace mood_recommendation.Controllers;

[ApiController, Route("api/[controller]"), Authorize]
public class HistoryController(MongoStore db, ExternalCatalog external) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> Moods([FromQuery] int page = 1)
    {
        if (page < 1 || page > 100000) return BadRequest();
        var query = db.LichSuTamTrangs.Where(x => x.TaiKhoanID == UserId);
        var pageItems = await query.OrderByDescending(x=>x.ThoiGian).ThenByDescending(x=>x.LichSuID).Skip((page-1)*50).Take(50).ToListAsync();
        var moodIds=pageItems.Select(x=>x.TamTrangID).Distinct().ToArray();
        var names=(await db.TamTrangs.Where(x=>moodIds.Contains(x.TamTrangID)).ToListAsync()).ToDictionary(x=>x.TamTrangID,x=>x.TenTamTrang);
        return Ok(new { total=await query.CountAsync(),page,pageSize=50,items=pageItems.Select(x=>new {
            x.LichSuID,x.RecommendationId,x.TamTrangID,tenTamTrang=names.GetValueOrDefault(x.TamTrangID),
            x.ThoiGian,x.Valence,x.Arousal,x.VanBanDauVao,x.RecommendedItems,x.Algorithm,x.Source,
            x.AnalysisId,x.AnalyzerVersion,x.MappingVersion,x.AnalysisSource,x.DecisionSource,
            x.MoodConfigHash,x.AnalysisExpiresAt
        }) });
    }
    [HttpGet("ratings")]
    public async Task<IActionResult> Ratings([FromQuery] int page = 1)
    {
        if (page < 1 || page > 100000) return BadRequest();
        var query = db.DanhGias.Where(x => x.TaiKhoanID == UserId);
        var rows = await query.OrderByDescending(x => x.ThoiGian).ThenByDescending(x => x.DanhGiaID)
            .Skip((page - 1) * 50).Take(50).ToListAsync();
        var ids = rows.Select(x => x.NoiDungID).Distinct().ToArray();
        var contents = await PersonalContentQuery.Contents(db, ids);
        var references = await PersonalContentQuery.References(external, ids, HttpContext.RequestAborted);
        string? Snapshot(DanhGia row, string field) => row.ExtraElements.TryGetValue(field, out var value) && value.IsString ? value.AsString : null;
        return Ok(new { total = await query.CountAsync(), page, pageSize = 50,
            items = rows.Select(x => new {
                x.DanhGiaID, x.NoiDungID, x.SoSao, x.NhanXet, x.ThoiGian, x.RecommendationId,
                available = contents.ContainsKey(x.NoiDungID) || references.Contains(x.NoiDungID),
                content = PersonalContentQuery.Card(contents.GetValueOrDefault(x.NoiDungID), x.NoiDungID,
                    Snapshot(x, "title"), Snapshot(x, "imageUrl"), Snapshot(x, "contentType"))
            }) });
    }
    [HttpPost("ratings")]
    public async Task<IActionResult> Rate(RatingDto dto)
    {
        var content = await db.GetContent(dto.NoiDungID) ?? await external.Get(dto.NoiDungID, HttpContext.RequestAborted);
        if (content is null) return NotFound();
        if (!await ActivityQuery.ValidRecommendation(db, UserId, dto.RecommendationId, dto.NoiDungID))
            return BadRequest(new { message = "Invalid recommendation reference." });
        var rating = new DanhGia { TaiKhoanID = UserId, NoiDungID = dto.NoiDungID, RecommendationId = dto.RecommendationId,
            SoSao = dto.SoSao, NhanXet = dto.NhanXet?.Trim(), ThoiGian = DateTime.UtcNow };
        rating.ExtraElements["title"] = content.TieuDe;
        rating.ExtraElements["imageUrl"] = content.HinhAnh is null ? MongoDB.Bson.BsonNull.Value : content.HinhAnh;
        rating.ExtraElements["contentType"] = content.LoaiNoiDung;
        await db.Insert(rating);
        
        return Ok(new { rating.DanhGiaID, rating.NoiDungID, rating.SoSao, rating.NhanXet, rating.ThoiGian });
    }
    [HttpDelete("ratings/{id:long}")]
    public async Task<IActionResult> DeleteRating(long id)
    {
        var rating = await db.DanhGias.FirstOrDefaultAsync(x => x.DanhGiaID == id && x.TaiKhoanID == UserId);
        if (rating == null) return NotFound();
        await db.Delete(rating);  return NoContent();
    }
}
