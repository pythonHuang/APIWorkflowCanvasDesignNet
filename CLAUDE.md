# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在此仓库中工作时提供指引。

## 构建与运行命令

```bash
# 后端 (ASP.NET Core 8) — 运行在 http://localhost:9127
cd Juggle.Api && dotnet run

# 还原并构建整个解决方案
dotnet restore Juggle.sln && dotnet build Juggle.sln

# 前端 (Vue3 + Vite) — 运行在 http://localhost:5173
cd JuggleNet6.Frontend && npm install && npm run dev

# 前端生产构建 (输出 → Juggle.Api/wwwroot/)
cd JuggleNet6.Frontend && npm run build

# Docker
docker-compose up -d                                   # 完整部署
docker build -t pythonhuang/juggle-net8:v1.0 .         # 多阶段构建

# Windows 一键启动脚本 (交互式菜单)
启动Juggle.bat
```

默认账号: `juggle` / `juggle`

## 架构（DDD 四层）

```
Juggle.Api/              # 表示层 — Controllers + Program.cs
Juggle.Application/      # 应用层 — DTO (Request/Response)、FlowExecutionService、JwtService
Juggle.Domain/           # 领域层 — 实体、流程引擎 (FlowEngine + 13个节点执行器)
Juggle.Infrastructure/   # 基础设施层 — JuggleDbContext、JsonHelper、Md5Helper
JuggleNet6.Frontend/     # Vue 3 + Element Plus + @vue-flow/core + Monaco Editor
```

**依赖方向**: `Api → Application → Domain ← Infrastructure`（Api 引用所有三个项目；Application 和 Domain 不引用 Infrastructure）。

项目引用关系: `Juggle.Api.csproj` 引用其他三个项目；`Juggle.Infrastructure.csproj` 和 `Juggle.Application.csproj` 都引用 `Juggle.Domain.csproj`。

## Controller 模式

Controller 直接注入 `JuggleDbContext`——CRUD 操作**不走 Service 层**。只有复杂编排逻辑（流程执行、数据源连接、JWT 签发）才抽取到 `Juggle.Application/Services/` 下的 Service 中。所有 Controller 返回 `ApiResult`（code/message/data）或 `PageResult<T>`（code/message/total/data）。

## 流程引擎（系统核心）

流程引擎（`Juggle.Domain/Engine/FlowEngine.cs`）执行 JSON 格式的节点图。以 **Scoped** 生命周期注册在 DI 中，接收 `IHttpClientFactory` + 可选的数据源/静态变量字典。

**13 种节点类型**: START、END、METHOD（HTTP调用）、CONDITION（分支判断）、MERGE（分支汇聚）、ASSIGN（赋值）、CODE（JS脚本）、MYSQL/DB（SQL执行）、SUB_FLOW（递归调用子流程）、LOOP（循环）、DELAY（延迟）、PARALLEL（并行）、NOTIFY（通知）。

**变量命名约定**（理解数据流转的关键）:

- `input_*` — 流程入参（来自调用方）
- `output_*` — 流程出参（返回给调用方）
- `env_*` — 内部中间变量（流程内使用）
- `_loop_item`、`_loop_index`、`_loop_total`、`_loop_results` — 循环内置变量

**METHOD 节点数据流转**: `InputFillRules` 在调用前将变量映射到 API 入参；`OutputFillRules` 在调用后将 API 响应映射回变量。

`FlowExecutionService`（`Juggle.Application/Services/Flow/`）是主要的编排器，负责加载数据源、快照静态变量、运行引擎、持久化日志、回写静态变量变更。

## 多租户数据隔离

`ICurrentTenantProvider`（定义在 Infrastructure 层，在 Api 层由 `HttpContextTenantProvider` 实现）从 JWT Claims 中提取租户 ID。`JuggleDbContext` 通过**全局查询过滤器**（`HasQueryFilter`）使用它：

- **严格隔离**（`e.TenantId == CurrentTenantId`）：流程、数据源、Token、静态变量等
- **宽松隔离**（`e.TenantId == null || e.TenantId == CurrentTenantId`）：套件、API、参数、角色——TenantId 为 null 的记录为"全局"数据，对所有租户可见

`ITenantAccessor`（与 `ICurrentTenantProvider` 不同）是应用层的租户上下文，供 Controller 使用；`Program.cs` 中的中间件从 JWT Claims 加载它（`/open/` 和 `/api/health` 路径跳过）。

## 数据库配置

系统数据库通过 `DB_TYPE` 环境变量（或 appsettings 中的 `Database:Type`）选择。支持: `sqlite`（默认）、`mysql`、`postgresql`、`sqlserver`。参见 `Program.cs:39-82` 的 `ConfigureDbContext` 分支逻辑。

业务数据源（供 DB/MySQL 节点使用）通过界面在"系统设置 → 数据源"中配置——支持 6 种类型: SQLite、MySQL、PostgreSQL、SQLServer、Oracle、达梦。

## API URL 规范


| 前缀          | 认证方式              | 用途            |
| ------------- | --------------------- | --------------- |
| `/api/`       | JWT Bearer            | 管理控制台      |
| `/open/`      | X-Access-Token 请求头 | 外部流程触发    |
| `/api/health` | 无                    | Docker 健康检查 |

## 关键环境变量

- `DB_TYPE` — 系统数据库类型 (sqlite/mysql/postgresql/sqlserver)
- `DB_CONNECTION_STRING` — 完整连接字符串（覆盖各独立参数）
- `DB_PATH` — SQLite 文件路径
- `DB_HOST`、`DB_PORT`、`DB_NAME`、`DB_USER`、`DB_PASS` — 各独立数据库参数
- `Jwt__Key` — JWT 签名密钥（≥32 字符）
- `ASPNETCORE_URLS` — 监听地址（默认 `http://+:9127`）

## 编码约定

- **C# DTO**: 使用 `record` 类型
- **EF Core**: Code First + `EnsureCreated()`（非 Migration）；列名通过 `HasColumnName` 映射为 snake_case
- **JSON**: camelCase 输出，中文不转义
- **日期**: 以 ISO 8601 字符串存储在 TEXT 列中
- **软删除**: 所有实体使用 `deleted` 列（0/1）
- **前端**: `<script setup>` 语法（Composition API），API 调用通过 service 层文件，使用 Pinia 管理状态
- **流程内容**: 以 JSON 字符串存储在 `flow_content` 列（TEXT 类型）

Git 仓库

- **仓库地址**：`https://github.com/pythonHuang/JuggleNet6.git`
- **本地路径**：`d:\WorkBuddyMyWorkSpace\Juggle接口编排Net6\JuggleNet6\`（子目录独立 Git 仓库）
- **用户偏好**：每完成一个 Bug 修复或需求开发，立即单独 git commit + push GitHub（不攒多个）
- **.gitignore 已忽略**：`*.db / *.db-shm / *.db-wal / wwwroot/ / node_modules/ / dist/`
