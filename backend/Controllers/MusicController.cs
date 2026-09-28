using mood_recommendation.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver.Linq;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Models;

namespace mood_recommendation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MusicController : ControllerBase
    {
        private static readonly string[] MusicTypes = ["Music", "Song", "Music", "Nhac"];
        private readonly MongoStore _context;
        private readonly ExternalCatalog external;

        public MusicController(MongoStore context, ExternalCatalog catalog)
        {
            _context = context; external = catalog;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                return Ok((await _context.Contents("Song")).Concat(await external.List("Song",HttpContext.RequestAborted)));
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to load music." });
            }
        }

        [HttpGet("mood-suggestion")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> SuggestMood([FromQuery, System.ComponentModel.DataAnnotations.StringLength(255)] string? genres)
            => Ok(await new GenreMoodService(_context).Suggest(genres,"Song"));

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                var music = await _context.GetContent(id) ?? await external.Get(id,HttpContext.RequestAborted);
                if (music != null && music.LoaiNoiDung != "Song") return NotFound();
                return music == null ? NotFound(new { message = "Song not found." }) : Ok(music);
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to load song." });
            }
        }

        [HttpGet("{id}/preview")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<IActionResult> Preview(string id, CancellationToken ct)
        {
            if(id.StartsWith("SPOTIFY_") && await external.Contains(id)) return Ok(new {mode="embed",embedUrl=SpotifyClient.EmbedUrl(id[8..]),sourceUrl="https://open.spotify.com/track/"+id[8..]});
            var music = await _context.Nhacs.FirstOrDefaultAsync(x => x.NoiDungID == id, ct);
            if (music == null) return NotFound(new { message = "Song not found." });
            if (music.Source != "Spotify" || !mood_recommendation.Services.SpotifyClient.ValidId(music.ExternalId))
                return UnprocessableEntity(new { message = "This song is not linked to Spotify." });
            return Ok(new { mode = "embed", embedUrl = mood_recommendation.Services.SpotifyClient.EmbedUrl(music.ExternalId!),
                sourceUrl = "https://open.spotify.com/track/" + music.ExternalId });
        }

        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(MusicRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NoiDungID) || string.IsNullOrWhiteSpace(dto.TieuDe))
            {
                return BadRequest(new { message = "Content ID and title are required." });
            }

            try
            {
                if (await _context.NoiDungGiaiTris.AnyAsync(x => x.NoiDungID == dto.NoiDungID.Trim()) || await external.Contains(dto.NoiDungID.Trim()))
                {
                    return BadRequest(new { message = "Content ID already exists." });
                }

                var content = new NoiDungGiaiTri
                {
                    NoiDungID = dto.NoiDungID.Trim(), TieuDe = dto.TieuDe.Trim(), LoaiNoiDung = "Song", HinhAnh = dto.HinhAnh,
                    Nhac = new Nhac { TenNgheSi = dto.TenNgheSi, Genre = dto.Genre, Duration = dto.Duration }
                };
                var moodError = await new GenreMoodService(_context).Apply(content,dto.Genre,dto.Valence,dto.Arousal,dto.MoodSource,true);
                if(moodError != null) return BadRequest(new { message = moodError });
                await _context.SaveContent(content, create: true);
                return CreatedAtAction(nameof(GetById), new { id = content.NoiDungID }, content);
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to add song." });
            }
        }

        [HttpPut("{id}")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(string id, MusicRequestDto dto)
        {
            if(await external.Contains(id)) return Conflict(new {message="This item stores only a source ID. Its metadata is managed by the provider."});
            if (string.IsNullOrWhiteSpace(dto.TieuDe))
            {
                return BadRequest(new { message = "Title is required." });
            }

            try
            {
                var music = await _context.GetContent(id);
                if (music != null && music.LoaiNoiDung != "Song") return NotFound();
                if (music == null || music.Nhac == null) return NotFound(new { message = "Song not found." });
                var moodError = await new GenreMoodService(_context).Apply(music,dto.Genre,dto.Valence,dto.Arousal,dto.MoodSource,false);
                if(moodError != null) return BadRequest(new { message = moodError });
                music.TieuDe = dto.TieuDe.Trim(); music.HinhAnh = dto.HinhAnh;
                music.Nhac.TenNgheSi = dto.TenNgheSi; music.Nhac.Genre = dto.Genre; music.Nhac.Duration = dto.Duration;
                await _context.SaveContent(music);
                return Ok(music);
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to update song." });
            }
        }

        [HttpDelete("{id}")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            if(id.StartsWith(ExternalCatalog.Prefix("Song")) && await external.Delete(id)) return NoContent();
            try
            {
                var music = await _context.GetContent(id);
                if (music != null && music.LoaiNoiDung != "Song") return NotFound();
                if (music == null) return NotFound(new { message = "Song not found." });
                await _context.DeleteContent(id);
                return NoContent();
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to delete song." });
            }
        }
    }
}