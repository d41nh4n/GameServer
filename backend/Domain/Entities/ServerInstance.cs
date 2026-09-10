namespace GamePanel.Domain.Entities;

public enum GameServerType { Valheim, Minecraft, ProjectZomboid }
public enum ServerStatus { Stopped, Running, Starting, Stopping }
public enum ProvisioningMode { Managed, AdoptExisting }
public enum ServerRuntimeType { Process, Systemd }
public enum ServerProtocol { Udp, Tcp }

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
    public string? InstanceKey { get; set; }
    public ProvisioningMode ProvisioningMode { get; set; } = ProvisioningMode.Managed;
    public ServerRuntimeType RuntimeType { get; set; } = ServerRuntimeType.Process;
    public string? RuntimeId { get; set; }
    public string? InstallationPath { get; set; }
    public string? DataPath { get; set; }
    public string? BackupPath { get; set; }
    public int? SteamAppId { get; set; }
    public ServerProtocol? Protocol { get; set; }
    public string? ReadinessMarker { get; set; }
    public bool Ready { get; set; }
}