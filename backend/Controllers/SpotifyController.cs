using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver.Linq;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;

[ApiController, Route("api/[controller]"), Authorize(Roles = "Admin")]
public class SpotifyController(ISpotifyClient spotify, SpotifyImportService importer, MongoStore db) : ControllerBase
{
    private IActionResult Failure(SpotifyException e)
    {
        if (e.RetryAfter != null) Response.Headers.RetryAfter = e.RetryAfter.Value.ToString();
        return StatusCode(e.StatusCode, new { message = e.Message, code = e.Code, retryAfter = e.RetryAfter });
    }
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery, Required, StringLength(200)] string query,
        [FromQuery, Range(1,10)] int limit = 10, [FromQuery, Range(0,1000)] int offset = 0, CancellationToken ct = default)
    {
        try
        {
            var tracks = await spotify.Search(query, limit, offset, ct);
            var ids = tracks.Select(x => x.ExternalId).ToArray();
            var imported = await db.Nhacs.Where(x => x.Source == "Spotify" && ids.Contains(x.ExternalId!)).Select(x => x.ExternalId).ToListAsync(ct);
            var refIds=ids.Select(id=>"SPOTIFY_"+id).ToArray();
            var refs=await db.Database.GetCollection<mood_recommendation.Models.ExternalReference>("externalReferences").Find(x=>refIds.Contains(x.Id)).ToListAsync(ct);
            imported.AddRange(refs.Select(x=>x.Id[8..]));
            return Ok(new { items = tracks, importedIds = imported.Distinct(), offset, limit });
        }
        catch (SpotifyException e) { return Failure(e); }
    }
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { await spotify.Search("music", 1, 0, ct); return Ok(new { configured = true, connected = true, message = "Connected to the Spotify catalog." }); }
        catch (SpotifyException e) { return Failure(e); }
    }
    [HttpGet("{id}/preview")]
    public IActionResult Preview(string id) => SpotifyClient.ValidId(id)
        ? Ok(new { mode = "embed", embedUrl = SpotifyClient.EmbedUrl(id), sourceUrl = "https://open.spotify.com/track/" + id })
        : BadRequest(new { message = "Invalid Spotify ID." });

    [HttpPost("import")]
    public async Task<IActionResult> Import(SpotifyImportDto dto, CancellationToken ct)
    {
        if (dto.SongIds.Any(id => !SpotifyClient.ValidId(id))) return BadRequest(new { message = "Invalid Spotify track ID." });
        var mood = dto.TamTrangID.HasValue ? await db.TamTrangs.FirstOrDefaultAsync(x => x.TamTrangID == dto.TamTrangID, ct) : null;
        if (dto.TamTrangID.HasValue && mood == null) return NotFound(new { message = "Mood not found." });
        var genre = string.IsNullOrWhiteSpace(dto.GenreId) ? null : await db.Find<mood_recommendation.Models.TheLoai>(dto.GenreId);
        if (!string.IsNullOrWhiteSpace(dto.GenreId) && (genre == null || genre.ContentType == "Movie" || genre.DefaultValence == null || genre.DefaultArousal == null)) return BadRequest(new {message="Select a configured music genre."});
        var results = new List<SpotifyImportResult>();
        foreach (var id in dto.SongIds.Distinct())
        {
            try { results.Add(await importer.Import(id, mood, "admin-selected", ct, genre)); }
            catch (SpotifyException e)
            {
                results.Add(new(id, "unavailable", null, e.Message));
                if (e.StatusCode is 429 or 503)
                {
                    foreach (var rest in dto.SongIds.Distinct().Where(x => results.All(r => r.ExternalId != x)))
                        results.Add(new(rest, "unavailable", null, e.Message));
                    if (e.RetryAfter != null) Response.Headers.RetryAfter = e.RetryAfter.Value.ToString();
                    break;
                }
            }
        }
        return Ok(new { items = results });
    }
}
