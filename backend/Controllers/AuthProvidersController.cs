using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.Models;
using mood_recommendation.Services;

namespace mood_recommendation.Controllers;

[ApiController,Route("api/Auth")]
public sealed class AuthProvidersController(MongoStore db,PasswordRecoveryService recovery,
    IGoogleIdentityVerifier google,TokenService tokens,IConfiguration config):ControllerBase
{
    [HttpGet("providers")]
    public IActionResult Providers()=>Ok(new {
        passwordReset=recovery.IsConfigured, google=google.IsConfigured,
        googleServerClientId=google.IsConfigured?config["Firebase:GoogleServerClientId"]:null
    });

    [HttpPost("forgot-password"),EnableRateLimiting("recovery")]
    public async Task<IActionResult> Forgot(ForgotPasswordDto dto,CancellationToken ct) {
        if(!recovery.IsConfigured)return StatusCode(503,new{message="Chức năng gửi email chưa sẵn sàng. Hãy thử lại sau."});
        Response.Headers.CacheControl="no-store";
        try {
            var id=await recovery.RequestAsync(dto.Email.Trim(),ct);
            return Ok(new {challengeId=id,expiresInSeconds=600,retryAfterSeconds=60,
                message="Nếu email thuộc tài khoản đang hoạt động, mã xác nhận sẽ được gửi đến hộp thư."});
        } catch(RecoveryThrottledException) {
            Response.Headers.RetryAfter="60";
            return StatusCode(429,new{message="Hãy chờ 60 giây trước khi yêu cầu mã mới.",retryAfterSeconds=60});
        }
    }
    [HttpPost("reset-password"),EnableRateLimiting("recovery")]
    public async Task<IActionResult> Reset(ResetPasswordDto dto,CancellationToken ct) {
        Response.Headers.CacheControl="no-store";
        return await recovery.ResetAsync(dto.ChallengeId,dto.Code,dto.NewPassword,ct)
            ? Ok(new{message="Đã đặt mật khẩu mới. Hãy đăng nhập lại."})
            : BadRequest(new{message="Mã không hợp lệ, đã hết hạn hoặc đã dùng. Hãy yêu cầu mã mới."});
    }
    [HttpPost("google"),EnableRateLimiting("google-login"),RequestSizeLimit(32768)]
    public async Task<IActionResult> Google(GoogleLoginDto dto,CancellationToken ct) {
        Response.Headers.CacheControl="no-store";
        if(!google.IsConfigured)return StatusCode(503,new{message="Đăng nhập Google chưa sẵn sàng. Hãy dùng email."});
        var identity=await google.VerifyAsync(dto.IdToken,ct);
        if(identity is null)return Unauthorized(new{message="Không xác minh được tài khoản Google. Hãy đăng nhập lại."});
        var user=await db.TaiKhoans.FirstOrDefaultAsync(x=>x.GoogleUid==identity.Uid,ct);
        if(user is null) {
            var email=MongoStore.Normalize(identity.Email);
            user=await db.TaiKhoans.FirstOrDefaultAsync(x=>x.EmailNormalized==email,ct);
            if(user is not null) {
                if(!user.TrangThai || user.VaiTro!="User" || user.GoogleUid is not null)
                    return StatusCode(403,new{message="Tài khoản này không thể liên kết bằng Google."});
                if(string.IsNullOrEmpty(dto.CurrentPassword))
                    return Conflict(new{code="google_link_required",message="Email này đã có tài khoản. Nhập mật khẩu Moodify hiện tại để liên kết Google."});
                if(!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword,user.MatKhau))
                    return Unauthorized(new{message="Mật khẩu Moodify chưa đúng."});
                var linked=await db.Collection<TaiKhoan>().UpdateOneAsync(
                    x=>x.TaiKhoanID==user.TaiKhoanID && x.GoogleUid==null && x.TrangThai
                        && x.VaiTro=="User" && x.MatKhau==user.MatKhau,
                    Builders<TaiKhoan>.Update.Set(x=>x.GoogleUid,identity.Uid),cancellationToken:ct);
                if(linked.ModifiedCount!=1)return Conflict(new{message="Tài khoản vừa thay đổi. Hãy đăng nhập lại."});
                user.GoogleUid=identity.Uid;
            } else {
                var name=new string(identity.Name.Where(c=>!char.IsControl(c)).Take(70).ToArray()).Trim();
                if(name.Length<2)name="Moodify";
                var normalized=MongoStore.Normalize(name);
                if(await db.TaiKhoans.AnyAsync(x=>x.TenDangNhapNormalized==normalized,ct))
                    name+="_"+Guid.NewGuid().ToString("N")[..6];
                user=new TaiKhoan {TenDangNhap=name,Email=identity.Email.Trim(),VaiTro="User",TrangThai=true,
                    GoogleUid=identity.Uid,HasPassword=false,
                    MatKhau=BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))};
                try {await db.Insert(user);}
                catch(MongoWriteException e) when(e.WriteError.Category==ServerErrorCategory.DuplicateKey) {
                    return Conflict(new{message="Tài khoản vừa được cập nhật. Hãy thử đăng nhập lại."});
                }
            }
        }
        if(!user.TrangThai || user.VaiTro!="User")
            return StatusCode(403,new{message="Tài khoản không được phép đăng nhập ứng dụng bằng Google."});
        var expiresAt=DateTime.UtcNow.AddHours(1);
        return Ok(new {token=tokens.Create(user,expiresAt),expiresAt,user=new {
            user.TaiKhoanID,user.TenDangNhap,user.Email,user.VaiTro,user.AvatarDataUrl,user.HasPassword
        }});
    }
}
public sealed class ForgotPasswordDto {
    [Required,EmailAddress,StringLength(150)] public string Email {get;set;}="";
}
public sealed class ResetPasswordDto {
    [Required,RegularExpression("^[a-f0-9]{32}$")] public string ChallengeId {get;set;}="";
    [Required,RegularExpression("^[0-9]{6}$")] public string Code {get;set;}="";
    [Required,StringLength(128,MinimumLength=8)] public string NewPassword {get;set;}="";
}
public sealed class GoogleLoginDto {
    [Required,StringLength(16000)] public string IdToken {get;set;}="";
    [StringLength(128)] public string? CurrentPassword {get;set;}
}
