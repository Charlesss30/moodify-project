using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;
namespace mood_recommendation.Controllers;
[ApiController, Route("api/Account"), Authorize]
public sealed class AccountController(MongoStore db) : ControllerBase
{
 int AccountId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 [HttpGet("profile")] public async Task<IActionResult> Profile() { var u=await db.Find<TaiKhoan>(AccountId); return u is null?NotFound():Ok(new {u.TaiKhoanID,u.TenDangNhap,u.Email,u.HasPassword,u.PreferredContentType,u.MoodGoal,u.AvatarDataUrl}); }
 [HttpPut("profile")] public async Task<IActionResult> UpdateProfile(UpdateAccountProfileDto dto) {
  var name=dto.TenDangNhap?.Trim()??""; var email=dto.Email?.Trim()??"";
  if(name.Length is <2 or >100)return BadRequest(new{message="Tên hiển thị cần từ 2 đến 100 ký tự."});
  if(email.Length is <3 or >150||!MailAddress.TryCreate(email,out _))return BadRequest(new{message="Email chưa đúng định dạng."});
  var u=await db.Find<TaiKhoan>(AccountId);if(u is null)return NotFound();
  if(!string.Equals(email,u.Email,StringComparison.OrdinalIgnoreCase)&&(string.IsNullOrEmpty(dto.CurrentPassword)||!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword,u.MatKhau)))return Unauthorized(new{message="Mật khẩu hiện tại chưa đúng."});
  if(dto.AvatarDataUrl is { Length: > 1_500_000 } || (dto.AvatarDataUrl is not null && !dto.AvatarDataUrl.StartsWith("data:image/",StringComparison.Ordinal)))return BadRequest(new{message="Ảnh đại diện không hợp lệ hoặc vượt quá dung lượng cho phép."});
  var ne=MongoStore.Normalize(email);var nn=MongoStore.Normalize(name);
  if(await db.TaiKhoans.AnyAsync(x=>x.TaiKhoanID!=u.TaiKhoanID&&(x.EmailNormalized==ne||x.TenDangNhapNormalized==nn)))return Conflict(new{message="Tên đăng nhập hoặc email đã được sử dụng."});
  u.TenDangNhap=name;u.Email=email;if(dto.AvatarDataUrl is not null)u.AvatarDataUrl=dto.AvatarDataUrl;await db.Update(u);
  return Ok(new{u.TaiKhoanID,u.TenDangNhap,u.Email,u.HasPassword,u.PreferredContentType,u.MoodGoal,u.AvatarDataUrl});
 }
 [HttpPut("preferences")] public async Task<IActionResult> UpdatePreferences(UpdateAccountPreferencesDto dto) {
  if(dto.PreferredContentType is not ("Both" or "Music" or "Movie"))return BadRequest(new{message="Lựa chọn nội dung không hợp lệ."});
  if(dto.MoodGoal is not ("balance" or "relax" or "energy" or "focus"))return BadRequest(new{message="Mục tiêu tâm trạng không hợp lệ."});
  var u=await db.Find<TaiKhoan>(AccountId);if(u is null)return NotFound();u.PreferredContentType=dto.PreferredContentType;u.MoodGoal=dto.MoodGoal;await db.Update(u);
  return Ok(new{u.TaiKhoanID,u.TenDangNhap,u.Email,u.VaiTro,u.PreferredContentType,u.MoodGoal,u.AvatarDataUrl});
 }
 [HttpPut("password")] public async Task<IActionResult> ChangePassword(ChangeAccountPasswordDto dto) {
  if(string.IsNullOrEmpty(dto.CurrentPassword)||string.IsNullOrEmpty(dto.NewPassword))return BadRequest(new{message="Hãy nhập mật khẩu hiện tại và mật khẩu mới."});
  if(dto.NewPassword.Length<8||dto.NewPassword.Length>128)return BadRequest(new{message="Mật khẩu mới cần từ 8 đến 128 ký tự."});
  if(dto.CurrentPassword==dto.NewPassword)return BadRequest(new{message="Mật khẩu mới cần khác mật khẩu hiện tại."});
  var u=await db.Find<TaiKhoan>(AccountId);if(u is null)return NotFound();
  if(!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword,u.MatKhau))return Unauthorized(new{message="Mật khẩu hiện tại chưa đúng."});
  u.MatKhau=BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);await db.Update(u);return Ok(new{message="Đã đổi mật khẩu thành công."});
 }
}
public sealed class UpdateAccountProfileDto { public string? TenDangNhap{get;set;} public string? Email{get;set;} public string? CurrentPassword{get;set;} public string? AvatarDataUrl{get;set;} }
public sealed class UpdateAccountPreferencesDto { public string PreferredContentType{get;set;}="Both"; public string MoodGoal{get;set;}="balance"; }
public sealed class ChangeAccountPasswordDto { public string? CurrentPassword{get;set;} public string? NewPassword{get;set;} }


