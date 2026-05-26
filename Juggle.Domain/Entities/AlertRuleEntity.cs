namespace Juggle.Domain.Entities;

/// <summary>告警规则</summary>
public class AlertRuleEntity : BaseEntity
{
    public string? Name { get; set; }
    public string? MetricType { get; set; }  // flow_fail / api_fail / flow_timeout / api_timeout
    public string? Condition { get; set; }   // > / >= / < / <= / ==
    public double Threshold { get; set; }
    public string? Channel { get; set; }     // email / sms / email+sms
    public string? Recipients { get; set; }  // 逗号分隔
    public string? Description { get; set; }
    public int Status { get; set; } = 1;     // 1=启用 0=停用
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}
