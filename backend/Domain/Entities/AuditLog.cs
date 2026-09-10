namespace GamePanel.Domain.Entities;

public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }
    public string UsernameSnapshot { get; set; } = "system";
    public Guid? ServerInstanceId { get; set; }
    public string Action { get; set; } = "";
    public bool IsSuccess { get; set; }
    public string? ResultCode { get; set; }
    public string? MetadataJson { get; set; }
}