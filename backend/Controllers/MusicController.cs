using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Models;

namespace mood_recommendation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MusicController : ControllerBase
    {
        private static readonly string[] MusicTypes = ["Music", "Nhạc", "Nhac"];
        private readonly AppDbContext _context;

        public MusicController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                return Ok(await _context.NoiDungGiaiTris.AsNoTracking().Include(x => x.Nhac)
                    .Where(x => MusicTypes.Contains(x.LoaiNoiDung)).ToListAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể lấy danh sách nhạc.", detail = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                var music = await _context.NoiDungGiaiTris.AsNoTracking().Include(x => x.Nhac)
                    .FirstOrDefaultAsync(x => x.NoiDungID == id && MusicTypes.Contains(x.LoaiNoiDung));
                return music == null ? NotFound(new { message = "Không tìm thấy bài hát." }) : Ok(music);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể lấy bài hát.", detail = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(MusicRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NoiDungID) || string.IsNullOrWhiteSpace(dto.TieuDe))
            {
                return BadRequest(new { message = "NoiDungID và TieuDe không được để trống." });
            }

            try
            {
                if (await _context.NoiDungGiaiTris.AnyAsync(x => x.NoiDungID == dto.NoiDungID.Trim()))
                {
                    return BadRequest(new { message = "NoiDungID đã tồn tại." });
                }

                var content = new NoiDungGiaiTri
                {
                    NoiDungID = dto.NoiDungID.Trim(), TieuDe = dto.TieuDe.Trim(), LoaiNoiDung = "Music", HinhAnh = dto.HinhAnh,
                    Nhac = new Nhac { TenNgheSi = dto.TenNgheSi, Genre = dto.Genre, Duration = dto.Duration }
                };
                _context.NoiDungGiaiTris.Add(content);
                await _context.SaveChangesAsync();
                return CreatedAtAction(nameof(GetById), new { id = content.NoiDungID }, content);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể thêm bài hát.", detail = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, MusicRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TieuDe))
            {
                return BadRequest(new { message = "TieuDe không được để trống." });
            }

            try
            {
                var music = await _context.NoiDungGiaiTris.Include(x => x.Nhac)
                    .FirstOrDefaultAsync(x => x.NoiDungID == id && MusicTypes.Contains(x.LoaiNoiDung));
                if (music == null || music.Nhac == null) return NotFound(new { message = "Không tìm thấy bài hát." });
                music.TieuDe = dto.TieuDe.Trim(); music.HinhAnh = dto.HinhAnh;
                music.Nhac.TenNgheSi = dto.TenNgheSi; music.Nhac.Genre = dto.Genre; music.Nhac.Duration = dto.Duration;
                await _context.SaveChangesAsync();
                return Ok(music);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể cập nhật bài hát.", detail = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var music = await _context.NoiDungGiaiTris.FirstOrDefaultAsync(x => x.NoiDungID == id && MusicTypes.Contains(x.LoaiNoiDung));
                if (music == null) return NotFound(new { message = "Không tìm thấy bài hát." });
                _context.NoiDungGiaiTris.Remove(music);
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể xóa bài hát.", detail = ex.Message });
            }
        }
    }
}