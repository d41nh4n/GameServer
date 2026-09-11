namespace GamePanel.Api.CoreContracts;

public sealed record ValheimControlActionBody(string? TargetPlayerId, string? Message, int? Value, string? Weather, string? Item, int? Amount);
