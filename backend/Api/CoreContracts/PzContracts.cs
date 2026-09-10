namespace GamePanel.Api.CoreContracts;

public sealed record RconRequest(string Command);
public sealed record RconBroadcast(string Message);
public sealed record RconKick(string Username, string Reason = "Kicked");
public sealed record ConfigUpdateBody(Dictionary<string, Dictionary<string, string>> Config);
public sealed record SandboxChange(string Section, string Key, object Value);
public sealed record SandboxSaveBody(List<SandboxChange> Values);
public sealed record PzModUpdateBody(List<string>? WorkshopIds, List<string>? ModIds);