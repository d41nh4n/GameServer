using GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GamePanel.Infrastructure.Services;

public sealed class AuditLogRetentionService : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogRetentionService> _logger;

    public AuditLogRetentionService(IServiceScopeFactory scopeFactory, ILogger<AuditLogRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DeleteExpiredAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await DeleteExpiredAsync(stoppingToken);
    }

    private async Task DeleteExpiredAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTime.UtcNow - Retention;
            await db.AuditLogs.Where(x => x.CreatedAtUtc < cutoff).ExecuteDeleteAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Audit log retention cleanup failed"); }
    }
}
