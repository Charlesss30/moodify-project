using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        private readonly AppDbContext _context;

        public MoviesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                return Ok(await _context.NoiDungGiaiTris
                    .AsNoTracking()
                    .Include(x => x.Phim)
                    .Where(x => MovieTypes.Contains(x.LoaiNoiDung))
                    .ToListAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể lấy danh sách phim.", detail = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                var movie = await _context.NoiDungGiaiTris.AsNoTracking().Include(x => x.Phim)
                    .FirstOrDefaultAsync(x => x.NoiDungID == id && MovieTypes.Contains(x.LoaiNoiDung));
                return movie == null ? NotFound(new { message = "Không tìm thấy phim." }) : Ok(movie);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể lấy phim.", detail = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create(MovieRequestDto dto)
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
                    NoiDungID = dto.NoiDungID.Trim(), TieuDe = dto.TieuDe.Trim(), LoaiNoiDung = "Movie",
                    HinhAnh = dto.HinhAnh, MoTa = dto.MoTa,
                    Phim = new Phim { TheLoai = dto.TheLoai, DiemDanhGiaTB = dto.DiemDanhGiaTB }
                };
                _context.NoiDungGiaiTris.Add(content);
                await _context.SaveChangesAsync();
                return CreatedAtAction(nameof(GetById), new { id = content.NoiDungID }, content);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể thêm phim.", detail = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, MovieRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TieuDe))
            {
                return BadRequest(new { message = "TieuDe không được để trống." });
            }

            try
            {
                var movie = await _context.NoiDungGiaiTris.Include(x => x.Phim)
                    .FirstOrDefaultAsync(x => x.NoiDungID == id && MovieTypes.Contains(x.LoaiNoiDung));
                if (movie == null || movie.Phim == null)
                {
                    return NotFound(new { message = "Không tìm thấy phim." });
                }

                movie.TieuDe = dto.TieuDe.Trim(); movie.HinhAnh = dto.HinhAnh; movie.MoTa = dto.MoTa;
                movie.Phim.TheLoai = dto.TheLoai; movie.Phim.DiemDanhGiaTB = dto.DiemDanhGiaTB;
                await _context.SaveChangesAsync();
                return Ok(movie);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể cập nhật phim.", detail = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var movie = await _context.NoiDungGiaiTris.FirstOrDefaultAsync(x => x.NoiDungID == id && MovieTypes.Contains(x.LoaiNoiDung));
                if (movie == null) return NotFound(new { message = "Không tìm thấy phim." });
                _context.NoiDungGiaiTris.Remove(movie);
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không thể xóa phim.", detail = ex.Message });
            }
        }
    }
}