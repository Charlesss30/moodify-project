using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver.Linq;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using mood_recommendation.Services;

namespace MoodRecommendationAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly MongoStore _context;
        private readonly TokenService _tokens;
        private readonly AdminLoginHandoff _handoff;

        public AuthController(MongoStore context, TokenService tokens, AdminLoginHandoff handoff)
        {
            _context = context;
            _tokens = tokens;
            _handoff = handoff;
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _context.TaiKhoans.Where(x => x.TaiKhoanID == id)
                .Select(x => new { x.TaiKhoanID, x.TenDangNhap, x.Email, x.VaiTro }).SingleAsync());
        }

        private CookieOptions HandoffCookie(bool expire = false) => new()
        {
            HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict,
            Path = AdminLoginHandoff.CookiePath, MaxAge = expire ? TimeSpan.Zero : TimeSpan.FromMinutes(1),
            IsEssential = true
        };

        [HttpPost("admin-session")]
        public async Task<IActionResult> AdminSession()
        {
            Response.Headers.CacheControl = "no-store";
            if (!Request.Cookies.TryGetValue(AdminLoginHandoff.CookieName, out var code))
                return NoContent();
            Response.Cookies.Delete(AdminLoginHandoff.CookieName, HandoffCookie(expire: true));
            var id = _handoff.Consume(code);
            if (id == null) return Unauthorized(new { message = "Your sign-in handoff has expired. Please sign in again." });
            var user = await _context.TaiKhoans.FirstOrDefaultAsync(x => x.TaiKhoanID == id);
            if (user == null || !user.TrangThai || !AdminRoles.IsAdmin(user.VaiTro))
                return StatusCode(403, new { message = "This account no longer has administrator access." });
            var expiresAt = DateTime.UtcNow.AddHours(1);
            return Ok(new { token = _tokens.Create(user, expiresAt), expiresAt });
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
                    message = "Please complete all required fields."
                });
            }

            var exists = await _context.TaiKhoans
                .AnyAsync(x =>
                    x.TenDangNhapNormalized == MongoStore.Normalize(dto.TenDangNhap) ||
                    x.EmailNormalized == MongoStore.Normalize(dto.Email));

            if (exists)
            {
                return BadRequest(new
                {
                    message = "Username or email already exists."
                });
            }

            var taiKhoan = new TaiKhoan
            {
                TenDangNhap = dto.TenDangNhap,
                Email = dto.Email,
                MatKhau = BCrypt.Net.BCrypt.HashPassword(dto.MatKhau),
                VaiTro = "User",
                TrangThai = true
            };

            await _context.Insert(taiKhoan);


            return Ok(new
            {
                message = "Registration successful."
            });
        }

        // POST: api/Auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            Response.Headers.CacheControl = "no-store";
            // A new login attempt replaces any pending browser handoff.
            if (Request.Cookies.TryGetValue(AdminLoginHandoff.CookieName, out var oldCode))
                _handoff.Consume(oldCode);
            Response.Cookies.Delete(AdminLoginHandoff.CookieName, HandoffCookie(expire: true));
            if (string.IsNullOrWhiteSpace(dto.Identifier) || string.IsNullOrEmpty(dto.MatKhau))
                return BadRequest(new { message = "Please enter your username and password." });
            var user = await _context.TaiKhoans
                .FirstOrDefaultAsync(x =>
                    x.EmailNormalized == MongoStore.Normalize(dto.Identifier) ||
                    x.TenDangNhapNormalized == MongoStore.Normalize(dto.Identifier));

            if (user == null)
            {
                return Unauthorized(new
                {
                    message = "Email or username was not found."
                });
            }

            if (!user.TrangThai)
            {
                return Unauthorized(new
                {
                    message = "This account has been disabled."
                });
            }

            bool passwordValid = BCrypt.Net.BCrypt.Verify(dto.MatKhau, user.MatKhau);

            if (!passwordValid)
            {
                return Unauthorized(new
                {
                    message = "Incorrect password."
                });
            }

            if (AdminRoles.IsAdmin(user.VaiTro))
                Response.Cookies.Append(AdminLoginHandoff.CookieName, _handoff.Issue(user.TaiKhoanID), HandoffCookie());
            var expiresAt = DateTime.UtcNow.AddHours(1);
            return Ok(new
            {
                message = "Login successful.",
                token = _tokens.Create(user, expiresAt),
                expiresAt,
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