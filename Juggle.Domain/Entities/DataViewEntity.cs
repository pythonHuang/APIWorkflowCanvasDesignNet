namespace Juggle.Domain.Entities;

public class DataViewEntity : BaseEntity
{
    public string? GroupName { get; set; }
    public string? Name { get; set; }
    public long DataSourceId { get; set; }
    public string? Sql { get; set; }
    public string? Parameters { get; set; } // JSON: [{name,type,default,label}]
    public string? Remark { get; set; }
    public int Status { get; set; } = 1;
}
