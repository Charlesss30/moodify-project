using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver.Linq;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Models;

namespace mood_recommendation.Controllers;

[ApiController, Route("api/[controller]")]
public class MoodMappingController(MongoStore db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.TamTrangs
        .OrderBy(x => x.TamTrangID).Select(x => new {
            x.TamTrangID, x.TenTamTrang, x.MinValence, x.MaxValence, x.MinArousal, x.MaxArousal, x.UpdatedAt
        }).ToListAsync());

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(MoodDto dto)
    {
        if (await db.TamTrangs.AnyAsync(x => x.TenTamTrang.ToLower() == dto.TenTamTrang.Trim().ToLower()))
            return Conflict(new { message = "Mood already exists." });
        var mood = new TamTrang();
        Apply(mood, dto);
        await db.Insert(mood);
        return Created($"/api/MoodMapping/{mood.TamTrangID}", mood);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var mood = await db.TamTrangs.FirstOrDefaultAsync(x => x.TamTrangID == id);
        return mood == null ? NotFound() : Ok(mood);
    }

    [HttpPut("{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, MoodDto dto)
    {
        var mood = await db.Find<TamTrang>(id);
        if (mood == null) return NotFound();
        if (await db.TamTrangs.AnyAsync(x => x.TamTrangID != id && x.TenTamTrang.ToLower() == dto.TenTamTrang.Trim().ToLower()))
            return Conflict(new { message = "Mood already exists." });
        Apply(mood, dto);
        await db.Update(mood);
        return Ok(mood);
    }

    [HttpDelete("{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var mood = await db.Find<TamTrang>(id);
        if (mood == null) return NotFound();
        if (await db.LichSuTamTrangs.AnyAsync(x => x.TamTrangID == id))
            return Conflict(new { message = "This mood has history entries. Edit it instead of deleting it." });
        await db.Delete(mood);
        return NoContent();
    }

    [HttpPut("content/{id}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Map(string id, ContentMoodDto dto)
    {
        var content = await db.GetContent(id);
        if (content == null) return NotFound();
        content.Valence = dto.Valence; content.Arousal = dto.Arousal;
        content.ReviewStatus = "reviewed"; content.MoodRuleVersion = null; content.ModelVersion = null;
        if (content.Nhac != null) content.Nhac.MoodSource = "admin-adjusted";
        if (content.Phim != null) content.Phim.MoodSource = "manual";
        await db.SaveContent(content);
        return Ok(new { content.NoiDungID, content.Valence, content.Arousal });
    }

    private static void Apply(TamTrang mood, MoodDto dto)
    {
        mood.TenTamTrang = dto.TenTamTrang.Trim();
        mood.MinValence = dto.MinValence; mood.MaxValence = dto.MaxValence;
        mood.MinArousal = dto.MinArousal; mood.MaxArousal = dto.MaxArousal;
    }
}
