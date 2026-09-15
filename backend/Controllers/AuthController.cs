using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Models;

namespace MoodRecommendationAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AuthController(AppDbContext context)
        {
            _context = context;
        }

        // POST: api/Auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TenDangNhap) ||
                string.IsNullOrWhiteSpace(dto.Email) ||
                string.IsNullOrWhiteSpace(dto.MatKhau))
            {
                return BadRequest(new
                {
                    message = "Vui lòng nhập đầy đủ thông tin."
                });
            }

            var exists = await _context.TaiKhoans
                .AnyAsync(x =>
                    x.TenDangNhap == dto.TenDangNhap ||
                    x.Email == dto.Email);

            if (exists)
            {
                return BadRequest(new
                {
                    message = "Tên đăng nhập hoặc email đã tồn tại."
                });
            }

            var taiKhoan = new TaiKhoan
            {
                TenDangNhap = dto.TenDangNhap,
                Email = dto.Email,
                MatKhau = BCrypt.Net.BCrypt.HashPassword(dto.MatKhau),
                VaiTro = "NguoiDung",
                TrangThai = true
            };

            _context.TaiKhoans.Add(taiKhoan);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Đăng ký thành công."
            });
        }

        // POST: api/Auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var user = await _context.TaiKhoans
                .FirstOrDefaultAsync(x =>
                    x.Email == dto.Identifier ||
                    x.TenDangNhap == dto.Identifier);

            if (user == null)
            {
                return Unauthorized(new
                {
                    message = "Email hoặc tên đăng nhập không tồn tại."
                });
            }

            if (!user.TrangThai)
            {
                return Unauthorized(new
                {
                    message = "Tài khoản đã bị khóa."
                });
            }

            bool passwordValid = BCrypt.Net.BCrypt.Verify(dto.MatKhau, user.MatKhau);

            if (!passwordValid)
            {
                return Unauthorized(new
                {
                    message = "Mật khẩu không chính xác."
                });
            }

            return Ok(new
            {
                message = "Đăng nhập thành công.",
                user = new
                {
                    user.TaiKhoanID,
                    user.TenDangNhap,
                    user.Email,
                    user.VaiTro
                }
            });
        }
    }
}