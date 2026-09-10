namespace GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;

public interface IGameServerRuntime
{
    Task<IEnumerable<ServerInstance>> GetAllAsync(CancellationToken ct = default);
    Task<bool> StartAsync(Guid id, CancellationToken ct = default);
    Task<bool> StopAsync(Guid id, CancellationToken ct = default);
}
