using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.GameServers;
using Microsoft.Extensions.Configuration;

public sealed class ProjectZomboidAdapterRegressionTests
{
    private sealed class FakeDriver : ISystemdRuntimeDriver
    {
        public SystemdUnitState State =
            new(true, "active", "running", Environment.ProcessId, "pz-inv");
        public readonly List<SystemdControlAction> Actions = new();

        public Task<SystemdUnitState> GetStateAsync(string unitName, CancellationToken ct = default) =>
            Task.FromResult(State);

        public Task<bool> ControlAsync(string unitName, SystemdControlAction action, CancellationToken ct = default)
        {
            Actions.Add(action);
            State = action == SystemdControlAction.Stop
                ? new(true, "inactive", "dead", 0, "")
                : new(true, "active", "running", Environment.ProcessId, "pz-inv");
            return Task.FromResult(true);
        }
    }

    private sealed class FakeRcon : IRconClient
    {
        public Task<string> SendCommandAsync(string command, CancellationToken ct = default) =>
            Task.FromResult("");
    }

    private static ProjectZomboidAdapter Create(FakeDriver driver)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GameServers:ProjectZomboid:ServiceName"] = "pzserver-game.service",
            })
            .Build();
        return new ProjectZomboidAdapter(driver, config, new FakeRcon());
    }

    private static ServerInstance Instance() => new()
    {
        Type = GameServerType.ProjectZomboid,
        Name = "PZ",
        Password = "",
    };

    [Fact]
    public async Task StartUsesSharedDriverAndReturnsMainPid()
    {
        var driver = new FakeDriver
        {
            State = new(true, "inactive", "dead", 0, ""),
        };
        var adapter = Create(driver);

        var result = await adapter.StartAsync(Instance());

        Assert.True(result.Success);
        Assert.Equal(Environment.ProcessId, result.Snapshot.MainPid);
        Assert.Equal(new[] { SystemdControlAction.Start }, driver.Actions);
    }

    [Fact]
    public async Task StopUsesSharedDriverAndReturnsStoppedSnapshot()
    {
        var driver = new FakeDriver();
        var adapter = Create(driver);

        var result = await adapter.StopAsync(Instance());

        Assert.True(result.Success);
        Assert.Equal(GameServerStatus.Stopped, result.Snapshot.Status);
        Assert.Equal(new[] { SystemdControlAction.Stop }, driver.Actions);
    }
}
