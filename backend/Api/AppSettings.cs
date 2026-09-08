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
    public string ExecutablePath { get; set; } = "";
}