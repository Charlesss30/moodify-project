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
        return Ok(new { total=await query.CountAsync(),page,pageSize=50,items=pageItems.Select(x=>new {x.LichSuID,x.TamTrangID,tenTamTrang=names.GetValueOrDefault(x.TamTrangID),x.ThoiGian,x.Valence,x.Arousal,x.VanBanDauVao}) });
    }
    [HttpGet("ratings")]
    public async Task<IActionResult> Ratings([FromQuery] int page = 1)
    {
        if (page < 1 || page > 100000) return BadRequest();
        var query = db.DanhGias.Where(x => x.TaiKhoanID == UserId);
        return Ok(new { total = await query.CountAsync(), page, pageSize = 50,
            items = await query.OrderByDescending(x => x.ThoiGian).ThenByDescending(x => x.DanhGiaID)
                .Skip((page - 1) * 50).Take(50)
                .Select(x => new { x.DanhGiaID, x.NoiDungID, x.SoSao, x.NhanXet, x.ThoiGian }).ToListAsync() });
    }
    [HttpPost("ratings")]
    public async Task<IActionResult> Rate(RatingDto dto)
    {
        if (!await db.NoiDungGiaiTris.AnyAsync(x => x.NoiDungID == dto.NoiDungID) && !await external.Contains(dto.NoiDungID)) return NotFound();
        var rating = new DanhGia { TaiKhoanID = UserId, NoiDungID = dto.NoiDungID,
            SoSao = dto.SoSao, NhanXet = dto.NhanXet?.Trim(), ThoiGian = DateTime.UtcNow };
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
