using System.Text.Json;

namespace GamePanel.Infrastructure.GameServers;

public enum SystemdControlAction
{
    Start,
    Stop,
    Restart,
}

public sealed record SystemdUnitDefinition(
    string UnitName,
    string? ControlExecutable,
    bool UseSudo,
    string? SudoUser,
    bool ControlEnabled);

public sealed record SystemdUnitState(
    bool QuerySucceeded,
    string ActiveState,
    string SubState,
    int MainPid,
    string InvocationId)
{
    public bool IsRunning =>
        QuerySucceeded &&
        ActiveState == "active" &&
        SubState == "running" &&
        MainPid > 0;
}

public interface ISystemdRuntimeDriver
{
    Task<SystemdUnitState> GetStateAsync(string unitName, CancellationToken ct = default);
    Task<bool> ControlAsync(
        string unitName,
        SystemdControlAction action,
        CancellationToken ct = default);
}

/// <summary>
/// Executes a fixed systemctl query and allowlisted control wrappers. Unit names
/// and wrapper paths come only from internal application configuration.
/// </summary>
public sealed class SystemdRuntimeDriver : ISystemdRuntimeDriver
{
    private readonly ICommandRunner _runner;
    private readonly IReadOnlyDictionary<string, SystemdUnitDefinition> _units;
    private readonly TimeSpan _commandTimeout;
    private readonly PrivilegedBrokerClient? _broker;

    public SystemdRuntimeDriver(
        ICommandRunner runner,
        IEnumerable<SystemdUnitDefinition> units,
        TimeSpan? commandTimeout = null,
        PrivilegedBrokerClient? broker = null)
    {
        _runner = runner;
        _units = units.ToDictionary(x => x.UnitName, StringComparer.Ordinal);
        _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(30);
        _broker = broker;
        if (_commandTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        }
    }

    public async Task<SystemdUnitState> GetStateAsync(
        string unitName,
        CancellationToken ct = default)
    {
        RequireUnit(unitName);
        if (_broker is not null)
        {
            try
            {
                var brokerResponse = await _broker.SendAsync(unitName, "status", ct);
                var brokerState = brokerResponse.GetProperty("status");
                var brokerValues = brokerState.EnumerateObject()
                    .Where(x => x.Value.ValueKind is
                        JsonValueKind.String or
                        JsonValueKind.Number or
                        JsonValueKind.True or
                        JsonValueKind.False)
                    .ToDictionary(
                        x => x.Name,
                        x => x.Value.ValueKind == JsonValueKind.String
                            ? (x.Value.GetString() ?? "")
                            : x.Value.ToString(),
                        StringComparer.Ordinal);
                return new(true, Value(brokerValues, "ActiveState"), Value(brokerValues, "SubState"), ParsePositiveInt(Value(brokerValues, "MainPID")), Value(brokerValues, "InvocationID"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return FailedState(); }
        }
        var result = await RunBoundedAsync(new List<string>
        {
            "/usr/bin/systemctl",
            "show",
            unitName,
            "--property=ActiveState",
            "--property=SubState",
            "--property=MainPID",
            "--property=InvocationID",
            "--no-pager",
        }, ct);

        if (result is not { } commandResult || !commandResult.Succeeded)
        {
            return FailedState();
        }

        var values = ParseProperties(commandResult.StdOut);
        return new SystemdUnitState(
            true,
            Value(values, "ActiveState"),
            Value(values, "SubState"),
            ParsePositiveInt(Value(values, "MainPID")),
            Value(values, "InvocationID"));
    }

    public async Task<bool> ControlAsync(
        string unitName,
        SystemdControlAction action,
        CancellationToken ct = default)
    {
        var unit = RequireUnit(unitName);
        if (_broker is not null)
        {
            try { await _broker.SendAsync(unitName, action.ToString().ToLowerInvariant(), ct); return true; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return false; }
        }
        if (!unit.ControlEnabled || string.IsNullOrWhiteSpace(unit.ControlExecutable))
        {
            return false;
        }

        var argv = new List<string>();
        if (unit.UseSudo)
        {
            argv.Add("/usr/bin/sudo");
            argv.Add("-n");
            if (!string.IsNullOrWhiteSpace(unit.SudoUser))
            {
                argv.Add("-u");
                argv.Add(unit.SudoUser);
            }
        }

        argv.Add(unit.ControlExecutable);
        argv.Add(action switch
        {
            SystemdControlAction.Start => "start",
            SystemdControlAction.Stop => "stop",
            SystemdControlAction.Restart => "restart",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        });
        argv.Add(unitName);

        var result = await RunBoundedAsync(argv, ct);
        return result is { } commandResult && commandResult.Succeeded;
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

    private static SystemdUnitState FailedState() =>
        new(false, "", "", 0, "");

    private SystemdUnitDefinition RequireUnit(string unitName)
    {
        if (!_units.TryGetValue(unitName, out var unit))
        {
            throw new InvalidOperationException(
                $"Systemd unit '{unitName}' is not allowlisted.");
        }
        return unit;
    }

    private static Dictionary<string, string> ParseProperties(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            values[line[..separator]] = line[(separator + 1)..];
        }
        return values;
    }

    private static string Value(
        IReadOnlyDictionary<string, string> values,
        string key) => values.TryGetValue(key, out var value) ? value : "";

    private static int ParsePositiveInt(string value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 0;
}
