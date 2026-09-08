namespace GamePanel.Infrastructure.GameServers;
using GamePanel.Domain.Entities;

public enum GameServerStatus { Stopped, Running }

public interface IGameServerAdapter
{
    Task<(bool Success, int Pid)> StartAsync(ServerInstance instance, CancellationToken ct = default);
    Task<bool> StopAsync(int pid, CancellationToken ct = default);
    Task<GameServerStatus> GetStatusAsync(int pid, CancellationToken ct = default);
}