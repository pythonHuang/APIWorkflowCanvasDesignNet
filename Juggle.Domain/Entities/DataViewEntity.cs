namespace Juggle.Domain.Entities;

public class DataViewEntity : BaseEntity
{
    public string? GroupName { get; set; }
    public string? Name { get; set; }
    public long DataSourceId { get; set; }
    public string? Sql { get; set; }
    public string? Parameters { get; set; } // JSON: [{name,type,default,label}]

    /// <summary>SQL 字段中文对照（多行文本，每行一条：字段=中文注释）</summary>
    public string? ColumnMapping { get; set; }

    public string? Remark { get; set; }
    public int Status { get; set; } = 1;
}
