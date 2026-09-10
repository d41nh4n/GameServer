namespace GamePanel.Infrastructure.GameServers;
using GamePanel.Domain.Entities;

public interface IGameServerAdapterFactory
{
    IGameServerAdapter Create(ServerInstance instance);
}

public class GameServerAdapterFactory : IGameServerAdapterFactory
{
    private readonly IServiceProvider _services;

    public GameServerAdapterFactory(IServiceProvider services) => _services = services;

    public IGameServerAdapter Create(ServerInstance instance) => instance.Type switch
    {
        GameServerType.Valheim => _services.GetService(typeof(ValheimProvider)) as IGameServerAdapter
            ?? throw new InvalidOperationException("ValheimProvider not registered"),
        GameServerType.ProjectZomboid => _services.GetService(typeof(ProjectZomboidAdapter)) as IGameServerAdapter
            ?? throw new InvalidOperationException("ProjectZomboidAdapter not registered"),
        _ => throw new NotSupportedException($"No adapter registered for game type '{instance.Type}'"),
    };
}