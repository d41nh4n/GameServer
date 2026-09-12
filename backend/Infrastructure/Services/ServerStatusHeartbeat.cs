using GamePanel.Application.Interfaces;
using GamePanel.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GamePanel.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GamePanel.Infrastructure.Services;

/// <summary>Polls every runtime and persists the authoritative observed state.</summary>
public sealed class ServerStatusHeartbeat : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ServerStatusHeartbeat> _logger;
    private readonly IHubContext<ServerHub> _hub;
    private bool _snapshotPersistenceAvailable = true;
    private readonly Dictionary<Guid, string> _lastStatusKeys = new();
    public ServerStatusHeartbeat(IServiceScopeFactory scopeFactory, ILogger<ServerStatusHeartbeat> logger, IHubContext<ServerHub> hub)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PollAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await PollAsync(stoppingToken);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runtime = scope.ServiceProvider.GetRequiredService<GameServerManager>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var servers = (await runtime.RefreshActiveAsync(ct)).ToList();
            var now = DateTime.UtcNow;
            foreach (var server in servers)
            {
                var statusKey = $"{server.Status}:{server.Ready}:{server.ProcessId}";
                if (!_lastStatusKeys.TryGetValue(server.Id, out var previousKey) || previousKey != statusKey)
                {
                    _lastStatusKeys[server.Id] = statusKey;
                    _logger.LogInformation("Server status heartbeat changed {MetricEvent} {MetricType} {ServerId} {Status} {Ready} {ProcessId} {ObservedAtUtc}", true, "status_heartbeat", server.Id, server.Status, server.Ready, server.ProcessId, now);
                }
                if (_snapshotPersistenceAvailable)
                {
                    try
                    {
                        var snapshot = await db.ServerStatusSnapshots.FirstOrDefaultAsync(x => x.ServerInstanceId == server.Id, ct);
                        if (snapshot is null)
                        {
                            snapshot = new Domain.Entities.ServerStatusSnapshot { ServerInstanceId = server.Id };
                            db.ServerStatusSnapshots.Add(snapshot);
                        }
                        snapshot.Status = server.Status;
                        snapshot.ProcessId = server.ProcessId;
                        snapshot.Ready = server.Ready;
                        snapshot.ObservedAtUtc = now;
                        snapshot.Source = "heartbeat";
                    }
                    catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("ServerStatusSnapshots", StringComparison.Ordinal))
                    {
                        _snapshotPersistenceAvailable = false;
                        _logger.LogWarning("ServerStatusSnapshots table is not migrated; continuing realtime heartbeat without persistence");
                    }
                }
                await _hub.Clients.All.SendAsync("ServerHeartbeat", server.Id, (int)server.Status, server.Ready, server.ProcessId, now, ct);
            }
            if (_snapshotPersistenceAvailable) await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Server heartbeat poll failed"); }
    }
}
