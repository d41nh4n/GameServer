namespace GamePanel.Application.Interfaces;
using GamePanel.Domain.Entities;
public interface IGameServerRuntime { Task<IEnumerable<ServerInstance>> GetAllAsync(); Task<bool> StartAsync(Guid id); Task<bool> StopAsync(Guid id); }
