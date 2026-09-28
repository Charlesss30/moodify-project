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
    public class GenresController : ControllerBase
    {
        private readonly MongoStore _context;

        public GenresController(MongoStore context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                return Ok(await _context.TheLoais

                    .OrderBy(x => x.TenTheLoai)
                    .ToListAsync());
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to load genres." });
            }
        }

        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(GenreRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TenTheLoai))
            {
                return BadRequest(new { message = "Genre name is required." });
            }

            try
            {
                var name = dto.TenTheLoai.Trim();
                if (await _context.TheLoais.AnyAsync(x => x.TenTheLoai.ToLower() == name.ToLower()))
                {
                    return BadRequest(new { message = "Genre already exists." });
                }

                var genre = new TheLoai { TenTheLoai = name, ContentType = dto.ContentType ?? "Both", DefaultValence = dto.DefaultValence, DefaultArousal = dto.DefaultArousal };
                await _context.Insert(genre);
                return CreatedAtAction(nameof(GetById), new { id = genre.TheLoaiID }, genre);
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to add genre." });
            }
        }

        [HttpPut("{id}")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(string id, GenreRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TenTheLoai))
            {
                return BadRequest(new { message = "Genre name is required." });
            }

            try
            {
                var genre = await _context.Find<TheLoai>(id);
                if (genre == null)
                {
                    return NotFound(new { message = "Genre not found." });
                }

                var name = dto.TenTheLoai.Trim();
                if (await _context.TheLoais.AnyAsync(x => x.TheLoaiID != id && x.TenTheLoai.ToLower() == name.ToLower()))
                    return Conflict(new { message = "Genre already exists." });
                genre.TenTheLoai = name;
                var type = dto.ContentType ?? genre.ContentType;
                var v = dto.ClearMoodDefaults ? null : dto.DefaultValence ?? genre.DefaultValence;
                var a = dto.ClearMoodDefaults ? null : dto.DefaultArousal ?? genre.DefaultArousal;
                if(type != genre.ContentType || v != genre.DefaultValence || a != genre.DefaultArousal) genre.MoodVersion++;
                genre.ContentType = type; genre.DefaultValence = v; genre.DefaultArousal = a;
                await _context.Update(genre);
                return Ok(genre);
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to update genre." });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                var genre = await _context.TheLoais.FirstOrDefaultAsync(x => x.TheLoaiID == id);
                return genre == null
                    ? NotFound(new { message = "Genre not found." })
                    : Ok(genre);
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to load genre." });
            }
        }

        [HttpDelete("{id}")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var genre = await _context.Find<TheLoai>(id);
                if (genre == null)
                {
                    return NotFound(new { message = "Genre not found." });
                }

                if (await _context.Phims.AnyAsync(x => x.TheLoaiIDs.Contains(id)) || await _context.Nhacs.AnyAsync(x => x.TheLoaiID == id))
                    return Conflict(new { message = "This genre is used by movies or music. Update their genres before deleting it." });
                await _context.Delete(genre);
                return NoContent();
            }
            catch (Exception ex) when (ex is not MongoException && ex is not System.ComponentModel.DataAnnotations.ValidationException)
            {
                return StatusCode(500, new { message = "Unable to delete genre." });
            }
        }
    }
}