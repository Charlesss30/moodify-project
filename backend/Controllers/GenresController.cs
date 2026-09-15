using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Models;

namespace mood_recommendation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GenresController : ControllerBase
    {
        private readonly AppDbContext _context;

        public GenresController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                return Ok(await _context.TheLoais
                    .AsNoTracking()
                    .OrderBy(x => x.TenTheLoai)
                    .ToListAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể lấy danh sách thể loại.", detail = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(GenreRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TenTheLoai))
            {
                return BadRequest(new { message = "Tên thể loại không được để trống." });
            }

            try
            {
                var name = dto.TenTheLoai.Trim();
                if (await _context.TheLoais.AnyAsync(x => x.TenTheLoai.ToLower() == name.ToLower()))
                {
                    return BadRequest(new { message = "Thể loại đã tồn tại." });
                }

                var genre = new TheLoai { TenTheLoai = name };
                _context.TheLoais.Add(genre);
                await _context.SaveChangesAsync();
                return CreatedAtAction(nameof(GetById), new { id = genre.TheLoaiID }, genre);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể thêm thể loại.", detail = ex.Message });
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, GenreRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TenTheLoai))
            {
                return BadRequest(new { message = "Tên thể loại không được để trống." });
            }

            try
            {
                var genre = await _context.TheLoais.FindAsync(id);
                if (genre == null)
                {
                    return NotFound(new { message = "Không tìm thấy thể loại." });
                }

                genre.TenTheLoai = dto.TenTheLoai.Trim();
                await _context.SaveChangesAsync();
                return Ok(genre);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể cập nhật thể loại.", detail = ex.Message });
            }
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                var genre = await _context.TheLoais.AsNoTracking().FirstOrDefaultAsync(x => x.TheLoaiID == id);
                return genre == null
                    ? NotFound(new { message = "Không tìm thấy thể loại." })
                    : Ok(genre);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể lấy thể loại.", detail = ex.Message });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var genre = await _context.TheLoais.FindAsync(id);
                if (genre == null)
                {
                    return NotFound(new { message = "Không tìm thấy thể loại." });
                }

                _context.TheLoais.Remove(genre);
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể xóa thể loại.", detail = ex.Message });
            }
        }
    }
}