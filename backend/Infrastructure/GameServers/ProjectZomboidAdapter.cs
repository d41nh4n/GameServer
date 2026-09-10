namespace GamePanel.Infrastructure.GameServers;
using GamePanel.Domain.Entities;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Systemd-backed Project Zomboid adapter with RCON save-then-stop and logs.
/// Uses pz-gamectl wrapper for start/stop (via sudoers) and reads systemd for
/// status. RCON is only used pre-stop to broadcast + save the world.
/// </summary>
public class ProjectZomboidAdapter : IGameServerAdapter
{
    private readonly ISystemdRuntimeDriver _driver;
    private readonly IRconClient _rcon;
    private readonly string _serviceName;

    public ProjectZomboidAdapter(
        ISystemdRuntimeDriver driver,
        IConfiguration config,
        IRconClient rcon)
    {
        _driver = driver;
        _rcon = rcon;
        var serviceName = config["GameServers:ProjectZomboid:ServiceName"];
        _serviceName = string.IsNullOrWhiteSpace(serviceName)
            ? "pzserver-game.service"
            : serviceName;
    }

    // ---- IGameServerAdapter ----

    public async Task<GameServerActionResult> StartAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        if (!await _driver.ControlAsync(
                _serviceName,
                SystemdControlAction.Start,
                ct))
        {
            return new(false, await InspectAsync(instance, ct));
        }

        var deadline = DateTime.UtcNow.AddSeconds(150);
        GameServerRuntimeSnapshot snapshot;
        do
        {
            snapshot = await InspectAsync(instance, ct);
            if (snapshot.Status == GameServerStatus.Running) break;
            await Task.Delay(1000, ct);
        }
        while (DateTime.UtcNow < deadline);

        return new(
            snapshot.Status == GameServerStatus.Running,
            snapshot);
    }

    public async Task<GameServerActionResult> StopAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        // 1. Broadcast shutdown message, save via RCON
        try
        {
            await _rcon.SendCommandAsync(
                @"servermsg ""Server is shutting down for maintenance...""", ct);
            await _rcon.SendCommandAsync("save", ct);
            await Task.Delay(3000, ct); // give time to flush
        }
        catch
        {
            // RCON unavailable — fall through to systemd stop (SIGINT handles save).
        }

        // 2. systemd stop (SIGINT → PZ saves then exits per unit configuration)
        var controlSucceeded = await _driver.ControlAsync(
            _serviceName,
            SystemdControlAction.Stop,
            ct);
        if (!controlSucceeded)
        {
            return new(false, await InspectAsync(instance, ct));
        }

        // 3. Poll until stopped
        var deadline = DateTime.UtcNow.AddSeconds(24);
        GameServerRuntimeSnapshot snapshot;
        do
        {
            snapshot = await InspectAsync(instance, ct);
            if (snapshot.Status == GameServerStatus.Stopped) break;
            await Task.Delay(1000, ct);
        }
        while (DateTime.UtcNow < deadline);

        return new(controlSucceeded, snapshot);
    }

    public async Task<GameServerRuntimeSnapshot> InspectAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        var state = await _driver.GetStateAsync(_serviceName, ct);
        if (!state.QuerySucceeded)
        {
            return new(GameServerStatus.Unknown, instance.ProcessId, false);
        }
        var isRunning =
            state.QuerySucceeded &&
            state.ActiveState == "active" &&
            state.MainPid > 0 &&
            Directory.Exists($"/proc/{state.MainPid}");
        if (!isRunning)
        {
            return new(GameServerStatus.Stopped, null, false);
        }
        return new(GameServerStatus.Running, state.MainPid, true);
    }

    // ---- Extended: logs ----

    public async Task<string> GetLogsAsync(int lines = 200, CancellationToken ct = default)
    {
        lines = Math.Max(1, Math.Min(lines, 1000));
        var result = await RunSystemctlAsync(
            _serviceName,
            "logs",
            lines.ToString(),
            ct);
        return result;
    }

    private static async Task<string> RunSystemctlAsync(
        string unitName,
        string subcommand,
        string arg,
        CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/usr/bin/sudo",
            ArgumentList = { "-n", "/usr/local/sbin/pz-gamectl", subcommand, arg },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return "";

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return stdout;
    }
}