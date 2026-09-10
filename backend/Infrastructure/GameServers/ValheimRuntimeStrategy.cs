namespace GamePanel.Infrastructure.GameServers;
using GamePanel.Domain.Entities;

/// <summary>
/// Applies Valheim-specific health rules to an allowlisted systemd unit.
/// Running requires current-unit cgroup ownership, both UDP ports, and the
/// readiness marker from the current systemd invocation.
/// </summary>
public sealed class ValheimRuntimeStrategy
{
    private readonly ISystemdRuntimeDriver _driver;
    private readonly IValheimRuntimeProbe _probe;

    public ValheimRuntimeStrategy(
        ISystemdRuntimeDriver driver,
        IValheimRuntimeProbe probe)
    {
        _driver = driver;
        _probe = probe;
    }

    public async Task<GameServerRuntimeSnapshot> InspectAsync(
        ServerInstance instance,
        CancellationToken ct = default)
    {
        if (instance.ProvisioningMode != ProvisioningMode.AdoptExisting ||
            instance.RuntimeType != ServerRuntimeType.Systemd ||
            string.IsNullOrWhiteSpace(instance.RuntimeId))
        {
            return Unknown();
        }

        SystemdUnitState state;
        try
        {
            state = await _driver.GetStateAsync(instance.RuntimeId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unknown();
        }
        if (!state.QuerySucceeded) return Unknown();
        if (state.ActiveState != "active")
        {
            return new GameServerRuntimeSnapshot(
                GameServerStatus.Stopped,
                null,
                false);
        }
        if (!state.IsRunning)
        {
            return new GameServerRuntimeSnapshot(
                GameServerStatus.Starting,
                state.MainPid > 0 ? state.MainPid : null,
                false);
        }

        if (instance.Port <= 0 || instance.Port >= 65535)
        {
            return new GameServerRuntimeSnapshot(
                GameServerStatus.Starting,
                state.MainPid,
                false);
        }

        bool inUnit;
        bool portsOwned;
        bool markerFound;
        try
        {
            var cgroupTask = _probe.IsProcessInUnitAsync(
                state.MainPid,
                instance.RuntimeId,
                ct);
            var portsTask = _probe.AreUdpPortsOwnedAsync(
                state.MainPid,
                instance.Port,
                instance.Port + 1,
                ct);
            var markerTask = _probe.HasReadinessMarkerAsync(
                instance.RuntimeId,
                state.InvocationId,
                instance.ReadinessMarker ?? "",
                ct);
            await Task.WhenAll(cgroupTask, portsTask, markerTask);
            inUnit = await cgroupTask;
            portsOwned = await portsTask;
            markerFound = await markerTask;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            inUnit = false;
            portsOwned = false;
            markerFound = false;
        }
        var ready = inUnit && portsOwned && markerFound;

        return new GameServerRuntimeSnapshot(
            ready ? GameServerStatus.Running : GameServerStatus.Starting,
            state.MainPid,
            ready);
    }

    private static GameServerRuntimeSnapshot Unknown() =>
        new(GameServerStatus.Unknown, null, false);
}
