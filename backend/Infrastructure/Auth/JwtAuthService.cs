using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;
using GamePanel.Domain.ValueObjects;
using GamePanel.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace GamePanel.Infrastructure.Auth;

/// <summary>
/// JWT auth với HS256 + local user store (SQLite). Key z cấu hửng "Jwt:Secret".
/// Claims: sub=userId (persisted user Id), role (persisted role), iat, exp.
/// Hasla przechowywane jako hash (IPasswordHasher) — NIGDY plaintext.
/// </summary>
public class JwtAuthService : IAuthService
{
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;

    public JwtAuthService(IConfiguration config, AppDbContext db, IPasswordHasher<User> hasher)
    {
        _config = config;
        _db = db;
        _hasher = hasher;
    }

    private (string Secret, string Issuer, string Audience, int Minutes) Settings()
    {
        var secret = _config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.StartsWith("CHANGE_ME"))
            throw new InvalidOperationException("Jwt:Secret a été configuré (obligatoire, >= 32 caractères).");
        var issuer = _config["Jwt:Issuer"] ?? "GamePanelApi";
        var audience = _config["Jwt:Audience"] ?? "GamePanelClient";
        var minutes = int.TryParse(_config["Jwt:ExpiryMinutes"], out var m) && m > 0 ? m : 30;
        return (secret, issuer, audience, minutes);
    }

    private static SymmetricSecurityKey Key(string secret) =>
        new(Encoding.UTF8.GetBytes(secret));

    private static string Normalize(string username) =>
        username.Trim().ToLowerInvariant();

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
                    ClockSkew = TimeSpan.FromMinutes(5),
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

    public async Task<bool> UsersNeedBootstrapAsync(CancellationToken ct = default)
    {
        return !(await _db.Users.AnyAsync(ct));
    }

    public async Task EnsureBootstrapAdminAsync(string envUsername, string envPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(envUsername) || string.IsNullOrWhiteSpace(envPassword))
            throw new InvalidOperationException("Bootstrap admin requires GAMEPANEL_BOOTSTRAP_ADMIN_USERNAME and GAMEPANEL_BOOTSTRAP_ADMIN_PASSWORD when Users table is empty.");

        if (!await UsersNeedBootstrapAsync(ct))
            return; // idempotent — already has users

        var normalized = Normalize(envUsername);
        var existing = await _db.Users.FirstOrDefaultAsync(x => x.NormalizedUsername == normalized, ct);
        if (existing != null) return; // nie nadpisuj

        var dummy = new User { Id = Guid.NewGuid(), Username = normalized };
        var hash = _hasher.HashPassword(dummy, envPassword);

        _db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = envUsername,
            NormalizedUsername = normalized,
            PasswordHash = hash,
            Role = UserRole.Admin,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<User?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        var normalized = Normalize(username);
        var user = await _db.Users.FirstOrDefaultAsync(x => x.NormalizedUsername == normalized, ct);
        if (user == null)
            return null; // generic — nie ujawniamy czym była 404

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result != PasswordVerificationResult.Success && result != PasswordVerificationResult.SuccessRehashNeeded)
            return null; // generic — złe hasło

        // Rehash upgrade (np. za niski iteration count). Nie ujawnia plaintext.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password);
            await _db.SaveChangesAsync(ct);
        }

        return user;
    }
}