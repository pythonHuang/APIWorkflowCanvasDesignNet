# Juggle 接口编排平台 - 系统架构文档

> 版本：v1.6  
> 日期：2026-05-19  
> 基于原项目（Java + Spring Boot + Vue3）重构为 .NET 8 + Vue3 + SQLite 技术栈

---

## 一、项目概述

**Juggle** 是一个图形化的微服务接口编排低代码工具，中文寓意"积木、魔法"——像积木一样灵活，像魔法一样强大。通过可视化拖拽流程设计，将多个已有的 API 接口编排为一个复合接口，直接供前端或业务系统调用，极大提高开发效率。

### 1.1 核心价值

| 场景 | 说明 |
|------|------|
| 微服务接口编排 | 基于已有基础接口快速开发新接口，无需重复编码 |
| 第三方系统集成 | 直接配置编排，打通系统壁垒，零代码完成对接 |
| BFF 适配层 | 代替传统 Node.js BFF，为前端提供聚合接口 |
| 私有化定制开发 | 通过编排实现定制逻辑，避免污染标准代码 |

### 1.2 技术栈对比

| 层次 | 原版（Java） | 重构版（.NET 8） |
|------|------------|----------------|
| 后端框架 | Spring Boot 2.7 | ASP.NET Core 8 (Web API) |
| 数据库 | MySQL | SQLite / MySQL / PostgreSQL / SQLServer |
| ORM | MyBatis | Entity Framework Core 8 |
| 前端框架 | Vue 3 | Vue 3 (Composition API) |
| 前端语言 | TypeScript | TypeScript |
| UI 组件库 | Element Plus | Element Plus |
| 代码编辑器 | Monaco Editor | Monaco Editor |
| HTTP 客户端 | Axios | Axios |
| 流程引擎 | Juggle Core (自研 Java) | 自研 C# 流程引擎 |

---

## 二、系统总体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        前端 (Vue 3 + TypeScript)                 │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────────┐ │
│  │ 流程设计器│  │ 套件管理 │  │ 对象管理 │  │  系统设置/市场   │ │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────────┬─────────┘ │
│       └─────────────┴─────────────┴─────────────────┘            │
│                            Axios HTTP                             │
└─────────────────────────────────────────────────────────────────┘
                                  │
                          REST API (HTTP/JSON)
                                  │
┌─────────────────────────────────────────────────────────────────┐
│               后端 (ASP.NET Core 6 Web API)                      │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                      接口层 (Controllers)                    │ │
│  │  /api/flow  /api/suite  /api/object  /api/system  /open     │ │
│  └──────────────────────────┬──────────────────────────────────┘ │
│                              │                                    │
│  ┌───────────────────────────┴──────────────────────────────────┐ │
│  │                    应用层 (Application Services)              │ │
│  │  FlowDefinitionService  ApiService  SuiteService  ...        │ │
│  └──────────────────────────┬──────────────────────────────────┘ │
│                              │                                    │
│  ┌───────────────────────────┴──────────────────────────────────┐ │
│  │                      领域层 (Domain)                          │ │
│  │  FlowEngine  FlowDefinition  Suite  Api  Object  DataSource  │ │
│  └──────────────────────────┬──────────────────────────────────┘ │
│                              │                                    │
│  ┌───────────────────────────┴──────────────────────────────────┐ │
│  │                  基础设施层 (Infrastructure)                   │ │
│  │  EF Core Repositories  SQLite  HTTP Client  JSON处理         │ │
│  └──────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                                  │
                           SQLite 数据库
```

---

## 三、后端分层架构设计

采用 **DDD（领域驱动设计）** 四层架构：

### 3.1 接口层（Interfaces / Controllers）

负责接收 HTTP 请求，参数校验，调用应用层服务，返回统一响应格式。

```
Controllers/
├── Api/
│   ├── FlowDefinitionController    # 流程定义 CRUD、设计、调试、部署
│   ├── FlowInfoController          # 流程信息管理
│   ├── FlowVersionController       # 流程版本管理、触发流程
│   ├── ApiController               # API 接口 CRUD、调试、解析 Swagger
│   ├── SuiteController             # 套件 CRUD、市场管理
│   ├── ObjectController            # 对象管理
│   ├── TokenController             # 访问令牌管理
│   ├── DataSourceController        # 数据源管理
│   ├── UserController              # 用户登录
│   └── DataTypeInfoController      # 数据类型列表
├── Open/
│   ├── FlowOpenController          # 开放接口（触发流程 + 服务别名）
│   └── WebhookTriggerController    # Webhook 触发
└── Example/
    ├── UserExampleController       # 示例用户接口
    ├── GoodsExampleController      # 示例商品接口
    └── OrderExampleController      # 示例订单接口
```

**统一响应格式：**
```json
{
  "code": 200,
  "message": "success",
  "data": { ... }
}
```

### 3.2 应用层（Application Services）

协调领域对象完成业务用例，不包含业务规则。

```
Services/
├── Flow/
│   ├── IFlowDefinitionService      # 流程定义 CRUD + 调试 + 部署
│   ├── IFlowInfoService            # 流程信息
│   ├── IFlowVersionService         # 版本管理 + 触发
│   └── IFlowRuntimeService         # 运行时（同步/异步执行）
├── Suite/
│   ├── IApiService                 # API CRUD + 调试 + Swagger 解析
│   └── ISuiteService               # 套件 CRUD + 市场安装
├── IObjectService                  # 对象管理
├── ITokenService                   # Token 管理
├── IDataSourceService              # 数据源管理
└── IUserService                    # 用户认证
```

### 3.3 领域层（Domain）

包含核心业务规则和领域模型。

```
Domain/
├── Flow/
│   ├── FlowDefinition              # 流程定义聚合根
│   ├── FlowVersion                 # 流程版本实体
│   └── FlowInfo                    # 流程信息实体
├── Suite/
│   ├── Api                         # API 接口实体
│   └── Suite                       # 套件实体
├── Object/                         # 对象实体
├── Parameter/                      # 参数实体（输入/输出/Header）
├── Variable/                       # 变量信息实体
├── System/
│   ├── Token                       # 访问令牌实体
│   └── DataSource                  # 数据源实体
└── Engine/                         # 流程引擎核心
    ├── FlowEngine                  # 流程执行引擎
    ├── NodeExecutors/              # 各类节点执行器
    │   ├── StartNodeExecutor
    │   ├── EndNodeExecutor
    │   ├── MethodNodeExecutor      # API 调用节点
    │   └── ConditionNodeExecutor   # 条件判断节点
    └── ExpressionEvaluator         # 条件表达式求值器
```

### 3.4 基础设施层（Infrastructure）

```
Infrastructure/
├── Persistence/
│   ├── JuggleDbContext             # EF Core DbContext
│   ├── Entities/                   # 数据库实体映射
│   └── Repositories/              # 仓储实现
├── Http/
│   └── HttpApiCaller              # HTTP API 调用工具
└── Common/
    ├── JsonHelper                  # JSON 处理
    └── MD5Helper                  # MD5 加密
```

---

## 四、前端架构设计

### 4.1 技术架构

```
console-ui/
├── src/
│   ├── views/              # 页面视图（按功能模块划分）
│   ├── components/         # 可复用组件
│   ├── service/            # API 调用服务层
│   ├── router/             # Vue Router 路由配置
│   ├── hooks/              # Composition API Hooks
│   ├── typings/            # TypeScript 类型定义
│   ├── utils/              # 工具函数
│   └── const/              # 常量定义
```

### 4.2 模块划分

| 模块 | 路由 | 说明 |
|------|------|------|
| 流程定义 | /flow/define | 流程的创建、设计、调试、部署 |
| 流程列表 | /flow/list | 已部署流程的管理 |
| 流程版本 | /flow/version/:id | 版本启用/禁用/删除 |
| 流程设计器 | /design/:id/:key | 独立的拖拽式流程设计页面 |
| 套件管理 | /suite/list | 套件 CRUD |
| 接口管理 | /suite/api/:code/:id | API 接口 CRUD、调试 |
| 对象管理 | /object/list | 自定义对象定义 |
| 套件市场 | /market/suite | 套件市场浏览和安装 |
| 模板市场 | /market/template | 流程模板市场 |
| Token 管理 | /system/token | 访问令牌管理 |
| 数据源管理 | /system/datasource | MySQL 数据源配置 |
| 市场 | /market | 五类条目市场（接口/流程/模型助手/Skill/报表）+ 发现/收藏/下载/分享 |
| 模型助手 | /ai/assistant | 自定义模型助手管理 + 流程/接口/报表智能编排 |
| 大模型设置 | /system/ai-provider | 模型供应商（多供应商/能力绑定/模型拉取测试） |
| 知识库 | /knowledge | 知识库管理（文档解析切片/检索/清洗/匹配记录） |
| Skill 管理 | /system/skill | 技能 CRUD / 导入导出 |
| Redis 配置 | /system/redis | Redis 多实例配置与测试 |
| 报表管理 | /report | 数据视图 + 报表设计器 + 报表查询 |

> 注：报表/知识库/市场/模型助手模块为 v1.8 新增，详见图 4.2 对应的菜单路由配置（`router/index.ts`）。

### 4.3 核心组件

| 组件 | 说明 |
|------|------|
| FlowDesign | 流程设计器（拖拽、节点编辑、变量管理） |
| FlowDebug | 流程调试器（参数输入、执行、响应展示） |
| ApiDebug | API 调试器 |
| CodeEditor | Monaco 代码编辑器封装 |
| DataTypeSelect | 数据类型选择器 |
| VariableSelect | 变量选择器 |
| ParamSetting | 参数配置组件 |
| InputRuleSetting | 输入规则配置（变量 → 接口入参） |
| OutputRuleSetting | 输出规则配置（接口出参 → 变量） |
| FilterGroup/Item | 条件表达式配置组件 |

---

## 五、数据库设计

使用 **SQLite** 替换原来的 MySQL，所有字段类型做相应转换。

### 5.1 核心数据表

| 表名 | 说明 |
|------|------|
| t_user | 用户表 |
| t_suite | 套件表 |
| t_api | API 接口表 |
| t_parameter | 参数表（API 入参/出参/Header、对象属性、流程入参/出参） |
| t_object | 自定义对象表 |
| t_flow_definition | 流程定义表 |
| t_variable_info | 变量信息表 |
| t_flow_info | 流程信息表（部署后） |
| t_flow_version | 流程版本表 |
| t_token | 访问令牌表 |
| t_data_source | 数据源表 |
| t_ai_provider | AI 模型供应商表（baseUrl/apiKey/models/capabilities 能力绑定） |
| t_ai_assistant | AI 自定义助手表（系统提示词/入出参/快速提示词/图标） |
| t_ai_conversation | AI 对话会话表（多轮消息/输出/历史） |
| t_skill | 技能表（分组/内容/启停，AI 节点拼入提示词） |
| t_kb / t_kb_document / t_kb_chunk / t_kb_match_log | 知识库 4 表（切片/向量/匹配记录） |
| t_redis_config | Redis 多实例配置表 |
| t_market_item | 市场条目表（平台级共享，含官方 ID/图标/作者/版本） |
| t_market_favorite | 市场收藏表（租户级） |
| t_data_view / t_report | 数据视图 / 报表表 |
| t_alert_rule / t_alert_record | 告警规则 / 告警记录表 |

### 5.2 ER 关系

```
t_suite ──< t_api ──< t_parameter (param_type=1入参,2出参,4Header)
                           │
t_object ──< t_parameter (param_type=3对象属性)

t_flow_definition ──< t_parameter (param_type=1流程入参,2流程出参)
t_flow_definition ──< t_variable_info
t_flow_definition ──(部署)──> t_flow_info ──< t_flow_version
```

---

## 六、流程引擎架构

流程引擎是系统的核心，负责解析和执行流程定义。

### 6.1 流程数据结构

流程内容存储为 JSON 格式的节点列表：

```json
[
  {
    "key": "start_xxx",
    "elementType": "START",
    "outgoings": ["method_yyy"]
  },
  {
    "key": "method_yyy",
    "elementType": "METHOD",
    "incomings": ["start_xxx"],
    "outgoings": ["condition_zzz"],
    "method": {
      "suiteCode": "example_suite",
      "methodCode": "api_code",
      "url": "http://...",
      "requestType": "POST",
      "inputFillRules": [...],
      "outputFillRules": [...]
    }
  },
  {
    "key": "condition_zzz",
    "elementType": "CONDITION",
    "conditions": [
      {
        "conditionName": "分支1",
        "conditionType": "CUSTOM",
        "expression": "env_var==true",
        "outgoing": "method_aaa"
      },
      {
        "conditionName": "默认else分支",
        "conditionType": "DEFAULT",
        "outgoing": "end_bbb"
      }
    ]
  },
  {
    "key": "end_bbb",
    "elementType": "END"
  }
]
```

### 6.2 节点类型

| 节点类型 | 说明 |
|---------|------|
| START | 开始节点，流程入口 |
| END | 结束节点，流程出口 |
| METHOD | 方法节点，调用 HTTP API / WebService 接口 |
| CONDITION | 条件节点，多分支判断 |
| ASSIGN | 赋值节点，常量/变量/入参/静态→赋值给变量/出参/静态/子对象 |
| CODE | 代码节点，执行 JavaScript 脚本 |
| MYSQL/DB | 数据库节点，执行 SQL 查询/更新 |
| MERGE | 聚合节点，多分支汇聚 |
| SUB_FLOW | 子流程节点，递归调用其他已发布流程 |
| LOOP | 循环节点，遍历数组执行子流程 |
| DELAY | 延迟节点，等待指定时间 |
| PARALLEL | 并行节点，多分支并发执行 |
| NOTIFY | 通知节点，Webhook/邮件通知 |
| TRANSFORM | 模板转换节点，${var|pipe} 语法替换占位符 |
| AI | 大模型节点：供应商/模型/图片输入/模型参数（深度思考/温度/最大输出/随机种子）+ 函数调用工具（接口/流程，最多 4 轮） |
| FILE_PARSE | 文件解析节点：json/csv/xml/text/图片（视觉模型识别） |
| EXCEL_READ | Excel 读取节点 |
| FILE_WRITE | 文件写入节点 |
| REDIS_GET | Redis 查询节点（多实例/变量模板/JSON 解析） |
| REDIS_SET | Redis 设置节点（过期秒数） |
| KB_SEARCH | 知识库检索节点（RAG 上下文） |
| DATA_EXTRACT | 数据提取节点（json/code/keyword/between/length 五种提取类型） |

### 6.3 变量机制

变量是流程中数据流转的载体，分为三种类型：

| 类型 | 前缀约定 | 说明 |
|------|--------|------|
| 输入变量 | input_ | 流程调用时传入的参数 |
| 输出变量 | output_ | 流程返回的结果数据 |
| 环境变量 | env_ | 流程内部中间状态数据 |

### 6.4 数据填充规则（FillRule）

节点之间的数据流转通过填充规则定义：

**InputFillRule**（变量 → API 入参）：
```json
{
  "source": "input_userName",      // 来源变量
  "sourceType": "VARIABLE",
  "target": "userName",            // 目标 API 入参
  "targetType": "INPUT_PARAM"
}
```

**OutputFillRule**（API 出参 → 变量）：
```json
{
  "source": "loginFlag",           // API 出参
  "sourceType": "OUTPUT_PARAM",
  "target": "env_isLogin",         // 目标变量
  "targetType": "VARIABLE"
}
```

### 6.5 执行流程

```
触发流程
    ↓
加载流程版本（FlowVersion）
    ↓
初始化运行时上下文（变量初始化）
    ↓
从 START 节点开始遍历
    ↓
┌─── METHOD 节点 ───────────────────────────────┐
│ 1. 根据 inputFillRules 从变量中取值填充入参     │
│ 2. 发起 HTTP 请求调用 API                      │
│ 3. 根据 outputFillRules 将响应写入变量          │
└───────────────────────────────────────────────┘
    ↓
┌─── CONDITION 节点 ────────────────────────────┐
│ 1. 遍历条件列表，计算表达式                     │
│ 2. 匹配成功则走对应分支                         │
│ 3. 无匹配则走 DEFAULT 分支                      │
└───────────────────────────────────────────────┘
    ↓
到达 END 节点
    ↓
从变量中读取输出变量，组装响应结果
    ↓
返回 FlowResult
```

### 6.6 同步 vs 异步流程

| 类型 | 说明 | 触发方式 |
|------|------|---------|
| sync（同步） | 直接返回执行结果 | 调用方等待响应 |
| async（异步） | 立即返回实例ID，后台执行 | 调用方轮询结果 |

---

## 七、API 设计规范

### 7.1 URL 规范

| 前缀 | 说明 | 是否需要认证 |
|------|------|------------|
| `/api/` | 控制台管理接口 | 是（JWT Token） |
| `/open/` | 开放接口（供外部触发流程） | 是（API Token） |
| `/example/` | 示例 Mock 接口 | 否 |

### 7.2 标准接口列表

**流程定义：**
- `POST /api/flow/definition/add` - 创建流程定义
- `DELETE /api/flow/definition/delete/{id}` - 删除流程定义
- `PUT /api/flow/definition/update` - 修改流程定义
- `PUT /api/flow/definition/save` - 保存流程内容
- `GET /api/flow/definition/info/{id}` - 查询流程定义详情
- `POST /api/flow/definition/page` - 分页查询
- `POST /api/flow/definition/debug/{flowKey}` - 调试流程
- `POST /api/flow/definition/deploy` - 部署流程

**流程版本：**
- `PUT /api/flow/version/status` - 启用/禁用版本
- `DELETE /api/flow/version/delete/{id}` - 删除版本
- `POST /api/flow/version/page` - 分页查询
- `GET /api/flow/version/latest/{flowKey}` - 获取最新版本号
- `POST /api/flow/version/trigger/{version}/{key}` - 触发流程

**开放接口：**
- `GET /open/flow/trigger/{version}/{key}` - GET 方式触发
- `POST /open/flow/trigger/{version}/{key}` - POST 方式触发
- `GET /open/flow/getAsyncFlowResult/{instanceId}` - 获取异步结果

---

## 八、安全设计

### 8.1 认证机制

- **控制台接口**：JWT Token 认证，登录后返回 token，每次请求携带 `Authorization: Bearer <token>`
- **开放接口**：API Token 认证，在 `t_token` 表中维护有效 token，请求时通过 header 或 query 参数传递

### 8.2 权限控制

当前版本为单用户/单租户设计，不区分权限角色。

---

## 九、部署架构

### 9.1 单机部署（默认）

```
┌──────────────────────────────────────┐
│              服务器/PC               │
│  ┌────────────────────────────────┐  │
│  │    ASP.NET Core 6 Web API      │  │
│  │    端口: 9127                   │  │
│  └────────────────────────────────┘  │
│  ┌────────────────────────────────┐  │
│  │         SQLite 数据库           │  │
│  │    juggle.db (文件型数据库)      │  │
│  └────────────────────────────────┘  │
│  ┌────────────────────────────────┐  │
│  │     Vue3 前端静态文件           │  │
│  │    由 .NET 静态文件服务提供      │  │
│  └────────────────────────────────┘  │
└──────────────────────────────────────┘
```

### 9.2 目录结构

```
JuggleNet6/
├── JuggleNet6.Backend/     # ASP.NET Core 6 后端
│   ├── Controllers/
│   ├── Services/
│   ├── Domain/
│   ├── Infrastructure/
│   ├── Migrations/         # EF Core 数据库迁移
│   └── appsettings.json
├── JuggleNet6.Frontend/    # Vue 3 前端
│   ├── src/
│   ├── public/
│   └── vite.config.ts
└── README.md
```

---

## 十、技术选型说明

### 10.1 为什么选择 SQLite

| 优点 | 说明 |
|------|------|
| 零依赖 | 不需要单独安装数据库服务 |
| 简单部署 | 数据库就是一个文件，方便备份迁移 |
| 适合场景 | 单机部署、中小规模数据量完全够用 |
| EF Core 支持 | Microsoft.EntityFrameworkCore.Sqlite 官方支持 |

### 10.2 为什么选择 .NET 6

| 优点 | 说明 |
|------|------|
| 跨平台 | Windows / Linux / macOS 均可运行 |
| 高性能 | ASP.NET Core 性能优异 |
| 现代化 | 最小托管模型、依赖注入、Swagger 内置 |
| 长期支持 | .NET 6 为 LTS 版本 |

### 10.3 流程引擎 C# 实现策略

原版使用了独立的 Java 流程引擎库（juggle-core），重构版需自研 C# 流程引擎，核心包括：
1. **流程定义解析器** - 将 JSON 流程内容反序列化为节点列表
2. **流程执行器** - 按节点拓扑顺序执行
3. **节点执行器** - 各类型节点的具体执行逻辑
4. **变量上下文** - 流程运行时的数据容器
5. **表达式求值器** - 条件节点表达式计算（使用 `System.Linq.Dynamic.Core` 或自实现）

---

## 十一、版本规划

| 阶段 | 内容 | 优先级 |
|------|------|--------|
| v1.0 | 基础 CRUD（套件、API、对象、流程定义、流程版本）+ 用户登录 | P0 |
| v1.1 | 流程引擎核心（METHOD 节点 + CONDITION 节点） | P0 |
| v1.2 | 流程调试 + 部署 + 触发 + 开放接口 | P0 |
| v1.3 | Token 管理 + 数据源管理 | P1 |
| v1.4 | 套件/模板市场（只读展示） | P2 |
| v1.5-v1.7 | 子流程/监控/静态变量/WSDL/TRANSFORM/报表/AI 助手 | ✅ 已完成 |
| v1.8 | AI 节点增强与工具调用、知识库、Redis 多实例、Skill 管理、市场生态（发现/收藏/分享 GitHub PR）、模型能力绑定 | ✅ 已完成（2026-09） |
| v1.9 | 官方市场运营（GitHub 仓库 index.json 维护）、更多节点类型、AI 多模态扩展 | ⏳ 待规划 |

---

## 十二、AI 能力与开放生态架构（v1.8 新增）

### 12.1 AI 大模型能力架构

```
┌──────────────────────────────────────────────────────────────────┐
│                    前端 AI 模块（Vue3）                            │
│  大模型设置(供应商/模型/能力绑定)  模型助手管理  流程/接口/报表助手   │
└──────────────┬───────────────────────────────────────────────────┘
               │ /api/ai/*（JWT）
┌──────────────▼───────────────────────────────────────────────────┐
│                    AiService（Application 层）                    │
│  ChatRequestAsync(统一入口: 图片/模型参数/tools/多轮历史)           │
│  ResolveConfigAsync(供应商解析)  GenerateFlow/Apis/Report(编排生成) │
│  Conversation(多轮会话)   ExtractJson(代码块剥离)                  │
└──────────────┬───────────────────────────────────────────────────┘
               │ Func 回调注入（引擎与配置解耦）
┌──────────────▼───────────────────────────────────────────────────┐
│                   FlowEngine 回调注入体系                          │
│  aiChatFunc       → AiChatRequest → AiChatResult(Text+ToolCalls)  │
│  toolsResolver    → 接口/流程 → OpenAI tools 定义(含参数说明)       │
│  toolRunner       → api:xxx HTTP 调用 / flow:xxx 递归子引擎执行     │
│  skillResolver    → 技能 ID → 提示词拼接                            │
│  kbSearchFunc     → 知识库检索上下文                                │
│  redisConnStrs    → Redis 多实例连接串（0=默认实例）                │
└───────────────────────────────────────────────────────────────────┘
```

**关键设计**：
1. **能力绑定（Capabilities）**：供应商存 `capabilities` JSON（skills/apis/flows/tools），前端按所选供应商过滤 AI 节点技能下拉。
2. **函数调用（Tool Calling）**：AI 节点勾选接口/流程 → 执行时经 `toolsResolver` 生成 OpenAI 函数定义 → 模型返回 `tool_calls` → `toolRunner` 执行（接口走 HTTP，流程走递归 FlowEngine）→ 结果以 `role=tool` 消息回传，最多 4 轮；`AiChatRequest` 携带 `History`（多轮 tool_calls 消息）与 `ToolsJson`。
3. **模型参数**：深度思考（enable_thinking）、温度、max_tokens、seed 经统一请求入口透传 OpenAI 兼容接口。

### 12.2 知识库检索架构

- **存储**：`t_kb`（库配置：切片大小/重叠/检索类型/向量模型）→ `t_kb_document`（文档）→ `t_kb_chunk`（切片，`vector_json` 存向量）。
- **检索**：文本模式（SQLite LIKE 关键词打分）与向量模式（调用供应商 embeddings 接口 → 余弦相似度 TopK）；`t_kb_match_log` 记录每次检索。
- **流程接入**：`KB_SEARCH` 节点经 `kbSearchFunc` 回调获取 TopK 上下文文本，供 AI 节点 RAG 问答。

### 12.3 市场生态架构（发现 / 收藏 / 分享）

```
GitHub 官方仓库 pythonHuang/APIWorkflowCanvasDesignNet
  └── market/
        ├── index.json            # 条目索引（id/type/name/author/version/file）
        ├── flow/1.json ...       # 各类型 {id}.json 条目文件

① 发现：GET /api/market/discover → 拉取 index.json + 各条目文件 → 本地 market/ 目录
② 导入：POST /api/market/import-file → 按 (market_item_id, item_type) upsert t_market_item
③ 应用：POST /api/market/import/{id} → 按类型创建业务实体（按名称/code 去重）
④ 收藏：t_market_favorite（租户级）→ 列表过滤 favorite=1
⑤ 分享：POST /api/market/generate-share-file → 本地生成 {id}.json + 更新 index.json
        （同名条目复用原 id）→ 前端 GitHub API：fork → 分支 → 写条目文件
        → 合并官方最新 index.json → Pull Request（管理员审核合并）
```

**多租户**：市场条目 `TenantId=null`（平台级共享，宽松隔离）；收藏 `TenantId=当前租户`（严格隔离）；导入产生的业务实体归属导入方租户。

### 12.4 Redis 多实例架构

- `t_redis_config` 存多实例（0=默认实例），`RedisConnectionPool` 按连接串缓存 `ConnectionMultiplexer`。
- 引擎经 `redisConnStrs` 字典（实例 ID → 连接串）注入；节点 `redisId` 解析实例，未配置时抛明确错误。
