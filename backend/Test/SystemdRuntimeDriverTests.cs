using GamePanel.Infrastructure.GameServers;

public sealed class SystemdRuntimeDriverTests
{
    private sealed class RecordingRunner : ICommandRunner
    {
        public readonly List<List<string>> Calls = new();
        public readonly Queue<CommandResult> Results = new();

        public Task<CommandResult> RunAsync(List<string> argv, CancellationToken ct = default)
        {
            Calls.Add(new List<string>(argv));
            return Task.FromResult(Results.Dequeue());
        }
    }

    private sealed class ThrowingRunner : ICommandRunner
    {
        public Task<CommandResult> RunAsync(List<string> argv, CancellationToken ct = default) =>
            throw new IOException("runner failed");
    }

    private sealed class BlockingRunner : ICommandRunner
    {
        public async Task<CommandResult> RunAsync(List<string> argv, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new CommandResult();
        }
    }

    [Fact]
    public async Task GetState_ParsesOnlyFixedSystemdProperties()
    {
        var runner = new RecordingRunner();
        runner.Results.Enqueue(new CommandResult
        {
            StdOut = "ActiveState=active\nSubState=running\nMainPID=4242\nInvocationID=abc123\n"
        });
        var driver = new SystemdRuntimeDriver(runner, new[]
        {
            new SystemdUnitDefinition("valheim-main.service", null, false, null, false),
        });

        var state = await driver.GetStateAsync("valheim-main.service");

        Assert.Equal("active", state.ActiveState);
        Assert.Equal("running", state.SubState);
        Assert.Equal(4242, state.MainPid);
        Assert.Equal("abc123", state.InvocationId);
        Assert.Single(runner.Calls);
        Assert.Equal(new[]
        {
            "/usr/bin/systemctl", "show", "valheim-main.service",
            "--property=ActiveState", "--property=SubState",
            "--property=MainPID", "--property=InvocationID", "--no-pager",
        }, runner.Calls[0]);
    }

    [Fact]
    public async Task UnknownUnit_IsRejectedBeforeCommandExecution()
    {
        var runner = new RecordingRunner();
        var driver = new SystemdRuntimeDriver(runner, Array.Empty<SystemdUnitDefinition>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.GetStateAsync("attacker.service"));

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task DisabledControl_FailsClosedWithoutCommandExecution()
    {
        var runner = new RecordingRunner();
        var driver = new SystemdRuntimeDriver(runner, new[]
        {
            new SystemdUnitDefinition("valheim-main.service", null, false, null, false),
        });

        var ok = await driver.ControlAsync("valheim-main.service", SystemdControlAction.Start);

        Assert.False(ok);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task RunnerExceptionFailsClosed()
    {
        var driver = new SystemdRuntimeDriver(new ThrowingRunner(), new[]
        {
            new SystemdUnitDefinition(
                "pzserver-game.service", "/control", false, null, true),
        });

        var state = await driver.GetStateAsync("pzserver-game.service");
        var controlled = await driver.ControlAsync(
            "pzserver-game.service",
            SystemdControlAction.Start);

        Assert.False(state.QuerySucceeded);
        Assert.False(controlled);
    }

    [Fact]
    public async Task InternalTimeoutFailsClosed()
    {
        var driver = new SystemdRuntimeDriver(
            new BlockingRunner(),
            new[]
            {
                new SystemdUnitDefinition(
                    "valheim-main.service", null, false, null, false),
            },
            TimeSpan.FromMilliseconds(20));

        var state = await driver.GetStateAsync("valheim-main.service");

        Assert.False(state.QuerySucceeded);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        var driver = new SystemdRuntimeDriver(
            new BlockingRunner(),
            new[]
            {
                new SystemdUnitDefinition(
                    "valheim-main.service", null, false, null, false),
            },
            TimeSpan.FromMinutes(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            driver.GetStateAsync("valheim-main.service", cts.Token));
    }

    [Fact]
    public async Task ConfiguredWrapper_PreservesProjectZomboidControlBehavior()
    {
        var runner = new RecordingRunner();
        runner.Results.Enqueue(new CommandResult { ExitCode = 0 });
        var driver = new SystemdRuntimeDriver(runner, new[]
        {
            new SystemdUnitDefinition(
                "pzserver-game.service", "/usr/local/sbin/pz-gamectl", true, "", true),
        });

        var ok = await driver.ControlAsync(
            "pzserver-game.service", SystemdControlAction.Stop);

        Assert.True(ok);
        Assert.Equal(
            new[] { "/usr/bin/sudo", "-n", "/usr/local/sbin/pz-gamectl", "stop", "pzserver-game.service" },
            runner.Calls.Single());
    }
}
