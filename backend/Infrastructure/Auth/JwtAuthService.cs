using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;
using GamePanel.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace GamePanel.Infrastructure.Auth;

/// <summary>
/// JWT auth với HS256. Key từ cấu hình "Jwt:Secret" (không hardcode).
/// Claims: sub=userId, role, iat, exp.
/// </summary>
public class JwtAuthService : IAuthService
{
    private readonly IConfiguration _config;

    public JwtAuthService(IConfiguration config) => _config = config;

    private (string Secret, string Issuer, string Audience, int Minutes) Settings()
    {
        var secret = _config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.StartsWith("CHANGE_ME"))
            throw new InvalidOperationException("Jwt:Secret chưa được cấu hình (bắt buộc, >= 32 ký tự).");
        var issuer = _config["Jwt:Issuer"] ?? "GamePanelApi";
        var audience = _config["Jwt:Audience"] ?? "GamePanelClient";
        var minutes = int.TryParse(_config["Jwt:ExpiryMinutes"], out var m) && m > 0 ? m : 30;
        return (secret, issuer, audience, minutes);
    }

    private static SymmetricSecurityKey Key(string secret) =>
        new(Encoding.UTF8.GetBytes(secret));

    public JwtToken GenerateToken(Guid userId, UserRole role)
    {
        var (secret, issuer, audience, minutes) = Settings();
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(minutes),
            signingCredentials: new SigningCredentials(Key(secret), SecurityAlgorithms.HmacSha256));

        return new JwtToken(new JwtSecurityTokenHandler().WriteToken(token), now.AddMinutes(minutes));
    }

    public User? ValidateToken(string token)
    {
        try
        {
            var (secret, issuer, audience, _) = Settings();
            var principal = new JwtSecurityTokenHandler().ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = Key(secret),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                }, out _);

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(sub, out var id)) return null;

            var role = principal.FindFirst(ClaimTypes.Role)?.Value ?? "User";
            return new User
            {
                Id = id,
                Role = Enum.TryParse<UserRole>(role, true, out var r) ? r : UserRole.User,
            };
        }
        catch (Exception)
        {
            return null; // invalid signature / expired / malformed
        }
    }
}