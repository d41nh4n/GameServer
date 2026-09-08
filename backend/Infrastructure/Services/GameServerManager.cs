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

    public async Task<IEnumerable<ServerInstance>> GetAllAsync()
        => await _db.ServerInstances.OrderBy(x => x.Name).ToListAsync();

    public async Task<bool> StartAsync(Guid id)
    {
        var s = await _db.ServerInstances.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null || s.Status == ServerStatus.Running) return false;

        s.Status = ServerStatus.Starting;
        await _db.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("ServerStateChanged", id, (int)ServerStatus.Starting);

        try
        {
            var adapter = _factory.Create(s);
            var (ok, pid) = await adapter.StartAsync(s);
            if (!ok) return false;

            s.ProcessId = pid;
            s.Status = ServerStatus.Running;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync("ServerStateChanged", id, (int)ServerStatus.Running);
            return true;
        }
        catch
        {
            s.Status = ServerStatus.Stopped;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync("ServerStateChanged", id, (int)ServerStatus.Stopped);
            return false;
        }
    }

    public async Task<bool> StopAsync(Guid id)
    {
        var s = await _db.ServerInstances.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null || s.Status == ServerStatus.Stopped || s.ProcessId is not { } pid) return false;

        s.Status = ServerStatus.Stopping;
        await _db.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("ServerStateChanged", id, (int)ServerStatus.Stopping);

        try
        {
            var adapter = _factory.Create(s);
            await adapter.StopAsync(pid);

            s.ProcessId = null;
            s.Status = ServerStatus.Stopped;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync("ServerStateChanged", id, (int)ServerStatus.Stopped);
            return true;
        }
        catch
        {
            s.Status = ServerStatus.Running;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync("ServerStateChanged", id, (int)ServerStatus.Running);
            return false;
        }
    }
}