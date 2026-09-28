using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver.Linq;
using MongoDB.Driver;
using mood_recommendation.Data;
using mood_recommendation.DTOs;

namespace mood_recommendation.Controllers;

[ApiController, Route("api/[controller]"), Authorize(Roles = "Admin")]
public class UsersController(MongoStore db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] string? search = null)
    {
        if (page < 1 || page > 100000) return BadRequest();
        var query = db.TaiKhoans;
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.TenDangNhap.Contains(search) || x.Email.Contains(search));
        return Ok(new {
            total = await query.CountAsync(), page, pageSize = 50,
            items = await query.OrderBy(x => x.TaiKhoanID).Skip((page - 1) * 50).Take(50)
                .Select(x => new { x.TaiKhoanID, x.TenDangNhap, x.Email, x.VaiTro, x.TrangThai, x.UpdatedAt }).ToListAsync()
        });
    }

    [HttpPut("{id:int}/access")]
    public async Task<IActionResult> Access(int id, UserAccessDto dto)
    {
        if (id.ToString() == User.FindFirstValue(ClaimTypes.NameIdentifier))
            return BadRequest(new { message = "You cannot disable your own account or change your own role." });
        var user = await db.Find<mood_recommendation.Models.TaiKhoan>(id);
        if (user == null) return NotFound();
        user.VaiTro = dto.VaiTro;
        user.TrangThai = dto.TrangThai;
        await db.Update(user);
        return Ok(new { user.TaiKhoanID, user.TenDangNhap, user.Email, user.VaiTro, user.TrangThai });
    }
}
