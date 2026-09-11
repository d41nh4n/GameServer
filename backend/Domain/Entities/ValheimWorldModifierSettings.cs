namespace GamePanel.Domain.Entities;

public sealed class ValheimWorldModifierSettings
{
    public Guid ServerInstanceId { get; set; }
    public string Combat { get; set; } = "normal";
    public double ResourceRate { get; set; } = 1;
    public string RaidRate { get; set; } = "normal";
    public string DeathPenalty { get; set; } = "normal";
    public string PortalMode { get; set; } = "hard";
    public bool PassiveEnemies { get; set; }
    public bool PlayerBasedRaids { get; set; }
    public bool HammerMode { get; set; }
    public bool NoBuildCost { get; set; }
}
