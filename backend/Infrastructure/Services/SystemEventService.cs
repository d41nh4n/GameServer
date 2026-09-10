namespace GamePanel.Infrastructure.Services;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public static class SystemEventTypes
{
    public const string StateChanged = "STATE_CHANGED";
    public const string ReadinessChanged = "READINESS_CHANGED";
    public const string CrashDetected = "CRASH_DETECTED";
    public const string BackupFailed = "BACKUP_FAILED";
}

public static class SystemEventSeverity
{
    public const string Info = "INFO";
    public const string Warning = "WARNING";
    public const string Error = "ERROR";
    public const string Critical = "CRITICAL";
}

public sealed class SystemEventService
{
    private readonly AppDbContext _db;
    public SystemEventService(AppDbContext db) => _db = db;

    public async Task RecordAsync(
        Guid serverInstanceId,
        string eventType,
        string severity,
        string message,
        ServerStatus? previousStatus = null,
        ServerStatus? currentStatus = null,
        int? mainPid = null,
        CancellationToken ct = default)
    {
        _db.SystemEvents.Add(new SystemEvent
        {
            CreatedAtUtc = DateTime.UtcNow,
            ServerInstanceId = serverInstanceId,
            EventType = eventType,
            Severity = severity,
            Message = message.Length > 255 ? message[..255] : message,
            PreviousStatus = previousStatus.HasValue ? (int)previousStatus.Value : null,
            CurrentStatus = currentStatus.HasValue ? (int)currentStatus.Value : null,
            MainPid = mainPid,
        });
        await _db.SaveChangesAsync(ct);
    }

    public IQueryable<SystemEvent> Query(Guid? serverId = null)
    {
        var query = _db.SystemEvents.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).AsQueryable();
        return serverId.HasValue
            ? query.Where(x => x.ServerInstanceId == serverId.Value).OrderByDescending(x => x.CreatedAtUtc)
            : query;
    }
}