namespace GamePanel.Application.Interfaces;

public enum ValheimCapability
{
    OnlinePlayers, KickPlayer, BanPlayer, BroadcastMessage, SaveWorld, ChangeTime,
    ChangeWeather, TeleportPlayer, SpawnItem, InventoryManagement, GodMode
}

public sealed record ValheimCapabilities(IReadOnlySet<ValheimCapability> Supported, bool Connected, DateTime? CheckedAtUtc, string? Reason);
public sealed record ValheimOnlinePlayer(string PlatformId, string Name, DateTime? ConnectedAtUtc);
public sealed record ValheimActionResult(bool Success, string Code, string? Message = null);
public sealed record ValheimControlRequest(string? TargetPlayerId = null, string? Message = null, int? Value = null, string? Weather = null, string? Item = null, int? Amount = null);

public interface IGameControlProtocol
{
    Task<ValheimCapabilities> GetCapabilitiesAsync(Guid serverId, CancellationToken ct = default);
    Task<IReadOnlyList<ValheimOnlinePlayer>> GetPlayersAsync(Guid serverId, CancellationToken ct = default);
    Task<ValheimActionResult> ExecuteAsync(Guid serverId, ValheimCapability capability, ValheimControlRequest request, CancellationToken ct = default);
}
