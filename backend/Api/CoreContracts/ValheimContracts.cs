namespace GamePanel.Api.CoreContracts;

public sealed record ValheimMemberChange(string Id, string Role);
public sealed record ValheimWorldModifierSettingsRequest(
    string Combat,
    double ResourceRate,
    string RaidRate,
    string DeathPenalty,
    string PortalMode,
    bool PassiveEnemies,
    bool PlayerBasedRaids,
    bool HammerMode,
    bool NoBuildCost);
