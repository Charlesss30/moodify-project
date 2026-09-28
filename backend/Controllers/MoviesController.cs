using mood_recommendation.Services;
using System.ComponentModel.DataAnnotations;
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
    public class MoviesController : ControllerBase
    {
        private static readonly string[] MovieTypes = ["Movie", "Phim"];
        private readonly MongoStore _context;
        private readonly ExternalCatalog external;

        public MoviesController(MongoStore context, ExternalCatalog catalog)
        {
            _context = context; external = catalog;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                return Ok((await _context.Contents("Movie")).Concat(await external.List("Movie",HttpContext.RequestAborted)));
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to load movies." });
            }
        }

        [HttpGet("mood-suggestion")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> SuggestMood([FromQuery, StringLength(255)] string? genres)
            => Ok(await new GenreMoodService(_context).Suggest(genres, "Movie"));

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                var movie = await _context.GetContent(id) ?? await external.Get(id,HttpContext.RequestAborted);
                if (movie != null && movie.LoaiNoiDung != "Movie") return NotFound();
                return movie == null ? NotFound(new { message = "Movie not found." }) : Ok(movie);
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to load movie." });
            }
        }

        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(MovieRequestDto dto)
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
                    NoiDungID = dto.NoiDungID.Trim(), TieuDe = dto.TieuDe.Trim(), LoaiNoiDung = "Movie",
                    HinhAnh = dto.HinhAnh, MoTa = dto.MoTa,
                    Phim = new Phim { TheLoai = dto.TheLoai, DiemDanhGiaTB = dto.DiemDanhGiaTB, IMDBID = dto.IMDBID }
                };
                var moodError = await new GenreMoodService(_context).Apply(content, dto.TheLoai, dto.Valence, dto.Arousal, dto.MoodSource, true);
                if (moodError != null) return BadRequest(new { message = moodError });
                await _context.SaveContent(content, create: true);
                return CreatedAtAction(nameof(GetById), new { id = content.NoiDungID }, content);
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to add movie." });
            }
        }

        [HttpPut("{id}")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(string id, MovieRequestDto dto)
        {
            if(await external.Contains(id)) return Conflict(new {message="This item stores only a source ID. Its metadata is managed by the provider."});
            if (string.IsNullOrWhiteSpace(dto.TieuDe))
            {
                return BadRequest(new { message = "Title is required." });
            }

            try
            {
                var movie = await _context.GetContent(id);
                if (movie != null && movie.LoaiNoiDung != "Movie") return NotFound();
                if (movie == null || movie.Phim == null)
                {
                    return NotFound(new { message = "Movie not found." });
                }

                var moodError = await new GenreMoodService(_context).Apply(movie, dto.TheLoai, dto.Valence, dto.Arousal, dto.MoodSource, false);
                if (moodError != null) return BadRequest(new { message = moodError });
                movie.TieuDe = dto.TieuDe.Trim(); movie.HinhAnh = dto.HinhAnh; movie.MoTa = dto.MoTa;
                movie.Phim.IMDBID = dto.IMDBID; movie.Phim.TheLoai = dto.TheLoai; movie.Phim.DiemDanhGiaTB = dto.DiemDanhGiaTB;
                await _context.SaveContent(movie);
                return Ok(movie);
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to update movie." });
            }
        }

        [HttpDelete("{id}")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            if(id.StartsWith(ExternalCatalog.Prefix("Movie")) && await external.Delete(id)) return NoContent();
            try
            {
                var movie = await _context.GetContent(id);
                if (movie != null && movie.LoaiNoiDung != "Movie") return NotFound();
                if (movie == null) return NotFound(new { message = "Movie not found." });
                await _context.DeleteContent(id);
                return NoContent();
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to delete movie." });
            }
        }
    }
}