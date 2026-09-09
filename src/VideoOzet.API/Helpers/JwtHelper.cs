using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace VideoOzet.API.Helpers;

public static class JwtHelper
{
    public static string GenerateToken(Guid userId, string email, string fullName, string role, IConfiguration config)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, fullName),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var expirationMinutes = int.Parse(config["Jwt:AccessTokenExpirationMinutes"] ?? "15");

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
    }
}
