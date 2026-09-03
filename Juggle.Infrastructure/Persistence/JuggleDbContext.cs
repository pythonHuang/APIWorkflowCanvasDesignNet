using Juggle.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Infrastructure.Persistence;

/// <summary>
/// Infrastructure 层用于获取当前租户 ID 的轻量接口（避免依赖 Web 层）。
/// 在 Juggle.Api 的 Program.cs 中注册具体实现。
/// </summary>
public interface ICurrentTenantProvider
{
    /// <summary>
    /// 获取当前请求的租户ID。
    /// null = 超管/匿名/后台任务，此时 DbContext 不加租户过滤。
    /// </summary>
    long? GetCurrentTenantId();
}

public class JuggleDbContext : DbContext
{
    private readonly ICurrentTenantProvider? _tenantProvider;

    public JuggleDbContext(DbContextOptions<JuggleDbContext> options) : base(options) { }

    /// <summary>带租户上下文的构造函数（由 DI 自动选择参数最多的）</summary>
    public JuggleDbContext(DbContextOptions<JuggleDbContext> options, ICurrentTenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    /// <summary>当前请求的租户ID（由 ICurrentTenantProvider 动态提供）</summary>
    private long? CurrentTenantId => _tenantProvider?.GetCurrentTenantId();

    public DbSet<UserEntity> Users { get; set; } = null!;
    public DbSet<SuiteEntity> Suites { get; set; } = null!;
    public DbSet<ApiEntity> Apis { get; set; } = null!;
    public DbSet<ParameterEntity> Parameters { get; set; } = null!;
    public DbSet<ObjectEntity> Objects { get; set; } = null!;
    public DbSet<FlowDefinitionEntity> FlowDefinitions { get; set; } = null!;
    public DbSet<VariableInfoEntity> VariableInfos { get; set; } = null!;
    public DbSet<FlowInfoEntity> FlowInfos { get; set; } = null!;
    public DbSet<FlowVersionEntity> FlowVersions { get; set; } = null!;
    public DbSet<TokenEntity> Tokens { get; set; } = null!;
    public DbSet<TokenPermissionEntity> TokenPermissions { get; set; } = null!;
    public DbSet<DataSourceEntity> DataSources { get; set; } = null!;
    public DbSet<FlowLogEntity> FlowLogs { get; set; } = null!;
    public DbSet<FlowNodeLogEntity> FlowNodeLogs { get; set; } = null!;
    public DbSet<StaticVariableEntity> StaticVariables { get; set; } = null!;
    public DbSet<ScheduleTaskEntity> ScheduleTasks { get; set; } = null!;
    public DbSet<WebhookEntity> Webhooks { get; set; } = null!;
    public DbSet<SystemConfigEntity> SystemConfigs { get; set; } = null!;
    public DbSet<FlowTestCaseEntity> FlowTestCases { get; set; } = null!;
    public DbSet<RoleEntity> Roles { get; set; } = null!;
    public DbSet<AlertRuleEntity> AlertRules { get; set; } = null!;
    public DbSet<AlertRecordEntity> AlertRecords { get; set; } = null!;
    public DbSet<RoleMenuEntity> RoleMenus { get; set; } = null!;
    public DbSet<TenantEntity> Tenants { get; set; } = null!;
    public DbSet<AuditLogEntity> AuditLogs { get; set; } = null!;
    public DbSet<LoginLogEntity> LoginLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 表名映射
        modelBuilder.Entity<UserEntity>().ToTable("t_user");
        modelBuilder.Entity<SuiteEntity>().ToTable("t_suite");
        modelBuilder.Entity<ApiEntity>().ToTable("t_api");
        modelBuilder.Entity<ParameterEntity>().ToTable("t_parameter");
        modelBuilder.Entity<ObjectEntity>().ToTable("t_object");
        modelBuilder.Entity<FlowDefinitionEntity>().ToTable("t_flow_definition");
        modelBuilder.Entity<VariableInfoEntity>().ToTable("t_variable_info");
        modelBuilder.Entity<FlowInfoEntity>().ToTable("t_flow_info");
        modelBuilder.Entity<FlowVersionEntity>().ToTable("t_flow_version");
        modelBuilder.Entity<TokenEntity>().ToTable("t_token");
        modelBuilder.Entity<DataSourceEntity>().ToTable("t_data_source");
        modelBuilder.Entity<FlowLogEntity>().ToTable("t_flow_log");
        modelBuilder.Entity<FlowNodeLogEntity>().ToTable("t_flow_node_log");
        modelBuilder.Entity<StaticVariableEntity>().ToTable("t_static_variable");
        modelBuilder.Entity<TokenPermissionEntity>().ToTable("t_token_permission");
        modelBuilder.Entity<ScheduleTaskEntity>().ToTable("t_schedule_task");
        modelBuilder.Entity<WebhookEntity>().ToTable("t_webhook");
        modelBuilder.Entity<SystemConfigEntity>().ToTable("t_system_config");
        modelBuilder.Entity<FlowTestCaseEntity>().ToTable("t_flow_test_case");
        modelBuilder.Entity<RoleEntity>().ToTable("t_role");
        modelBuilder.Entity<RoleMenuEntity>().ToTable("t_role_menu");
        modelBuilder.Entity<TenantEntity>().ToTable("t_tenant");
        modelBuilder.Entity<AuditLogEntity>().ToTable("t_audit_log");
        modelBuilder.Entity<LoginLogEntity>().ToTable("t_login_log");
        modelBuilder.Entity<AlertRuleEntity>().ToTable("t_alert_rule");
        modelBuilder.Entity<AlertRecordEntity>().ToTable("t_alert_record");
        modelBuilder.Entity<DataViewEntity>().ToTable("t_data_view");
        modelBuilder.Entity<DataViewEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.TenantId).HasColumnName("tenant_id");
            e.Property(p => p.GroupName).HasColumnName("group_name");
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.DataSourceId).HasColumnName("data_source_id");
            e.Property(p => p.Sql).HasColumnName("sql");
            e.Property(p => p.Parameters).HasColumnName("parameters");
            e.Property(p => p.ColumnMapping).HasColumnName("column_mapping");
            e.Property(p => p.Remark).HasColumnName("remark");
            e.Property(p => p.Status).HasColumnName("status");
        });
        modelBuilder.Entity<ReportEntity>().ToTable("t_report");
        modelBuilder.Entity<ReportEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.TenantId).HasColumnName("tenant_id");
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.GroupName).HasColumnName("group_name");
            e.Property(p => p.SourceType).HasColumnName("source_type");
            e.Property(p => p.SourceRef).HasColumnName("source_ref");
            e.Property(p => p.CustomSql).HasColumnName("custom_sql");
            e.Property(p => p.ParamsConfig).HasColumnName("params_config");
            e.Property(p => p.LayoutJson).HasColumnName("layout_json");
            e.Property(p => p.Status).HasColumnName("status");
        });

        // 列名映射（snake_case）— AlertRule
        modelBuilder.Entity<AlertRuleEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.TenantId).HasColumnName("tenant_id");
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.MetricType).HasColumnName("metric_type");
            e.Property(p => p.Condition).HasColumnName("condition");
            e.Property(p => p.Threshold).HasColumnName("threshold");
            e.Property(p => p.Channel).HasColumnName("channel");
            e.Property(p => p.Recipients).HasColumnName("recipients");
            e.Property(p => p.Description).HasColumnName("description");
            e.Property(p => p.Status).HasColumnName("status");
        });

        // 列名映射（snake_case）— AlertRecord
        modelBuilder.Entity<AlertRecordEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.TenantId).HasColumnName("tenant_id");
            e.Property(p => p.RuleId).HasColumnName("rule_id");
            e.Property(p => p.RuleName).HasColumnName("rule_name");
            e.Property(p => p.MetricType).HasColumnName("metric_type");
            e.Property(p => p.Message).HasColumnName("message");
            e.Property(p => p.Detail).HasColumnName("detail");
            e.Property(p => p.Status).HasColumnName("status");
        });

        // 列名映射（snake_case）
        modelBuilder.Entity<UserEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.UserName).HasColumnName("user_name");
            e.Property(p => p.Password).HasColumnName("password");
            e.Property(p => p.RoleId).HasColumnName("role_id");
            e.Property(p => p.TenantId).HasColumnName("tenant_id");
        });

        modelBuilder.Entity<SuiteEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.SuiteCode).HasColumnName("suite_code");
            e.Property(p => p.SuiteName).HasColumnName("suite_name");
            e.Property(p => p.SuiteClassifyId).HasColumnName("suite_classify_id");
            e.Property(p => p.SuiteImage).HasColumnName("suite_image");
            e.Property(p => p.SuiteVersion).HasColumnName("suite_version");
            e.Property(p => p.SuiteDesc).HasColumnName("suite_desc");
            e.Property(p => p.SuiteHelpDocJson).HasColumnName("suite_help_doc_json").HasColumnType("text");
            e.Property(p => p.SuiteFlag).HasColumnName("suite_flag");
        });

        modelBuilder.Entity<ApiEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.SuiteCode).HasColumnName("suite_code");
            e.Property(p => p.MethodCode).HasColumnName("method_code");
            e.Property(p => p.MethodName).HasColumnName("method_name");
            e.Property(p => p.MethodDesc).HasColumnName("method_desc");
            e.Property(p => p.Url).HasColumnName("url");
            e.Property(p => p.RequestType).HasColumnName("request_type");
            e.Property(p => p.ContentType).HasColumnName("content_type");
            e.Property(p => p.MockJson).HasColumnName("mock_json").HasColumnType("text");
            e.Property(p => p.MethodType).HasColumnName("method_type");
            e.Property(p => p.SoapVersion).HasColumnName("soap_version");
            e.Property(p => p.SoapMethod).HasColumnName("soap_method");
            e.Property(p => p.SoapNamespace).HasColumnName("soap_namespace");
            e.Property(p => p.SoapAction).HasColumnName("soap_action");
            e.Property(p => p.ServiceAlias).HasColumnName("service_alias");
            e.Property(p => p.Status).HasColumnName("status");
        });

        modelBuilder.Entity<ParameterEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.OwnerId).HasColumnName("owner_id");
            e.Property(p => p.OwnerCode).HasColumnName("owner_code");
            e.Property(p => p.ParamType).HasColumnName("param_type");
            e.Property(p => p.ParamCode).HasColumnName("param_code");
            e.Property(p => p.ParamName).HasColumnName("param_name");
            e.Property(p => p.DataType).HasColumnName("data_type");
            e.Property(p => p.ObjectCode).HasColumnName("object_code");
            e.Property(p => p.Required).HasColumnName("required");
            e.Property(p => p.DefaultValue).HasColumnName("default_value");
            e.Property(p => p.Description).HasColumnName("description");
            e.Property(p => p.SortNum).HasColumnName("sort_num");
            e.Property(p => p.ParamPosition).HasColumnName("param_position");
        });

        modelBuilder.Entity<ObjectEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.ObjectCode).HasColumnName("object_code");
            e.Property(p => p.ObjectName).HasColumnName("object_name");
            e.Property(p => p.ObjectDesc).HasColumnName("object_desc");
        });

        modelBuilder.Entity<FlowDefinitionEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.FlowKey).HasColumnName("flow_key");
            e.Property(p => p.FlowName).HasColumnName("flow_name");
            e.Property(p => p.FlowDesc).HasColumnName("flow_desc");
            e.Property(p => p.FlowContent).HasColumnName("flow_content").HasColumnType("text");
            e.Property(p => p.FlowType).HasColumnName("flow_type");
            e.Property(p => p.GroupName).HasColumnName("group_name");
            e.Property(p => p.Status).HasColumnName("status");
            e.Property(p => p.ServiceAlias).HasColumnName("service_alias");
        });

        modelBuilder.Entity<VariableInfoEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.FlowDefinitionId).HasColumnName("flow_definition_id");
            e.Property(p => p.FlowKey).HasColumnName("flow_key");
            e.Property(p => p.VariableCode).HasColumnName("variable_code");
            e.Property(p => p.VariableName).HasColumnName("variable_name");
            e.Property(p => p.DataType).HasColumnName("data_type");
            e.Property(p => p.VariableType).HasColumnName("variable_type");
            e.Property(p => p.DefaultValue).HasColumnName("default_value");
            e.Property(p => p.Description).HasColumnName("description");
        });

        modelBuilder.Entity<FlowInfoEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.FlowKey).HasColumnName("flow_key");
            e.Property(p => p.FlowName).HasColumnName("flow_name");
            e.Property(p => p.FlowDesc).HasColumnName("flow_desc");
            e.Property(p => p.FlowType).HasColumnName("flow_type");
            e.Property(p => p.Status).HasColumnName("status");
        });

        modelBuilder.Entity<FlowVersionEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.FlowInfoId).HasColumnName("flow_info_id");
            e.Property(p => p.FlowKey).HasColumnName("flow_key");
            e.Property(p => p.Version).HasColumnName("version");
            e.Property(p => p.FlowContent).HasColumnName("flow_content").HasColumnType("text");
            e.Property(p => p.Status).HasColumnName("status");
        });

        modelBuilder.Entity<TokenEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.TokenValue).HasColumnName("token_value");
            e.Property(p => p.TokenName).HasColumnName("token_name");
            e.Property(p => p.ExpiredAt).HasColumnName("expired_at");
            e.Property(p => p.Status).HasColumnName("status");
        });

        modelBuilder.Entity<DataSourceEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.DsName).HasColumnName("ds_name");
            e.Property(p => p.DsType).HasColumnName("ds_type");
            e.Property(p => p.Host).HasColumnName("host");
            e.Property(p => p.Port).HasColumnName("port");
            e.Property(p => p.DbName).HasColumnName("db_name");
            e.Property(p => p.Username).HasColumnName("username");
            e.Property(p => p.Password).HasColumnName("password");
        });

        modelBuilder.Entity<FlowLogEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.FlowKey).HasColumnName("flow_key");
            e.Property(p => p.FlowName).HasColumnName("flow_name");
            e.Property(p => p.Version).HasColumnName("version");
            e.Property(p => p.TriggerType).HasColumnName("trigger_type");
            e.Property(p => p.Status).HasColumnName("status");
            e.Property(p => p.StartTime).HasColumnName("start_time");
            e.Property(p => p.EndTime).HasColumnName("end_time");
            e.Property(p => p.CostMs).HasColumnName("cost_ms");
            e.Property(p => p.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
            e.Property(p => p.InputJson).HasColumnName("input_json").HasColumnType("text");
            e.Property(p => p.OutputJson).HasColumnName("output_json").HasColumnType("text");
        });

        modelBuilder.Entity<FlowNodeLogEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.FlowLogId).HasColumnName("flow_log_id");
            e.Property(p => p.NodeKey).HasColumnName("node_key");
            e.Property(p => p.NodeLabel).HasColumnName("node_label");
            e.Property(p => p.NodeType).HasColumnName("node_type");
            e.Property(p => p.SeqNo).HasColumnName("seq_no");
            e.Property(p => p.Status).HasColumnName("status");
            e.Property(p => p.StartTime).HasColumnName("start_time");
            e.Property(p => p.EndTime).HasColumnName("end_time");
            e.Property(p => p.CostMs).HasColumnName("cost_ms");
            e.Property(p => p.InputSnapshot).HasColumnName("input_snapshot").HasColumnType("text");
            e.Property(p => p.OutputSnapshot).HasColumnName("output_snapshot").HasColumnType("text");
            e.Property(p => p.Detail).HasColumnName("detail").HasColumnType("text");
            e.Property(p => p.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
        });

        modelBuilder.Entity<StaticVariableEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.VarCode).HasColumnName("var_code");
            e.Property(p => p.VarName).HasColumnName("var_name");
            e.Property(p => p.DataType).HasColumnName("data_type");
            e.Property(p => p.Value).HasColumnName("value");
            e.Property(p => p.DefaultValue).HasColumnName("default_value");
            e.Property(p => p.Description).HasColumnName("description");
            e.Property(p => p.GroupName).HasColumnName("group_name");
        });

        modelBuilder.Entity<TokenPermissionEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.TokenId).HasColumnName("token_id");
            e.Property(p => p.PermissionType).HasColumnName("permission_type");
            e.Property(p => p.ResourceKey).HasColumnName("resource_key");
            e.Property(p => p.ResourceName).HasColumnName("resource_name");
        });

        modelBuilder.Entity<ScheduleTaskEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.FlowKey).HasColumnName("flow_key");
            e.Property(p => p.FlowName).HasColumnName("flow_name");
            e.Property(p => p.CronExpression).HasColumnName("cron_expression");
            e.Property(p => p.InputJson).HasColumnName("input_json");
            e.Property(p => p.Status).HasColumnName("status");
            e.Property(p => p.LastRunTime).HasColumnName("last_run_time");
            e.Property(p => p.LastRunStatus).HasColumnName("last_run_status");
            e.Property(p => p.NextRunTime).HasColumnName("next_run_time");
            e.Property(p => p.RunCount).HasColumnName("run_count");
        });

        modelBuilder.Entity<WebhookEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.WebhookKey).HasColumnName("webhook_key");
            e.Property(p => p.WebhookName).HasColumnName("webhook_name");
            e.Property(p => p.FlowKey).HasColumnName("flow_key");
            e.Property(p => p.FlowName).HasColumnName("flow_name");
            e.Property(p => p.Secret).HasColumnName("secret");
            e.Property(p => p.AllowedMethod).HasColumnName("allowed_method");
            e.Property(p => p.AsyncMode).HasColumnName("async_mode");
            e.Property(p => p.Status).HasColumnName("status");
            e.Property(p => p.TriggerCount).HasColumnName("trigger_count");
            e.Property(p => p.LastTriggerTime).HasColumnName("last_trigger_time");
            e.Property(p => p.Remark).HasColumnName("remark");
        });

        modelBuilder.Entity<SystemConfigEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.ConfigKey).HasColumnName("config_key");
            e.Property(p => p.ConfigValue).HasColumnName("config_value");
            e.Property(p => p.ConfigName).HasColumnName("config_name");
            e.Property(p => p.ConfigGroup).HasColumnName("config_group");
            e.Property(p => p.Remark).HasColumnName("remark");
        });

        modelBuilder.Entity<FlowTestCaseEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.FlowKey).HasColumnName("flow_key");
            e.Property(p => p.CaseName).HasColumnName("case_name");
            e.Property(p => p.InputJson).HasColumnName("input_json").HasColumnType("text");
            e.Property(p => p.AssertJson).HasColumnName("assert_json").HasColumnType("text");
            e.Property(p => p.LastRunStatus).HasColumnName("last_run_status");
            e.Property(p => p.LastRunTime).HasColumnName("last_run_time");
            e.Property(p => p.LastRunResult).HasColumnName("last_run_result").HasColumnType("text");
            e.Property(p => p.Remark).HasColumnName("remark");
        });

        // 初始数据
        modelBuilder.Entity<TenantEntity>().HasData(new TenantEntity
        {
            Id = 1, TenantName = "默认租户", TenantCode = "default", Status = 1,
            MenuKeys = "[]",
            Deleted = 0, CreatedAt = "2026-01-01T00:00:00"
        });
        modelBuilder.Entity<RoleEntity>().HasData(new RoleEntity
        {
            Id = 1, RoleName = "超级管理员", RoleCode = "admin",
            Remark = "拥有所有权限", TenantId = 1,
            Deleted = 0, CreatedAt = "2026-01-01T00:00:00"
        });
        modelBuilder.Entity<UserEntity>().HasData(new UserEntity
        {
            Id = 1,
            UserName = "juggle",
            Password = "24cb6bcbc65730e9650745d379613563", // juggle MD5
            RoleId = 1,
            TenantId = 1,
            Deleted = 0,
            CreatedAt = "2026-01-01T00:00:00"
        });

        // 角色列映射
        modelBuilder.Entity<RoleEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.RoleName).HasColumnName("role_name");
            e.Property(p => p.RoleCode).HasColumnName("role_code");
            e.Property(p => p.Remark).HasColumnName("remark");
            e.Property(p => p.TenantId).HasColumnName("tenant_id");
        });
        modelBuilder.Entity<RoleMenuEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.RoleId).HasColumnName("role_id");
            e.Property(p => p.MenuKey).HasColumnName("menu_key");
        });
        modelBuilder.Entity<TenantEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.TenantName).HasColumnName("tenant_name");
            e.Property(p => p.TenantCode).HasColumnName("tenant_code");
            e.Property(p => p.Remark).HasColumnName("remark");
            e.Property(p => p.Status).HasColumnName("status");
            e.Property(p => p.ExpiredAt).HasColumnName("expired_at");
            e.Property(p => p.MenuKeys).HasColumnName("menu_keys").HasColumnType("text");
        });
        modelBuilder.Entity<AuditLogEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.Module).HasColumnName("module");
            e.Property(p => p.ActionType).HasColumnName("action_type");
            e.Property(p => p.TargetId).HasColumnName("target_id");
            e.Property(p => p.ChangeContent).HasColumnName("change_content").HasColumnType("text");
            e.Property(p => p.OperatorName).HasColumnName("operator_name");
            e.Property(p => p.OperatorId).HasColumnName("operator_id");
            e.Property(p => p.OperatorTenantId).HasColumnName("operator_tenant_id");
        });
        modelBuilder.Entity<LoginLogEntity>(e => {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Deleted).HasColumnName("deleted");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.UpdatedBy).HasColumnName("updated_by");
            e.Property(p => p.UserId).HasColumnName("user_id");
            e.Property(p => p.UserName).HasColumnName("user_name");
            e.Property(p => p.LoginType).HasColumnName("login_type");
            e.Property(p => p.Result).HasColumnName("result");
            e.Property(p => p.IpAddress).HasColumnName("ip_address");
            e.Property(p => p.UserAgent).HasColumnName("user_agent").HasColumnType("text");
        });

        // ── 统一补充 tenant_id 列名映射（BaseEntity 新增的字段，各实体配置 block 尚未覆盖的）──
        // 以下实体的 block 中未显式配置 TenantId 列名，统一补上
        void AddTenantIdCol<T>() where T : BaseEntity
            => modelBuilder.Entity<T>().Property(p => p.TenantId).HasColumnName("tenant_id");

        AddTenantIdCol<FlowDefinitionEntity>();
        AddTenantIdCol<FlowInfoEntity>();
        AddTenantIdCol<FlowVersionEntity>();
        AddTenantIdCol<FlowLogEntity>();
        AddTenantIdCol<FlowNodeLogEntity>();
        AddTenantIdCol<FlowTestCaseEntity>();
        AddTenantIdCol<DataSourceEntity>();
        AddTenantIdCol<StaticVariableEntity>();
        AddTenantIdCol<ScheduleTaskEntity>();
        AddTenantIdCol<WebhookEntity>();
        AddTenantIdCol<TokenEntity>();
        AddTenantIdCol<TokenPermissionEntity>();
        AddTenantIdCol<SuiteEntity>();
        AddTenantIdCol<ApiEntity>();
        AddTenantIdCol<ParameterEntity>();
        AddTenantIdCol<ObjectEntity>();
        AddTenantIdCol<VariableInfoEntity>();
        AddTenantIdCol<SystemConfigEntity>();
        AddTenantIdCol<RoleMenuEntity>();
        AddTenantIdCol<TenantEntity>();
        AddTenantIdCol<AuditLogEntity>();
        AddTenantIdCol<LoginLogEntity>();
        // UserEntity / RoleEntity 已在各自 block 里配置 TenantId，跳过

        // ── 全局查询过滤器（租户隔离）────────────────────────────────────────
        // CurrentTenantId 属性动态读取当前 HTTP 请求的租户 ID：
        //   null → 超管 / 匿名 / 后台任务，不过滤
        //   有值 → 只看本租户数据（严格隔离）或本租户+全局（宽松隔离）

        // 严格隔离：只有本租户的数据才可见（TenantId == current，null 行不可见）
        modelBuilder.Entity<FlowDefinitionEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<FlowInfoEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<FlowVersionEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<FlowLogEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<FlowNodeLogEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<FlowTestCaseEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<DataSourceEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<StaticVariableEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ScheduleTaskEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<WebhookEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<TokenEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<TokenPermissionEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ObjectEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<VariableInfoEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);

        // 宽松隔离：TenantId=null 的全局数据（官方套件、全局角色）对所有租户可见
        modelBuilder.Entity<RoleEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<SuiteEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ApiEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ParameterEntity>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == null || e.TenantId == CurrentTenantId);
    }
}
