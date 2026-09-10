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

    public GameServerManager(AppDbContext db, IGameServerAdapterFactory factory, IHubContext<ServerHub> hub)
    {
        _db = db;
        _factory = factory;
        _hub = hub;
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
                return false;
            }

            server.Status = ServerStatus.Starting;
            server.Ready = false;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync(
                "ServerStateChanged",
                id,
                (int)ServerStatus.Starting);

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
                return false;
            }

            server.Status = ServerStatus.Stopping;
            server.Ready = false;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync(
                "ServerStateChanged",
                id,
                (int)ServerStatus.Stopping);

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