using Juggle.Application.Services;
using Juggle.Application.Services.Impl;
using Juggle.Domain.Engine;
using Juggle.Infrastructure.Persistence;
using Juggle.Application.Services.Flow;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

var builder = WebApplication.CreateBuilder(args);

// ==================== 服务注册 ====================

// JSON 配置（camelCase 输出，中文不转义）
builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.PropertyNamingPolicy        = JsonNamingPolicy.CamelCase;
    opts.JsonSerializerOptions.DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    opts.JsonSerializerOptions.Encoder                     = JavaScriptEncoder.Create(UnicodeRanges.All);
});

// ==================== 数据库配置（支持多种数据库） ====================
// 优先级：环境变量 > appsettings.json > 默认值
// 环境变量：DB_TYPE=sqlite|sqlserver|mysql|postgresql，DB_CONNECTION_STRING 直接指定连接串
// 配置文件：appsettings.json → Database 节
var dbSection = builder.Configuration.GetSection("Database");
var dbType    = (Environment.GetEnvironmentVariable("DB_TYPE") ?? dbSection["Type"] ?? "sqlite").ToLower();
var dbConnStr = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ?? dbSection["ConnectionString"] ?? "";

// 辅助：从配置或环境变量读取值
string Cfg(string configKey, string envKey, string defaultValue)
    => Environment.GetEnvironmentVariable(envKey) ?? dbSection[configKey] ?? defaultValue;

void ConfigureDbContext(DbContextOptionsBuilder opts)
{
    switch (dbType)
    {
        case "sqlserver" or "mssql":
            var sqlServerConn = !string.IsNullOrEmpty(dbConnStr) ? dbConnStr
                : $"Server={Cfg("SqlServer:Host","DB_HOST","localhost")},{Cfg("SqlServer:Port","DB_PORT","1433")};"
                + $"Database={Cfg("SqlServer:Database","DB_NAME","juggle")};"
                + $"User Id={Cfg("SqlServer:UserId","DB_USER","sa")};"
                + $"Password={Cfg("SqlServer:Password","DB_PASS","Juggle@2026")};"
                + $"TrustServerCertificate={(Cfg("SqlServer:TrustServerCertificate","","true").ToLower() == "true" ? "True" : "False")};";
            opts.UseSqlServer(sqlServerConn);
            break;
        case "mysql":
            var mysqlConn = !string.IsNullOrEmpty(dbConnStr) ? dbConnStr
                : $"Server={Cfg("MySql:Host","DB_HOST","localhost")};"
                + $"Port={Cfg("MySql:Port","DB_PORT","3306")};"
                + $"Database={Cfg("MySql:Database","DB_NAME","juggle")};"
                + $"User={Cfg("MySql:UserId","DB_USER","root")};"
                + $"Password={Cfg("MySql:Password","DB_PASS","juggle")};"
                + $"CharSet={Cfg("MySql:CharSet","","utf8mb4")};";
            opts.UseMySql(mysqlConn, ServerVersion.AutoDetect(mysqlConn),
                mySql => { mySql.EnableRetryOnFailure(3); });
            break;
        case "postgresql" or "postgres":
            var pgConn = !string.IsNullOrEmpty(dbConnStr) ? dbConnStr
                : $"Host={Cfg("PostgreSql:Host","DB_HOST","localhost")};"
                + $"Port={Cfg("PostgreSql:Port","DB_PORT","5432")};"
                + $"Database={Cfg("PostgreSql:Database","DB_NAME","juggle")};"
                + $"Username={Cfg("PostgreSql:UserId","DB_USER","postgres")};"
                + $"Password={Cfg("PostgreSql:Password","DB_PASS","juggle")};";
            opts.UseNpgsql(pgConn);
            break;
        default: // sqlite
            var dbPath = !string.IsNullOrEmpty(dbConnStr) ? dbConnStr
                : Environment.GetEnvironmentVariable("DB_PATH")
                ?? dbSection["Sqlite:DataSource"]
                ?? "juggle.db";
            if (!Path.IsPathRooted(dbPath))
                dbPath = Path.Combine(Directory.GetCurrentDirectory(), dbPath);
            opts.UseSqlite($"Data Source={dbPath}");
            break;
    }
}

/// <summary>隐藏连接字符串中的密码部分</summary>
static string MaskPassword(string connStr)
{
    if (string.IsNullOrEmpty(connStr)) return connStr;
    return System.Text.RegularExpressions.Regex.Replace(
        connStr,
        @"(Password|PWD|User Id.*?Password)\s*=\s*[^;]+",
        m => m.Value.Contains('=') ? m.Value.Substring(0, m.Value.IndexOf('=') + 1) + "***" : m.Value,
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
    );
}

// 注册 IHttpContextAccessor（DbContext 内部读取当前 HTTP 请求的租户信息）
builder.Services.AddHttpContextAccessor();

// 注册 ICurrentTenantProvider：从 JWT Claims 动态获取当前租户 ID
builder.Services.AddScoped<Juggle.Infrastructure.Persistence.ICurrentTenantProvider,
    Juggle.Api.Services.HttpContextTenantProvider>();

// 注册 DbContext：EF Core DI 会自动注入 ICurrentTenantProvider（因为它已注册为 Scoped）
builder.Services.AddDbContext<JuggleDbContext>((sp, opts) =>
{
    ConfigureDbContext(opts);
});
// 说明：JuggleDbContext 有两个构造函数：
//   (DbContextOptions)              → 用于 EnsureCreated / 测试
//   (DbContextOptions, ICurrentTenantProvider) → 用于 HTTP 请求（EF Core DI 自动选择参数最多的）

// 如果不是 SQLite，则需要在启动时确保数据库已迁移
// SQLite 使用 EnsureCreated，其他数据库使用 Migrate 或 EnsureCreated

// JWT 认证
var jwtKey = builder.Configuration["Jwt:Key"] ?? "JuggleNet6SecretKey2026!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer           = true,
            ValidIssuer              = "JuggleNet6",
            ValidateAudience         = true,
            ValidAudience            = "JuggleNet6",
            ValidateLifetime         = true
        };
        // 统一返回 JSON 格式的 401，方便前端处理
        opts.Events = new JwtBearerEvents
        {
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode  = 401;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    JsonSerializer.Serialize(
                        new { code = 401, message = "未授权，请先登录", data = (object?)null },
                        new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                        }
                    ));
            }
        };
    });

builder.Services.AddAuthorization();

// HttpClient（供流程引擎和 API 调试使用）
builder.Services.AddHttpClient();

// 流程引擎（Scoped，因依赖 HttpClientFactory）
builder.Services.AddScoped<FlowEngine>();

// 定时任务调度器（后台服务）
builder.Services.AddHostedService<Juggle.Api.Services.ScheduleTaskService>();

// ── 业务 Service 层 ──────────────────────────────────────────────
builder.Services.AddScoped<FlowExecutionService>();  // 流程执行核心（数据源、静态变量、日志）
builder.Services.AddScoped<DataSourceService>();     // 数据源连接字符串构建 + 连接测试
builder.Services.AddScoped<JwtService>();            // JWT Token 签发
builder.Services.AddScoped<ITenantAccessor, TenantAccessor>();  // 多租户上下文

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Juggle Net8 API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Format: Bearer {token}",
        Name        = "Authorization",
        In          = ParameterLocation.Header,
        Type        = SecuritySchemeType.ApiKey,
        Scheme      = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// CORS（开发环境允许前端跨域）
builder.Services.AddCors(opts =>
{
    opts.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ==================== 应用构建 ====================
var app = builder.Build();

// 启动日志：显示数据库类型
Console.WriteLine($"[Juggle] 系统数据库类型: {dbType.ToUpper()}");
if (!string.IsNullOrEmpty(dbConnStr))
    Console.WriteLine($"[Juggle] 连接字符串: {MaskPassword(dbConnStr)}");

// ==================== 数据库初始化 ====================
// SQLite: EnsureCreated 自动建表
// SQLServer/MySQL/PostgreSQL: EnsureCreated 自动建表（生产环境建议改用 Migration）
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JuggleDbContext>();
    db.Database.EnsureCreated();

    // 补建迭代新增字段/表（仅 SQLite 需要手动 ALTER TABLE，其他数据库由 Migration 处理）
    if (dbType == "sqlite")
    {
        // 补建流程定义分组字段
        try { db.Database.ExecuteSqlRaw("ALTER TABLE t_flow_definition ADD COLUMN group_name TEXT DEFAULT NULL;"); }
        catch { /* 列已存在则忽略 */ }
        // 补建流程定义访问别名字段
        try { db.Database.ExecuteSqlRaw("ALTER TABLE t_flow_definition ADD COLUMN service_alias TEXT DEFAULT NULL;"); }
        catch { /* 列已存在则忽略 */ }
        // 补建 API WebService 字段
        try { db.Database.ExecuteSqlRaw("ALTER TABLE t_api ADD COLUMN soap_version TEXT DEFAULT NULL;"); } catch { }
        try { db.Database.ExecuteSqlRaw("ALTER TABLE t_api ADD COLUMN soap_method TEXT DEFAULT NULL;"); } catch { }
        try { db.Database.ExecuteSqlRaw("ALTER TABLE t_api ADD COLUMN soap_namespace TEXT DEFAULT NULL;"); } catch { }
        try { db.Database.ExecuteSqlRaw("ALTER TABLE t_api ADD COLUMN soap_action TEXT DEFAULT NULL;"); } catch { }
        try { db.Database.ExecuteSqlRaw("ALTER TABLE t_api ADD COLUMN service_alias TEXT DEFAULT NULL;"); } catch { }
        try { db.Database.ExecuteSqlRaw("ALTER TABLE t_api ADD COLUMN status INTEGER DEFAULT 1;"); } catch { }
        // 告警规则/记录表
        try { db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS t_alert_rule(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, metric_type TEXT, condition TEXT, threshold REAL, channel TEXT, recipients TEXT, description TEXT, status INTEGER DEFAULT 1, created_at TEXT, updated_at TEXT, deleted INTEGER DEFAULT 0);"); } catch { }
        try { db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS t_alert_record(id INTEGER PRIMARY KEY AUTOINCREMENT, rule_id INTEGER, rule_name TEXT, metric_type TEXT, message TEXT, detail TEXT, status TEXT DEFAULT 'triggered', created_at TEXT, deleted INTEGER DEFAULT 0);"); } catch { }
        // 补建参数位置字段
        try { db.Database.ExecuteSqlRaw("ALTER TABLE t_parameter ADD COLUMN param_position TEXT DEFAULT NULL;"); } catch { }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // 生产/容器环境同样开启 Swagger（可按需关闭）
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Juggle Net8 API v1"));
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// 多租户中间件：从 JWT Claims 中提取用户信息注入 ITenantAccessor
app.Use(async (ctx, next) =>
{
    var tenantAccessor = ctx.RequestServices.GetRequiredService<ITenantAccessor>();
    // 开放接口（/open/）和健康检查不设置租户
    if (!ctx.Request.Path.StartsWithSegments("/open") && ctx.Request.Path != "/api/health")
    {
        tenantAccessor.LoadFromClaims(ctx.User);
    }
    await next();
});

// 前端静态文件（生产环境）
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// 健康检查端点（供 Docker healthcheck / 负载均衡探针使用）
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }))
   .AllowAnonymous();

// SPA fallback（前端路由）
app.MapFallbackToFile("index.html");

app.Run();
