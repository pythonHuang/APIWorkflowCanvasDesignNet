namespace Juggle.Domain.Entities;

/// <summary>告警记录</summary>
public class AlertRecordEntity : BaseEntity
{
    public long RuleId { get; set; }
    public string? RuleName { get; set; }
    public string? MetricType { get; set; }
    public string? Message { get; set; }
    public string? Detail { get; set; }
    public string? Status { get; set; } = "triggered";  // triggered / resolved
}
