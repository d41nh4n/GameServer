using GamePanel.Domain.Entities;

namespace GamePanel.Domain.Entities;

public sealed class ServerStatusSnapshot
{
    public Guid ServerInstanceId { get; set; }
    public ServerInstance ServerInstance { get; set; } = null!;
    public ServerStatus Status { get; set; }
    public int? ProcessId { get; set; }
    public bool Ready { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public string Source { get; set; } = "heartbeat";
}
