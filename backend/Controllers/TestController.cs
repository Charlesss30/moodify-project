using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using mood_recommendation.Data;

namespace mood_recommendation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TestController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("accounts")]
        public async Task<IActionResult> GetAccounts()
        {
            var accounts = await _context.TaiKhoans
                .Select(x => new
                {
                    x.TaiKhoanID,
                    x.TenDangNhap,
                    x.Email,
                    x.VaiTro,
                    x.TrangThai
                })
                .ToListAsync();

            return Ok(accounts);
        }

        [HttpGet("moods")]
        public async Task<IActionResult> GetMoods()
        {
            var moods = await _context.TamTrangs
                .ToListAsync();

            return Ok(moods);
        }

        [HttpGet("contents")]
        public async Task<IActionResult> GetContents()
        {
            var contents = await _context.NoiDungGiaiTris
                .ToListAsync();

            return Ok(contents);
        }
    }
}
