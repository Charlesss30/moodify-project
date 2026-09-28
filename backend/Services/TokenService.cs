using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using mood_recommendation.Models;
namespace mood_recommendation.Services;
public sealed class TokenService(string key)
{
    public const string Issuer = "Moodify";
    public const string Audience = "MoodifyClients";
    public string Create(TaiKhoan user, DateTime expiresAt) => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken(Issuer, Audience,
            [new Claim(ClaimTypes.NameIdentifier, user.TaiKhoanID.ToString()),
             new Claim(ClaimTypes.Role, AdminRoles.IsAdmin(user.VaiTro) ? AdminRoles.Name : user.VaiTro),
             new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            expires: expiresAt,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
}
