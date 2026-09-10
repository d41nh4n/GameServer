namespace GamePanel.Infrastructure.GameServers;
using GamePanel.Domain.Entities;

public enum GameServerStatus
{
    Unknown,
    Stopped,
    Starting,
    Running,
    Stopping,
}

public sealed record GameServerRuntimeSnapshot(
    GameServerStatus Status,
    int? MainPid,
    bool Ready);

public sealed record GameServerActionResult(
    bool Success,
    GameServerRuntimeSnapshot Snapshot);

public interface IGameServerAdapter
{
    Task<GameServerActionResult> StartAsync(
        ServerInstance instance,
        CancellationToken ct = default);

    Task<GameServerActionResult> StopAsync(
        ServerInstance instance,
        CancellationToken ct = default);

    Task<GameServerRuntimeSnapshot> InspectAsync(
        ServerInstance instance,
        CancellationToken ct = default);
}
