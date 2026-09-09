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
    {
        var servers = await _db.ServerInstances.OrderBy(x => x.Name).ToListAsync();
        // Reconcile: nie zgłaszaj Running tylko dlatego, że DB ma stary PID po restarcie.
        // Źródłem prawdy jest adapter (systemd /proc dla PZ, /proc dla Valheim).
        foreach (var s in servers)
        {
            if (s.Status != ServerStatus.Running || s.ProcessId == null)
            {
                continue;
            }
            try
            {
                if (s.ProcessId is not { } pidInt)
                {
                    continue;
                }
                var adapter = _factory.Create(s);
                var st = await adapter.GetStatusAsync(pidInt);
                if (st == GameServerStatus.Stopped)
                {
                    s.ProcessId = null;
                    s.Status = ServerStatus.Stopped;
                    await _db.SaveChangesAsync();
                    await _hub.Clients.All.SendAsync("ServerStateChanged", s.Id, (int)ServerStatus.Stopped);
                }
            }
            catch
            {
                // brak zmian przy tym odczycie — system tymczasowo nieosiągalny
            }
        }
        return await _db.ServerInstances.OrderBy(x => x.Name).ToListAsync();
    }

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
            if (!ok)
            {
                return false;
            }

            s.ProcessId = pid;
            s.Status = ServerStatus.Running;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync("ServerStateChanged", id, (int)ServerStatus.Running);
            return true;
        }
        catch (Exception)
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