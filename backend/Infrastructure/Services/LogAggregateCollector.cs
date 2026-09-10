namespace GamePanel.Infrastructure.Services;
using System.Diagnostics;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>Collects bounded journal windows into LogAggregates without storing raw logs.</summary>
public sealed class LogAggregateCollector : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogAggregateCollector> _logger;
    private readonly TimeSpan _interval;

    public LogAggregateCollector(IServiceScopeFactory scopeFactory, ILogger<LogAggregateCollector> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(Math.Clamp(config.GetValue("Metrics:LogAggregateIntervalMinutes", 5), 1, 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Align to the next complete UTC window; no collection is done on startup.
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var end = FloorWindow(now, _interval);
            var delay = end.Add(_interval) - now;
            await Task.Delay(delay, stoppingToken);
            var windowEnd = FloorWindow(DateTime.UtcNow, _interval);
            var windowStart = windowEnd.Subtract(_interval);
            try { await CollectWindowAsync(windowStart, windowEnd, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { _logger.LogError(ex, "Log aggregate collection failed for {WindowStart}", windowStart); }
        }
    }

    private async Task CollectWindowAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aggregates = scope.ServiceProvider.GetRequiredService<LogAggregateService>();
        var servers = await db.ServerInstances.AsNoTracking()
            .Where(x => x.ProvisioningMode == ProvisioningMode.AdoptExisting && x.RuntimeType == ServerRuntimeType.Systemd && x.RuntimeId != null)
            .ToListAsync(ct);

        foreach (var server in servers)
        {
            var lines = await ReadJournalAsync(server.RuntimeId!, start, end, ct);
            var delta = LogAggregateParser.Parse(lines);
            await aggregates.UpsertAsync(server.Id, start, end, delta, ct);
        }
    }

    private static async Task<IReadOnlyList<string>> ReadJournalAsync(string unit, DateTime start, DateTime end, CancellationToken ct)
    {
        if (!unit.EndsWith(".service", StringComparison.Ordinal) || unit.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("RuntimeId is not an allowlisted systemd unit");

        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/journalctl",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-u"); psi.ArgumentList.Add(unit);
        psi.ArgumentList.Add("--since"); psi.ArgumentList.Add(start.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
        psi.ArgumentList.Add("--until"); psi.ArgumentList.Add(end.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
        psi.ArgumentList.Add("--no-pager"); psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("cat");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start journalctl");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException("journalctl failed");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static DateTime FloorWindow(DateTime value, TimeSpan interval)
    {
        var ticks = value.Ticks - value.Ticks % interval.Ticks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}