# Juggle.Api — 后端入口层

> ASP.NET Core 8 Web API · DDD 四层架构 · JWT 认证 · SQLite

---

## 项目概述

`Juggle.Api` 是 Juggle 接口编排平台的 **API 入口层**，负责：

- 接收并路由所有 HTTP 请求
- 参数校验与权限认证
- 调用应用层服务处理业务逻辑
- 返回统一 JSON 响应格式
- 托管前端静态文件（生产模式）

---

## 目录结构

```
Juggle.Api/
├── Controllers/
│   ├── Api/
│   │   ├── ApiController.cs              # API 接口 CRUD、调试、Swagger 解析
│   │   ├── DataSourceController.cs       # 数据源管理（增删改查 + 连接测试）
│   │   ├── FlowDefinitionController.cs   # 流程定义 CRUD + 调试 + 部署
│   │   ├── FlowInfoController.cs         # 流程信息管理（已部署流程）
│   │   ├── FlowLogController.cs          # 流程执行日志查询
│   │   ├── FlowVersionController.cs      # 流程版本管理 + 触发
│   │   ├── ObjectController.cs           # 自定义对象管理
│   │   ├── ParameterController.cs        # 参数管理（入参/出参/Header）
│   │   ├── StaticVariableController.cs   # 全局静态变量管理
│   │   ├── SuiteController.cs            # 套件管理
│   │   ├── TokenController.cs            # 访问令牌管理
│   │   ├── UserController.cs             # 用户登录
│   │   └── VariableInfoController.cs     # 流程变量信息管理
│   └── Open/
│       └── FlowOpenController.cs         # 开放接口（外部 GET/POST 触发流程）
├── wwwroot/                              # 前端构建产物（Vue3 静态文件）
├── juggle.db                             # SQLite 数据库文件
├── appsettings.json                      # 应用配置
├── appsettings.Development.json          # 开发环境配置
├── Program.cs                            # 应用入口 + 服务注册
└── Juggle.Api.csproj
```

---

## 启动与运行

### 前置要求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 开发模式启动

```powershell
cd Juggle.Api
dotnet run
```

默认监听端口：**9127**

| 地址 | 说明 |
|------|------|
| http://localhost:9127/ | 应用首页（前端页面） |
| http://localhost:9127/swagger | Swagger API 文档 |

### 发布

```powershell
dotnet publish -c Release -o ./publish
```

---

## 配置说明

### appsettings.json

```json
{
  "Jwt": {
    "Key": "JuggleNet6SecretKey2026!"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

- `Jwt:Key`：JWT 签名密钥，生产环境请修改为强密码

### 数据库

数据库文件 `juggle.db` 自动创建于 **程序运行目录**（即 `Juggle.Api/`），启动时通过 `EnsureCreated()` 自动建表，无需手动执行迁移。

---

## API 路由规范

### 认证方式

| 路由前缀 | 认证方式 | 说明 |
|---------|---------|------|
| `/api/*` | JWT Bearer Token | 控制台管理接口，需登录 |
| `/open/*` | API Token（Header/Query） | 开放接口，供外部调用流程 |

登录获取 JWT Token：
```
POST /api/system/user/login
Body: { "username": "juggle", "password": "juggle" }
```

后续请求携带：
```
Authorization: Bearer <token>
```

### 统一响应格式

```json
{
  "code": 200,
  "message": "success",
  "data": { ... }
}
```

| code | 说明 |
|------|------|
| 200 | 成功 |
| 400 | 参数错误 |
| 401 | 未授权（未登录或 Token 过期） |
| 500 | 服务器内部错误 |

---

## 主要接口列表

### 用户认证

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/system/user/login` | 用户登录，返回 JWT Token |

### 流程定义

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/flow/definition/add` | 创建流程定义 |
| PUT | `/api/flow/definition/update` | 修改流程定义 |
| DELETE | `/api/flow/definition/delete/{id}` | 删除流程定义 |
| GET | `/api/flow/definition/info/{id}` | 查询流程定义详情 |
| POST | `/api/flow/definition/page` | 分页查询 |
| PUT | `/api/flow/definition/save` | 保存流程画布内容 |
| POST | `/api/flow/definition/debug/{flowKey}` | 调试流程（返回节点日志） |
| POST | `/api/flow/definition/deploy` | 部署流程 |

### 流程版本

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/flow/version/page` | 分页查询版本列表 |
| PUT | `/api/flow/version/status` | 启用/禁用版本 |
| DELETE | `/api/flow/version/delete/{id}` | 删除版本 |
| GET | `/api/flow/version/latest/{flowKey}` | 获取最新版本号 |

### 套件 & 接口

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/suite/add` | 创建套件 |
| POST | `/api/suite/list` | 查询套件列表 |
| POST | `/api/suite/api/add` | 创建 API 接口 |
| POST | `/api/suite/api/list` | 查询接口列表 |
| POST | `/api/suite/api/debug` | 调试 API 接口 |

### 系统管理

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/system/datasource/add` | 新增数据源 |
| GET | `/api/system/datasource/list` | 数据源列表 |
| POST | `/api/system/datasource/test/{id}` | 测试数据源连接 |
| POST | `/api/system/datasource/metadata` | 表/视图/存储过程列表（SQL 设计辅助） |
| POST | `/api/system/datasource/columns` | 表/视图字段列表 |
| POST | `/api/system/datasource/test-sql` | 单独测试 SQL（查询预览/更改事务回滚） |
| POST | `/api/system/token/add` | 新增访问令牌 |
| GET | `/api/system/token/list` | 令牌列表 |
| GET | `/api/system/variable/list` | 全局静态变量列表 |
| POST | `/api/system/variable/save` | 保存静态变量值 |

### 开放接口（外部触发）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/open/flow/trigger/{version}/{key}` | GET 方式触发流程 |
| POST | `/open/flow/trigger/{version}/{key}` | POST 方式触发流程 |

---

## 项目依赖

主要 NuGet 包：

| 包 | 版本 | 用途 |
|----|------|------|
| Microsoft.EntityFrameworkCore.Sqlite | 8.x | SQLite ORM |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.x | JWT 认证 |
| Swashbuckle.AspNetCore | 6.x | Swagger 文档 |
| MySqlConnector | 2.3.7 | MySQL 数据库节点 |
| Npgsql | 8.0.3 | PostgreSQL 数据库节点 |
| Microsoft.Data.SqlClient | 5.2.1 | SQL Server 数据库节点 |

---

## 依赖的层

```
Juggle.Api
  └─ 引用 Juggle.Application
       └─ 引用 Juggle.Domain
            └─ 引用 Juggle.Infrastructure
```

- **Juggle.Domain** — 15个领域实体 + FlowEngine 流程引擎 + 节点执行器 + ExpressionEvaluator 表达式引擎
- **Juggle.Infrastructure** — JuggleDbContext（EF Core）+ JsonHelper + Md5Helper
- **Juggle.Application** — FlowExecutionService + DataSourceService + JwtService + 请求/响应 DTO

---

## 更新日志

### v1.8（最新）

- 🧮 **表达式引擎** — 新增 `ExpressionEvaluator`（Juggle.Domain/Engine），条件/赋值节点共用：算术 + - * / %、字符串拼接/切片/replace、toString 数字与日期格式化
- 🗄️ **数据库节点 SQL 辅助** — 新增 `metadata` / `columns` / `test-sql` 端点（表/视图/存储过程浏览、字段列表、SQL 单独测试——更改操作事务回滚不污染数据）
- 📊 **报表模块** — 数据视图 + 报表设计器 + 公式引擎（SUM/AVG/COUNT/IF 等）
- 📈 **监控模块** — API 拓扑图（健康检查/访问统计/DB 调用连线）+ 告警规则 + 告警记录
- 🔀 **条件节点扩展** — && || 括号、双变量比较、子属性、数组 length/元素取值
- 🔗 **接口直连访问** — 套件接口支持 `/open/api/{code}` 直接调用（Token 授权）+ 访问别名 + 停用/启用
- 🧩 **WSDL 解析增强** — generatedXSD 地址、xsd:include/import schemaLocation 递归加载、XSD 内容粘贴
