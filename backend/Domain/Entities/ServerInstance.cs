namespace GamePanel.Domain.Entities;

public enum GameServerType { Valheim, Minecraft, ProjectZomboid }
public enum ServerStatus { Stopped, Running, Starting, Stopping }

public class ServerInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string GameType { get; set; } = "";
    public GameServerType Type { get; set; }
    public ServerStatus Status { get; set; } = ServerStatus.Stopped;
    public int? ProcessId { get; set; }
    public int Port { get; set; } = 2456;
    public string WorldName { get; set; } = "Dedicated";
    public string Password { get; set; } = "";
}