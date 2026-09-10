namespace GamePanel.Infrastructure.GameServers;
using System.Text.RegularExpressions;

public interface IValheimRuntimeProbe
{
    Task<bool> IsProcessInUnitAsync(
        int pid,
        string unitName,
        CancellationToken ct = default);

    Task<bool> AreUdpPortsOwnedAsync(
        int pid,
        int firstPort,
        int secondPort,
        CancellationToken ct = default);

    Task<bool> HasReadinessMarkerAsync(
        string unitName,
        string invocationId,
        string marker,
        CancellationToken ct = default);
}

/// <summary>
/// Performs bounded, read-only Valheim runtime probes through fixed executables.
/// It never reads the Valheim environment file or process command line.
/// </summary>
public sealed class ValheimRuntimeProbe : IValheimRuntimeProbe
{
    private readonly ICommandRunner _runner;
    private readonly TimeSpan _commandTimeout;

    public ValheimRuntimeProbe(
        ICommandRunner runner,
        TimeSpan? commandTimeout = null)
    {
        _runner = runner;
        _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(5);
        if (_commandTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        }
    }

    public async Task<bool> IsProcessInUnitAsync(
        int pid,
        string unitName,
        CancellationToken ct = default)
    {
        if (pid <= 0 || string.IsNullOrWhiteSpace(unitName)) return false;
        var result = await RunBoundedAsync(new List<string>
        {
            "/usr/bin/cat",
            $"/proc/{pid}/cgroup",
        }, ct);
        return result is { } commandResult &&
            commandResult.Succeeded &&
            commandResult.StdOut.Split('\n').Any(line =>
                line.TrimEnd('\r').EndsWith("/" + unitName, StringComparison.Ordinal));
    }

    public async Task<bool> AreUdpPortsOwnedAsync(
        int pid,
        int firstPort,
        int secondPort,
        CancellationToken ct = default)
    {
        if (pid <= 0 || firstPort <= 0 || secondPort <= 0) return false;
        var result = await RunBoundedAsync(new List<string>
        {
            "/usr/bin/ss",
            "-H",
            "-lun",
        }, ct);
        if (result is not { } commandResult || !commandResult.Succeeded) return false;

        return OwnsPort(commandResult.StdOut, firstPort) &&
            OwnsPort(commandResult.StdOut, secondPort);
    }

    public async Task<bool> HasReadinessMarkerAsync(
        string unitName,
        string invocationId,
        string marker,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(unitName) ||
            string.IsNullOrWhiteSpace(invocationId) ||
            string.IsNullOrWhiteSpace(marker))
        {
            return false;
        }

        var result = await RunBoundedAsync(new List<string>
        {
            "/usr/bin/journalctl",
            "-u",
            unitName,
            "_SYSTEMD_INVOCATION_ID=" + invocationId,
            "--grep=" + marker,
            "--no-pager",
            "-n",
            "1",
            "-o",
            "cat",
        }, ct);
        return result is { } commandResult &&
            commandResult.Succeeded &&
            !string.IsNullOrWhiteSpace(commandResult.StdOut);
    }

    private async Task<CommandResult?> RunBoundedAsync(
        List<string> argv,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_commandTimeout);
        try
        {
            return await _runner.RunAsync(argv, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool OwnsPort(string output, int port)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (Regex.IsMatch(line, $@":{port}\b")) return true;
        }
        return false;
    }
}
