namespace GamePanel.Infrastructure.Services;
using GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using GamePanel.Infrastructure.GameServers;
using GamePanel.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

public class GameServerManager : IGameServerRuntime
{
    private readonly AppDbContext _db;
    private readonly IGameServerAdapterFactory _factory;
    private readonly IHubContext<ServerHub> _hub;
    private readonly SystemEventService _events;

    public GameServerManager(AppDbContext db, IGameServerAdapterFactory factory, IHubContext<ServerHub> hub)
        : this(db, factory, hub, new SystemEventService(db))
    {
    }

    public GameServerManager(AppDbContext db, IGameServerAdapterFactory factory, IHubContext<ServerHub> hub, SystemEventService events)
    {
        _db = db;
        _factory = factory;
        _hub = hub;
        _events = events;
    }

    public async Task<IEnumerable<ServerInstance>> GetAllAsync(CancellationToken ct = default)
    {
        var servers = await _db.ServerInstances.OrderBy(x => x.Name).ToListAsync(ct);
        var changed = false;
        foreach (var server in servers)
        {
            try
            {
                var snapshot = await _factory.Create(server).InspectAsync(server);
                if (snapshot.Status == GameServerStatus.Unknown) continue;

                var previousStatus = server.Status;
                changed |= ApplySnapshot(server, snapshot);
                if (server.Status != previousStatus)
                {
                    await _hub.Clients.All.SendAsync(
                        "ServerStateChanged",
                        server.Id,
                        (int)server.Status);
                    await _events.RecordAsync(
                        server.Id,
                        SystemEventTypes.StateChanged,
                        ServerStatusSeverity(server.Status),
                        $"Server state changed from {previousStatus} to {server.Status}",
                        previousStatus,
                        server.Status,
                        server.ProcessId,
                        ct);
                }
            }
            catch
            {
                // Preserve the last known state when runtime inspection is unavailable.
            }
        }

        if (changed) await _db.SaveChangesAsync();
        return servers;
    }

    public async Task<bool> StartAsync(Guid id, CancellationToken ct = default)
    {
        var server = await _db.ServerInstances.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (server == null) return false;

        try
        {
            var adapter = _factory.Create(server);
            var current = await adapter.InspectAsync(server);
            if (current.Status == GameServerStatus.Running)
            {
                ApplySnapshot(server, current);
                await _db.SaveChangesAsync();
                return true;
            }

            var previousStatus = server.Status;
            server.Status = ServerStatus.Starting;
            server.Ready = false;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync(
                "ServerStateChanged",
                id,
                (int)ServerStatus.Starting);
            await _events.RecordAsync(id, SystemEventTypes.StateChanged, SystemEventSeverity.Info,
                "Start requested", previousStatus, ServerStatus.Starting, server.ProcessId, ct);

            var result = await adapter.StartAsync(server);
            var fallback = new GameServerRuntimeSnapshot(
                GameServerStatus.Stopped,
                null,
                false);
            ApplySnapshot(
                server,
                result.Snapshot.Status == GameServerStatus.Unknown
                    ? fallback
                    : result.Snapshot);
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync(
                "ServerStateChanged",
                id,
                (int)server.Status);
            await _events.RecordAsync(id, SystemEventTypes.StateChanged, ServerStatusSeverity(server.Status),
                $"Start completed with state {server.Status}", ServerStatus.Starting, server.Status, server.ProcessId, ct);
            return result.Success;
        }
        catch
        {
            server.Status = ServerStatus.Stopped;
            server.ProcessId = null;
            server.Ready = false;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync(
                "ServerStateChanged",
                id,
                (int)ServerStatus.Stopped);
            return false;
        }
    }

    public async Task<bool> StopAsync(Guid id, CancellationToken ct = default)
    {
        var server = await _db.ServerInstances.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (server == null) return false;

        try
        {
            var adapter = _factory.Create(server);
            var current = await adapter.InspectAsync(server);
            if (current.Status == GameServerStatus.Stopped)
            {
                ApplySnapshot(server, current);
                await _db.SaveChangesAsync();
                return true;
            }

            var previousStatus = server.Status;
            server.Status = ServerStatus.Stopping;
            server.Ready = false;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync(
                "ServerStateChanged",
                id,
                (int)ServerStatus.Stopping);
            await _events.RecordAsync(id, SystemEventTypes.StateChanged, SystemEventSeverity.Info,
                "Stop requested", previousStatus, ServerStatus.Stopping, server.ProcessId, ct);

            var result = await adapter.StopAsync(server);
            ApplySnapshot(
                server,
                result.Snapshot.Status == GameServerStatus.Unknown
                    ? current
                    : result.Snapshot);
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync(
                "ServerStateChanged",
                id,
                (int)server.Status);
            await _events.RecordAsync(id, SystemEventTypes.StateChanged, ServerStatusSeverity(server.Status),
                $"Stop completed with state {server.Status}", ServerStatus.Stopping, server.Status, server.ProcessId, ct);
            return result.Success;
        }
        catch
        {
            server.Status = ServerStatus.Running;
            server.Ready = false;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync(
                "ServerStateChanged",
                id,
                (int)ServerStatus.Running);
            return false;
        }
    }

    private static string ServerStatusSeverity(ServerStatus status) =>
        status is ServerStatus.Stopped or ServerStatus.Running
            ? SystemEventSeverity.Info
            : SystemEventSeverity.Warning;

    private static bool ApplySnapshot(
        ServerInstance server,
        GameServerRuntimeSnapshot snapshot)
    {
        var status = snapshot.Status switch
        {
            GameServerStatus.Stopped => ServerStatus.Stopped,
            GameServerStatus.Starting => ServerStatus.Starting,
            GameServerStatus.Running => ServerStatus.Running,
            GameServerStatus.Stopping => ServerStatus.Stopping,
            _ => server.Status,
        };
        var changed =
            server.Status != status ||
            server.ProcessId != snapshot.MainPid ||
            server.Ready != snapshot.Ready;
        server.Status = status;
        server.ProcessId = snapshot.MainPid;
        server.Ready = snapshot.Ready;
        return changed;
    }
}