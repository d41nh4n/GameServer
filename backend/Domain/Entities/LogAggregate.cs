namespace GamePanel.Domain.Entities;

public sealed class LogAggregate
{
    public long Id { get; set; }
    public Guid ServerInstanceId { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public int FatalCount { get; set; }
    public int PlayerJoinCount { get; set; }
    public int PlayerLeaveCount { get; set; }
    public string? LastErrorMessage { get; set; }
}