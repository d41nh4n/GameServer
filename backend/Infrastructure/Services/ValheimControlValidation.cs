using GamePanel.Application.Interfaces;

namespace GamePanel.Infrastructure.Services;

public static class ValheimControlValidation
{
    public static string? Validate(ValheimCapability capability, ValheimControlRequest request)
    {
        if (capability is ValheimCapability.KickPlayer or ValheimCapability.BanPlayer or ValheimCapability.TeleportPlayer or ValheimCapability.InventoryManagement or ValheimCapability.GodMode)
            if (string.IsNullOrWhiteSpace(request.TargetPlayerId)) return "TargetPlayerId is required";
        if (capability == ValheimCapability.BroadcastMessage && (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 500)) return "Message is required and must be at most 500 characters";
        if (capability == ValheimCapability.ChangeTime && (request.Value is null or < 0 or > 23)) return "Time must be between 0 and 23";
        if (capability == ValheimCapability.ChangeWeather && request.Weather is not ("clear" or "rain" or "fog" or "snow")) return "Unsupported weather value";
        if (capability == ValheimCapability.SpawnItem && (string.IsNullOrWhiteSpace(request.Item) || request.Item.Length > 128 || request.Amount is null or < 1 or > 999)) return "Item and amount are required; amount must be 1..999";
        return null;
    }
}
