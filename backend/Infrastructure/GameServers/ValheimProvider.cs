namespace GamePanel.Infrastructure.GameServers;
using GamePanel.Domain.Entities;

/// <summary>
/// AdoptExisting Valheim adapter. Status inspection is read-only (systemctl
/// show + cgroup/port/marker probes). Start and stop delegate to the allowlisted
/// systemd control path so the game executable or unit is driven exactly as the
/// systemd unit defines it (valheim uses KillSignal=SIGINT, so stopping the unit
/// triggers Valheim's own save-then-shutdown). Control only ever applies to
/// allowlisted units (enforced by SystemdRuntimeDriver.RequireUnit).
/// </summary>
public sealed class ValheimProvider : IGameServerAdapter
{
    private readonly ValheimRuntimeStrategy _strategy;
    private readonly ISystemdRuntimeDriver _driver;

    public ValheimProvider(
        ValheimRuntimeStrategy strategy,
        ISystemdRuntimeDriver driver)
    {
        _strategy = strategy;
        _driver = driver;
    }

    public async Task<GameServerActionResult> StartAsync(
        ServerInstance instance,
        CancellationToken ct = default) =>
        new(await ControlOrFailClosedAsync(
            instance, SystemdControlAction.Start, ct),
            await InspectAsync(instance, ct));

    public async Task<GameServerActionResult> StopAsync(
        ServerInstance instance,
        CancellationToken ct = default) =>
        new(await ControlOrFailClosedAsync(
            instance, SystemdControlAction.Stop, ct),
            await InspectAsync(instance, ct));

    public Task<GameServerRuntimeSnapshot> InspectAsync(
        ServerInstance instance,
        CancellationToken ct = default) =>
        _strategy.InspectAsync(instance, ct);

    private async Task<bool> ControlOrFailClosedAsync(
        ServerInstance instance,
        SystemdControlAction action,
        CancellationToken ct)
    {
        var unit = instance.RuntimeId;
        if (instance.RuntimeType != ServerRuntimeType.Systemd ||
            instance.ProvisioningMode != ProvisioningMode.AdoptExisting ||
            string.IsNullOrWhiteSpace(unit))
        {
            return false;
        }
        try
        {
            return await _driver.ControlAsync(unit, action, ct);
        }
        catch
        {
            // Only allowlisted units may be controlled; anything else fails closed.
            return false;
        }
    }
}