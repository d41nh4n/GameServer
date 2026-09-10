namespace GamePanel.Infrastructure.Services;
using System.Security.Claims;
using System.Text.Json;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public static class AuditActions
{
    public const string StartServer = "START_SERVER";
    public const string StopServer = "STOP_SERVER";
    public const string UpdateConfig = "UPDATE_CONFIG";
    public const string ExecuteRcon = "EXEC_RCON";
    public const string CreateBackup = "CREATE_BACKUP";
    public const string AddMember = "ADD_MEMBER";
    public const string RemoveMember = "REMOVE_MEMBER";
}

public sealed class AuditLogService
{
    private readonly AppDbContext _db;
    public AuditLogService(AppDbContext db) => _db = db;

    public async Task RecordAsync(
        ClaimsPrincipal principal,
        string action,
        Guid? serverInstanceId,
        bool success,
        string? resultCode = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        var subject = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userId = Guid.TryParse(subject, out var parsedId) ? parsedId : (Guid?)null;
        var username = principal.Identity?.Name
            ?? principal.FindFirst(ClaimTypes.Name)?.Value
            ?? principal.FindFirst("username")?.Value
            ?? (userId.HasValue ? userId.Value.ToString() : "system");

        if (userId.HasValue)
        {
            var storedUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId.Value, ct);
            if (storedUser != null) username = storedUser.Username;
        }

        // Metadata is caller-controlled but must never contain credentials/tokens.
        var metadataJson = metadata == null ? null : JsonSerializer.Serialize(metadata);
        if (metadataJson != null && ContainsSecretKey(metadataJson))
            throw new InvalidOperationException("Audit metadata contains a prohibited secret field");

        _db.AuditLogs.Add(new AuditLog
        {
            CreatedAtUtc = DateTime.UtcNow,
            UserId = userId,
            UsernameSnapshot = username,
            ServerInstanceId = serverInstanceId,
            Action = action,
            IsSuccess = success,
            ResultCode = resultCode,
            MetadataJson = metadataJson,
        });
        await _db.SaveChangesAsync(ct);
    }

    private static bool ContainsSecretKey(string json) =>
        json.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        json.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        json.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        json.Contains("jwt", StringComparison.OrdinalIgnoreCase);
}