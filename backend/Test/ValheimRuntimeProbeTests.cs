using GamePanel.Infrastructure.GameServers;

public sealed class ValheimRuntimeProbeTests
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

    private sealed class BlockingRunner : ICommandRunner
    {
        public async Task<CommandResult> RunAsync(List<string> argv, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new CommandResult();
        }
    }

    [Fact]
    public async Task ProbesCgroupPortsAndCurrentInvocationWithoutProcessArguments()
    {
        var runner = new RecordingRunner();
        runner.Results.Enqueue(new CommandResult
        {
            StdOut = "0::/system.slice/valheim-main.service\n",
        });
        runner.Results.Enqueue(new CommandResult
        {
            StdOut =
                "UNCONN 0 0 *:2456 *:*\n" +
                "UNCONN 0 0 0.0.0.0:2457 0.0.0.0:*\n",
        });
        runner.Results.Enqueue(new CommandResult
        {
            StdOut = "Game server connected\n",
        });
        var probe = new ValheimRuntimeProbe(runner);

        Assert.True(await probe.IsProcessInUnitAsync(
            4242,
            "valheim-main.service"));
        Assert.True(await probe.AreUdpPortsOwnedAsync(4242, 2456, 2457));
        Assert.True(await probe.HasReadinessMarkerAsync(
            "valheim-main.service",
            "inv-current",
            "Game server connected"));

        Assert.Equal("/usr/bin/cat", runner.Calls[0][0]);
        Assert.Equal("/proc/4242/cgroup", runner.Calls[0][1]);
        Assert.Equal(new[] { "/usr/bin/ss", "-H", "-lun" }, runner.Calls[1]);
        Assert.Contains("_SYSTEMD_INVOCATION_ID=inv-current", runner.Calls[2]);
        Assert.DoesNotContain(runner.Calls.SelectMany(x => x), x =>
            x.Contains("cmdline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProbeTimeoutFailsClosed()
    {
        var probe = new ValheimRuntimeProbe(
            new BlockingRunner(),
            TimeSpan.FromMilliseconds(20));

        Assert.False(await probe.IsProcessInUnitAsync(
            4242,
            "valheim-main.service"));
    }

    [Fact]
    public async Task ProbePropagatesCallerCancellation()
    {
        var probe = new ValheimRuntimeProbe(
            new BlockingRunner(),
            TimeSpan.FromMinutes(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            probe.AreUdpPortsOwnedAsync(4242, 2456, 2457, cts.Token));
    }

    [Fact]
    public async Task UdpPortsMustBothBeListening()
    {
        var runner = new RecordingRunner();
        runner.Results.Enqueue(new CommandResult
        {
            StdOut =
                "UNCONN 0 0 *:2456 *:*\n" +
                "UNCONN 0 0 0.0.0.0:9999 0.0.0.0:*\n",
        });
        var probe = new ValheimRuntimeProbe(runner);

        Assert.False(await probe.AreUdpPortsOwnedAsync(4242, 2456, 2457));
    }
}
