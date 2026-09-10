using GamePanel.Domain.Entities;
using GamePanel.Infrastructure.GameServers;

public sealed class ValheimRuntimeStrategyTests
{
    private sealed class FakeDriver : ISystemdRuntimeDriver
    {
        public SystemdUnitState State = new(true, "active", "running", 4242, "inv-1");
        public int StateCalls;
        public int ControlCalls;

        public Task<SystemdUnitState> GetStateAsync(string unitName, CancellationToken ct = default)
        {
            StateCalls++;
            return Task.FromResult(State);
        }

        public Task<bool> ControlAsync(string unitName, SystemdControlAction action, CancellationToken ct = default)
        {
            ControlCalls++;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeProbe : IValheimRuntimeProbe
    {
        public bool Cgroup = true;
        public bool Ports = true;
        public bool Marker = true;

        public Task<bool> IsProcessInUnitAsync(int pid, string unitName, CancellationToken ct = default) =>
            Task.FromResult(Cgroup);

        public Task<bool> AreUdpPortsOwnedAsync(int pid, int firstPort, int secondPort, CancellationToken ct = default) =>
            Task.FromResult(Ports);

        public Task<bool> HasReadinessMarkerAsync(string unitName, string invocationId, string marker, CancellationToken ct = default) =>
            Task.FromResult(Marker);
    }

    private sealed class NoCallRunner : ICommandRunner
    {
        public int Calls;

        public Task<CommandResult> RunAsync(List<string> argv, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new CommandResult());
        }
    }

    private static ServerInstance AdoptedInstance() => new()
    {
        InstanceKey = "valheim-main",
        ProvisioningMode = ProvisioningMode.AdoptExisting,
        RuntimeType = ServerRuntimeType.Systemd,
        RuntimeId = "valheim-main.service",
        Port = 2456,
        ReadinessMarker = "Game server connected",
    };

    [Fact]
    public async Task CurrentInvocationWithCgroupPortsAndMarker_IsReady()
    {
        var driver = new FakeDriver();
        var strategy = new ValheimRuntimeStrategy(driver, new FakeProbe());

        var snapshot = await strategy.InspectAsync(AdoptedInstance());

        Assert.Equal(GameServerStatus.Running, snapshot.Status);
        Assert.Equal(4242, snapshot.MainPid);
        Assert.True(snapshot.Ready);
    }

    [Fact]
    public async Task MissingCurrentInvocationMarker_IsStartingNotReady()
    {
        var driver = new FakeDriver();
        var probe = new FakeProbe { Marker = false };
        var strategy = new ValheimRuntimeStrategy(driver, probe);

        var snapshot = await strategy.InspectAsync(AdoptedInstance());

        Assert.Equal(GameServerStatus.Starting, snapshot.Status);
        Assert.False(snapshot.Ready);
    }

    [Fact]
    public async Task WrongCgroupOrMissingPorts_FailsClosed()
    {
        var driver = new FakeDriver();
        var probe = new FakeProbe { Cgroup = false };
        var strategy = new ValheimRuntimeStrategy(driver, probe);

        var snapshot = await strategy.InspectAsync(AdoptedInstance());

        Assert.Equal(GameServerStatus.Starting, snapshot.Status);
        Assert.False(snapshot.Ready);
    }

    [Fact]
    public async Task LegacyManagedProcessRecordNeverCallsSystemdOrControl()
    {
        var driver = new FakeDriver();
        var strategy = new ValheimRuntimeStrategy(driver, new FakeProbe());
        var provider = new ValheimProvider(strategy, driver);
        var legacy = AdoptedInstance();
        legacy.ProvisioningMode = ProvisioningMode.Managed;
        legacy.RuntimeType = ServerRuntimeType.Process;
        legacy.RuntimeId = null;

        var inspection = await provider.InspectAsync(legacy);
        var start = await provider.StartAsync(legacy);
        var stop = await provider.StopAsync(legacy);

        Assert.Equal(GameServerStatus.Unknown, inspection.Status);
        Assert.False(start.Success);
        Assert.False(stop.Success);
        Assert.Equal(0, driver.StateCalls);
        Assert.Equal(0, driver.ControlCalls);
    }

    [Fact]
    public async Task RuntimeIdOutsideAllowlistFailsClosedWithoutCommand()
    {
        var runner = new NoCallRunner();
        var driver = new SystemdRuntimeDriver(runner, new[]
        {
            new SystemdUnitDefinition("valheim-main.service", null, false, null, false),
        });
        var strategy = new ValheimRuntimeStrategy(driver, new FakeProbe());
        var instance = AdoptedInstance();
        instance.RuntimeId = "attacker.service";

        var snapshot = await strategy.InspectAsync(instance);

        Assert.Equal(GameServerStatus.Unknown, snapshot.Status);
        Assert.False(snapshot.Ready);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task ValheimProvider_AdoptedStartStopInvokeSystemdControl()
    {
        var driver = new FakeDriver();
        var strategy = new ValheimRuntimeStrategy(driver, new FakeProbe());
        var provider = new ValheimProvider(strategy, driver);
        var instance = AdoptedInstance();

        var start = await provider.StartAsync(instance);
        var stop = await provider.StopAsync(instance);

        Assert.True(start.Success);
        Assert.True(stop.Success);
        Assert.Equal(2, driver.ControlCalls);
    }

    [Fact]
    public async Task ValheimProvider_NonAdoptedStartStopFailClosedWithoutControl()
    {
        var driver = new FakeDriver();
        var strategy = new ValheimRuntimeStrategy(driver, new FakeProbe());
        var provider = new ValheimProvider(strategy, driver);
        var instance = AdoptedInstance();
        instance.ProvisioningMode = ProvisioningMode.Managed;
        instance.RuntimeType = ServerRuntimeType.Process;
        instance.RuntimeId = null;

        var start = await provider.StartAsync(instance);
        var stop = await provider.StopAsync(instance);

        Assert.False(start.Success);
        Assert.False(stop.Success);
        Assert.Equal(0, driver.ControlCalls);
    }
}
