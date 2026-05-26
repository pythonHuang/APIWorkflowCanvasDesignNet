namespace Juggle.Domain.Entities;

public class ReportEntity : BaseEntity
{
    public string? Name { get; set; }
    public string? GroupName { get; set; }
    public string? SourceType { get; set; }    // flow / api / dataview / sql
    public string? SourceRef { get; set; }     // flowKey / apiCode / dataViewId
    public string? CustomSql { get; set; }     // SourceType=sql 时使用
    public string? ParamsConfig { get; set; }  // 查询参数 JSON
    public string? LayoutJson { get; set; }    // 报表布局 JSON
    public int Status { get; set; } = 1;
}
