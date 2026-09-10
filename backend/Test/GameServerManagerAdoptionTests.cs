using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.Data;
using GamePanel.Infrastructure.GameServers;
using GamePanel.Infrastructure.Hubs;
using GamePanel.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class GameServerManagerAdoptionTests : IDisposable
{
    private sealed class FakeAdapter : IGameServerAdapter
    {
        public GameServerRuntimeSnapshot Inspection =
            new(GameServerStatus.Stopped, null, false);
        public GameServerActionResult StartResult =
            new(false, new(GameServerStatus.Stopped, null, false));
        public GameServerActionResult StopResult =
            new(false, new(GameServerStatus.Running, 4242, true));
        public int StartCalls;
        public int StopCalls;

        public Task<GameServerRuntimeSnapshot> InspectAsync(ServerInstance instance, CancellationToken ct = default) =>
            Task.FromResult(Inspection);

        public Task<GameServerActionResult> StartAsync(ServerInstance instance, CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(StartResult);
        }

        public Task<GameServerActionResult> StopAsync(ServerInstance instance, CancellationToken ct = default)
        {
            StopCalls++;
            return Task.FromResult(StopResult);
        }
    }

    private sealed class FakeFactory : IGameServerAdapterFactory
    {
        private readonly IGameServerAdapter _adapter;
        public FakeFactory(IGameServerAdapter adapter) => _adapter = adapter;
        public IGameServerAdapter Create(ServerInstance instance) => _adapter;
    }

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        "gamepanel-manager-adoption-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly List<ServiceProvider> _providers = new();

    [Fact]
    public async Task GetAll_ReconcilesAdoptedServiceRunningWhenDatabaseWasStopped()
    {
        var adapter = new FakeAdapter
        {
            Inspection = new(GameServerStatus.Running, 4242, true),
        };
        await using var db = await CreateDb();
        var server = Instance(ServerStatus.Stopped);
        db.ServerInstances.Add(server);
        await db.SaveChangesAsync();
        var manager = CreateManager(db, adapter);

        var result = (await manager.GetAllAsync()).Single();

        Assert.Equal(ServerStatus.Running, result.Status);
        Assert.Equal(4242, result.ProcessId);
        Assert.True(result.Ready);
    }

    [Fact]
    public async Task FailedStart_DoesNotRemainStarting()
    {
        var adapter = new FakeAdapter();
        await using var db = await CreateDb();
        var server = Instance(ServerStatus.Stopped);
        db.ServerInstances.Add(server);
        await db.SaveChangesAsync();
        var manager = CreateManager(db, adapter);

        var ok = await manager.StartAsync(server.Id);

        Assert.False(ok);
        Assert.Equal(1, adapter.StartCalls);
        Assert.Equal(ServerStatus.Stopped, server.Status);
        Assert.False(server.Ready);
    }

    [Fact]
    public async Task FailedStop_DoesNotClaimStopped()
    {
        var adapter = new FakeAdapter
        {
            Inspection = new(GameServerStatus.Running, 4242, true),
            StopResult = new(false, new(GameServerStatus.Running, 4242, true)),
        };
        await using var db = await CreateDb();
        var server = Instance(ServerStatus.Running);
        server.ProcessId = 4242;
        server.Ready = true;
        db.ServerInstances.Add(server);
        await db.SaveChangesAsync();
        var manager = CreateManager(db, adapter);

        var ok = await manager.StopAsync(server.Id);

        Assert.False(ok);
        Assert.Equal(1, adapter.StopCalls);
        Assert.Equal(ServerStatus.Running, server.Status);
        Assert.Equal(4242, server.ProcessId);
    }

    private async Task<AppDbContext> CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=" + _dbPath)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private GameServerManager CreateManager(
        AppDbContext db,
        IGameServerAdapter adapter)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var hub = provider.GetRequiredService<IHubContext<ServerHub>>();
        return new GameServerManager(db, new FakeFactory(adapter), hub);
    }

    private static ServerInstance Instance(ServerStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Valheim Main",
        GameType = "Valheim",
        Type = GameServerType.Valheim,
        Status = status,
        Password = "",
        InstanceKey = "valheim-main",
        ProvisioningMode = ProvisioningMode.AdoptExisting,
        RuntimeType = ServerRuntimeType.Systemd,
        RuntimeId = "valheim-main.service",
    };

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
