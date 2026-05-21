# Juggle接口编排Net6 - 项目长期记忆

## 项目概述
将 Java/Spring Boot 的 Juggle 接口编排系统迁移到 .NET 8 + SQLite + Vue3。

## Git 仓库
- **仓库地址**：`https://github.com/pythonHuang/JuggleNet6.git`
- **本地路径**：`d:\WorkBuddyMyWorkSpace\Juggle接口编排Net6\JuggleNet6\`（子目录独立 Git 仓库）
- **用户偏好**：每完成一个 Bug 修复或需求开发，立即单独 git commit + push GitHub（不攒多个）
- **.gitignore 已忽略**：`*.db / *.db-shm / *.db-wal / wwwroot/ / node_modules/ / dist/`

## 技术栈
- **后端**: ASP.NET Core 8, EF Core + SQLite, JWT认证, Swagger
- **前端**: Vue3 + Vite + Element Plus + Pinia + Vue Router
- **数据库**: SQLite (文件: juggle.db，位于后端运行目录)

## 项目目录
```
d:\WorkBuddyMyWorkSpace\Juggle接口编排Net6\JuggleNet6\
├── Juggle.Api\             ← API层（Controllers + Program.cs）
├── Juggle.Application\     ← 应用层（Services + Request/Response DTOs）
├── Juggle.Infrastructure\  ← 基础设施层（JuggleDbContext + JsonHelper + Md5Helper）
├── Juggle.Domain\          ← 领域层（Entities + FlowEngine + NodeExecutors）
└── JuggleNet6.Frontend\    ← Vue3 前端
```

## 运行方式
```powershell
# 启动后端（端口9127）
cd JuggleNet6\Juggle.Api && dotnet run

# 构建前端并集成
cd JuggleNet6\JuggleNet6.Frontend
npm run build
Copy-Item dist\* ..\Juggle.Api\wwwroot\ -Recurse -Force
```

## 关键URL
- 前端: http://localhost:9127/
- Swagger: http://localhost:9127/swagger
- 默认账号: juggle / juggle

## 重要接口路由
| 功能 | 路由 | 方法 |
|------|------|------|
| 接口列表 | /api/suite/api/list | POST |
| 流程内容保存 | /api/flow/definition/save | PUT |
| 流程导出 | /api/flow/definition/export/{id} | GET |
| 流程导入 | /api/flow/definition/import | POST |
| 流程克隆 | /api/flow/definition/clone/{id} | POST |
| 数据源测试 | /api/system/datasource/test/{id} | POST |
| 系统配置 | /api/system/config/all | GET |
| 用户分页 | /api/user/page | POST |
| 测试用例 | /api/flow/testcase/run/{id} | POST |

## 开放接口
- **触发**：`GET/POST /open/flow/trigger/{key}` + Header: X-Access-Token
- **异步触发**：`POST /open/flow/triggerAsync/{key}` → 返回 `{logId}`
- **Webhook 触发**：`POST/GET /open/webhook/{webhookKey}` + 可选签名

## 节点执行器（13种）
START / END / METHOD / CONDITION / MERGE / ASSIGN / CODE / MYSQL(DB) / SUB_FLOW / LOOP / DELAY / PARALLEL / NOTIFY

## DDD 四层架构
```
Juggle.Api（Controllers）
  ↓
Juggle.Application（FlowExecutionService + DataSourceService + JwtService）
  ↓
Juggle.Infrastructure（JuggleDbContext + JsonHelper + Md5Helper）
  ↓
Juggle.Domain（Entities + FlowEngine + NodeExecutors）
```

## Controllers 清单（Juggle.Api）
- Api/: ApiController, DataSourceController, FlowDefinitionController, FlowInfoController,
        FlowLogController, FlowVersionController, FlowTestCaseController, ObjectController,
        ParameterController, StaticVariableController, SuiteController, ScheduleTaskController,
        SystemConfigController, TokenController, UserController, VariableInfoController, WebhookController
- Open/: FlowOpenController, WebhookTriggerController

## 数据库表
- t_user, t_flow_definition, t_flow_info, t_flow_version, t_flow_log, t_flow_node_log
- t_variable_info, t_static_variable, t_data_source, t_schedule_task
- t_webhook, t_token, t_token_permission, t_suite, t_api, t_parameter, t_object
- t_system_config, t_flow_test_case
- t_role, t_role_menu, t_tenant（需求26），t_audit_log（需求29）

## 前端模块（完整列表）
- flow/: FlowDefinitionList, FlowDesign, FlowInfoList, FlowVersionList, FlowLog, AsyncFlowResult, Dashboard, FlowTestCase（新）
- suite/: SuiteList, ApiList, ApiDetail
- object/: ObjectList, ObjectAttrList
- system/: TokenList, DataSourceList, StaticVariable, ScheduleTask, WebhookList, UserManage（新）, SystemConfig（新）

## 侧边栏菜单（当前结构）
- 流程管理: 流程定义 / 监控仪表盘 / 流程列表 / 执行日志 / 测试用例 / 异步结果查询
- 套件管理: 套件列表
- 对象管理
- 系统设置: Token管理 / 数据源管理 / 静态变量 / 定时任务 / Webhook管理 / 用户管理 / 角色管理 / 租户管理 / 系统配置

## 多租户数据隔离（需求30 - 2026-04-10）
- **BaseEntity** 统一加 `TenantId` 字段，所有业务实体自动继承，原有实体中重复定义的 TenantId 已清理
- **JuggleDbContext**：新增 `ICurrentTenantProvider` 接口（定义在 Infrastructure），API 层的 `HttpContextTenantProvider` 实现（读 JWT Claims，RoleId=1超管返回null不过滤）
- **全局查询过滤器**：严格隔离（FlowDefinition/Log/TestCase/DataSource/StaticVar/ScheduleTask/Webhook/Token/Object/VariableInfo）+ 宽松隔离（Role/Suite/Api/Parameter，TenantId=null全局数据所有租户可见）
- **所有Controllers** 写入时设置 `TenantId = _tenant.TenantId`（FlowDefinition/DataSource/StaticVariable/Webhook/Token/ScheduleTask/Object/Suite + FlowExecutionService日志）
- **SQLite 补列**：所有 23 张表均已 ALTER TABLE 添加 `tenant_id INTEGER` 列（Python 脚本执行）
- **架构设计**：Infrastructure 不依赖 Web（无 Microsoft.AspNetCore.Http 引用），通过 `ICurrentTenantProvider` 接口解耦
- 系统数据库：通过 `DB_TYPE` 环境变量切换 sqlite/sqlserver/mysql/postgresql
- 连接配置：`DB_CONNECTION_STRING` 或各数据库专属环境变量（DB_HOST/DB_PORT/DB_NAME/DB_USER/DB_PASS）
- EF Core 统一升级到 8.0.4（Infrastructure/Application/Domain 三层一致）
- JuggleDbContext 大文本字段已标注 `HasColumnType("text")` 确保跨数据库兼容

## 数据源类型（需求25 - 已支持6种）
- sqlite / mysql / postgresql / sqlserver / oracle / dm(达梦)
- 默认端口：SQLite=0, MySQL=3306, PostgreSQL=5432, SQLServer=1433, Oracle=1521, DM=5236
- NuGet：Oracle.ManagedDataAccess.Core 23.6.0, DM.DmProvider 8.3.1

## test.html 登录机制（Bug修复）
- 新增 JWT 登录弹窗，登录态存储在 localStorage（与主应用共享）
- 列表请求自动携带 Authorization: Bearer {token}
- Access Token 输入框仅用于触发开放接口（X-Access-Token header）

## 已实现的完整功能列表（25个需求）
| # | 功能 | Commit |
|---|------|--------|
| 1-5 | 流程设计器/节点/调试/日志 | 早期 |
| 6 | 异步触发+结果查询 | e46995c |
| 7 | Token权限控制 | 401f2dd |
| 8 | 流程/套件导入导出+Word文档 | 5488ae7/42f2d19 |
| 9 | 流程分组管理 | 59db7a1 |
| 10 | 节点超时+重试 | eb88924 |
| 11 | 定时任务调度 | 3d0ecbd |
| 12 | 监控仪表盘 | cae0dd5 |
| 13 | 接口Mock功能 | 3a2d7a9 |
| 14 | LOOP+DELAY节点 | 2db23fe |
| 15 | PARALLEL并行节点 | 7da02e5 |
| 16 | NOTIFY通知节点 | affc5ad |
| 17 | Webhook触发 | 7e0928d |
| 18 | 流程版本对比 | 669a58a |
| 19 | 流程克隆功能 | a5b07fd |
| 20 | 用户管理 | c69b413 |
| 21 | 系统配置中心 | 7e6de5b |
| 22 | 全局异常告警 | 6d8b716 |
| 23 | 流程测试用例 | 543bbab |
| 24 | Monaco Editor | 054268f |
| 25 | 数据源Oracle/达梦+系统多数据库+test.html登录 | 1bfccd8 |
| 26 | 角色管理 + 菜单权限（RBAC） | 169eb8f |
| 27 | 系统数据库配置文件支持（appsettings.json Database节） | 169eb8f |
| 28 | 多租户实现（TenantEntity + ITenantAccessor 中间件） | 169eb8f |
| 29 | 多租户权限增强（角色归属租户+菜单权限约束+用户编辑+审计日志） | 67acf0b |
| 30 | 完整多租户数据隔离（BaseEntity统一TenantId+DbContext全局过滤器+所有Controllers写入TenantId） | 4485fdc |

## 全局告警机制（v2.0新增）
- 在 FlowExecutionService.RunAsync 末尾，非 debug 触发失败时自动调用 SendAlertAsync
- 从 t_system_config 读取 alert.enabled / alert.on.fail.enabled 配置决定是否告警
- 支持 Webhook POST 和邮件 SMTP 两种告警通道

## Monaco Editor 集成
- 组件: `src/components/MonacoEditor.vue`
- CDN: `https://cdn.jsdelivr.net/npm/monaco-editor@0.55.1/min/vs`
- 已集成: CODE节点（JS高亮）、MYSQL节点（SQL高亮）
- 自动补全: $var.getVariableValue / $var.setVariableValue / $static.* / JSON.*

## 流程测试用例规范
- 断言 JSON 格式：`{"变量名": "期望值"}`
- 批量执行：`POST /api/flow/testcase/runAll/{flowKey}`
- 执行结果含：status / summary / assertResults / outputs / errorMessage / costMs

## Bug修复记录
- **TokenPermission表名映射**：JuggleDbContext.cs 需添加 ToTable + 列名映射（2026-04-02）
- **PARALLEL执行器参数**：FlowNodeLog → NodeLogEntry 类型错误（2026-04-04）
- **alert.body event关键字**：C# 匿名对象不能用 event，改为 eventType（2026-04-04）
- **TryGetValue out var**：在 `if(TryGetValue(..., out var x) == true)` 模式外使用 var 需先初始化
- **SQLite 数据库缺少新增列/表**：项目未用 EF Core Migrations，新增实体字段（ExpiredAt/MenuKeys）和新表（t_login_log/t_audit_log）需手动 ALTER/CREATE 到 juggle.db（2026-04-05）
- **⚠️ 重要**：后续新增实体或字段，务必同步手动更新 SQLite 数据库，否则运行时 500 错误
- **LoginLogEntity TenantId 列名遗漏**：DbContext 中 AddTenantIdCol 统一注册时因注释误导跳过了 LoginLogEntity，导致登录 500（2026-04-10 修复）
- **规则**：每新增实体，必须在 AddTenantIdCol 列表中加一行，注释"已在block配置"的实体需要再次核查

## 编译/构建命令
```powershell
# 后端
Set-Location "...\Juggle.Api"; dotnet build

# 前端
Set-Location "...\JuggleNet6.Frontend"
(& "D:\Program Files\nodejsv20_12\npm.cmd" run build 2>&1) -join "`n"
```
