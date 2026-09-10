namespace GamePanel.Api;

public class JwtSettings
{
    public const string Section = "Jwt";
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "GamePanelApi";
    public string Audience { get; set; } = "GamePanelClient";
    public int ExpiryMinutes { get; set; } = 30;
}

public class TailscaleSettings
{
    public const string Section = "Tailscale";
    public bool Require { get; set; }
    public string Subnet { get; set; } = "100.64.0.0/10";
}

public class ValheimSettings
{
    public const string Section = "GameServers:Valheim";
    public string ServiceName { get; set; } = "valheim-main.service";
    public string InstanceKey { get; set; } = "valheim-main";
    public string ReadinessMarker { get; set; } = "Game server connected";
    public int BasePort { get; set; } = 2456;
}

public class ProjectZomboidSettings
{
    public const string Section = "GameServers:ProjectZomboid";
    public string ServiceName { get; set; } = "pzserver-game.service";
    public string ControlExecutable { get; set; } = "/usr/local/sbin/pz-gamectl";
    public string InstallPath { get; set; } = "{PZ_HOME}/steamcmd/zomboid";
    public string ConfigDirectory { get; set; } = "{PZ_HOME}/Zomboid/Server";
    public string DefaultServerName { get; set; } = "servertest_new";
    public string RconHost { get; set; } = "127.0.0.1";
    public int RconPort { get; set; } = 27015;
    public string RconPassword { get; set; } = "";
    // Sekrety trzymane w env / machine-specific appsettings — nie w źródłach.
    public string SudoUser { get; set; } = "";
}