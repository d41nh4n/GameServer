namespace GamePanel.Infrastructure.Services;
using GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public sealed class GlobalMetricsService
{
    private readonly AppDbContext _db;
    public GlobalMetricsService(AppDbContext db) => _db = db;

    public async Task<GlobalMetricsSnapshot> GetAsync(CancellationToken ct = default)
    {
        var servers = await _db.ServerInstances.AsNoTracking().ToListAsync(ct);
        var since = DateTime.UtcNow.AddHours(-24);
        var aggregates = await _db.LogAggregates.AsNoTracking()
            .Where(x => x.WindowEndUtc > since)
            .ToListAsync(ct);

        return new GlobalMetricsSnapshot
        {
            TotalServers = servers.Count,
            RunningServers = servers.Count(x => x.Status == Domain.Entities.ServerStatus.Running),
            ReadyServers = servers.Count(x => x.Ready),
            StartingServers = servers.Count(x => x.Status == Domain.Entities.ServerStatus.Starting),
            StoppedServers = servers.Count(x => x.Status == Domain.Entities.ServerStatus.Stopped),
            WarningCount24h = aggregates.Sum(x => x.WarningCount),
            ErrorCount24h = aggregates.Sum(x => x.ErrorCount),
            FatalCount24h = aggregates.Sum(x => x.FatalCount),
            PlayerJoins24h = aggregates.Sum(x => x.PlayerJoinCount),
            PlayerLeaves24h = aggregates.Sum(x => x.PlayerLeaveCount),
            LastErrorMessage = aggregates
                .Where(x => !string.IsNullOrWhiteSpace(x.LastErrorMessage))
                .OrderByDescending(x => x.WindowEndUtc)
                .Select(x => x.LastErrorMessage)
                .FirstOrDefault(),
            WindowStartUtc = since,
            GeneratedAtUtc = DateTime.UtcNow,
        };
    }
}

public sealed record GlobalMetricsSnapshot
{
    public int TotalServers { get; init; }
    public int RunningServers { get; init; }
    public int ReadyServers { get; init; }
    public int StartingServers { get; init; }
    public int StoppedServers { get; init; }
    public int WarningCount24h { get; init; }
    public int ErrorCount24h { get; init; }
    public int FatalCount24h { get; init; }
    public int PlayerJoins24h { get; init; }
    public int PlayerLeaves24h { get; init; }
    public string? LastErrorMessage { get; init; }
    public DateTime WindowStartUtc { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
}