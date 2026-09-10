namespace GamePanel.Domain.Entities;

public sealed class SystemEvent
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid ServerInstanceId { get; set; }
    public string EventType { get; set; } = "";
    public int? PreviousStatus { get; set; }
    public int? CurrentStatus { get; set; }
    public int? MainPid { get; set; }
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "INFO";
}