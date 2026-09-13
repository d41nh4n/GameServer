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

public sealed record ValheimModToggleRequest(string RelativePath);
public sealed record ValheimModDeleteRequest(string RelativePath);
public sealed record ValheimModConfigSaveRequest(string Name, string Content);
public sealed record ValheimThunderstoreInstallRequest(string DownloadUrl, string PackageFullName);
