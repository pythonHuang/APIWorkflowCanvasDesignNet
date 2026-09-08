<div align="center">

# 🎪 Juggle 接口流程编排平台（支持AI自动生成流程）

<p>
  <a href="https://github.com/pythonHuang/JuggleNet6/releases"><img src="https://img.shields.io/github/v/release/pythonHuang/JuggleNet6?style=flat-square" alt="Release"></a>
  <a href="https://github.com/pythonHuang/JuggleNet6/blob/main/LICENSE"><img src="https://img.shields.io/github/license/pythonHuang/JuggleNet6?style=flat-square" alt="License"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8"></a>
  <a href="https://vuejs.org/"><img src="https://img.shields.io/badge/Vue-3.0-4FC08D?style=flat-square&logo=vue.js" alt="Vue 3"></a>
</p>

**中文** | [English](#english)

</div>

---

## 📖 简介

Juggle 中文有"积木、魔法"之意，寓意像积木一样灵活，像魔法一样强大，满足灵活多变的业务需求，助力业务快速落地！

Juggle 是一个**图形化微服务编排工具**，通过简单的流程编排，快速完成接口开发，大大提高开发效率。

### 🎨 流程编排预览

![流程编排](./images/flowdesign.png)

### ✨ 核心能力


| 场景          | 说明                                            |
| ------------- | ----------------------------------------------- |
| 🧩 微服务编排 | 根据已有基础接口快速编排开发新接口              |
| 🤖 **AI 智能编排** | **对话式描述需求，大模型自动生成接口流程编排** |
| 🔗 系统集成   | 快速打通第三方系统平台，消除系统壁垒            |
| 📦 BFF 层     | 面向前端提供聚合/适配层（Backend for Frontend） |
| 🎨 定制开发   | 私有化标准功能定制，避免污染标准代码            |

---

## 🚀 快速开始

### Docker 一键启动

```bash
docker run -d \
  --name juggle \
  -p 9127:9127 \
  -v juggle_data:/data \
  pythonhuang/juggle-net8:v1.0
```

或使用 docker-compose：

```bash
docker-compose up -d
```

- 访问地址：http://localhost:9127
- 默认账号：`juggle` / `juggle`

---

## 🤖 AI 智能编排

通过自然语言对话，让大模型自动生成接口流程编排，无需手动拖拽节点。

### 使用方式

1. 进入**流程设计器**，点击工具栏「AI 生成」按钮
2. 展开「模型设置」，填写 OpenAI 兼容接口的地址、密钥与模型名（支持 DeepSeek / 通义千问 / Kimi / OpenAI 等，配置保存在系统配置中）
3. 在需求描述中说明编排需求，例如：
   > 接收用户id，先调用获取用户信息接口，再根据用户id查询该用户的订单列表，最后返回用户名称和订单列表
4. 点击「生成并替换画布」— AI 会从已加载的接口清单中选择接口、自动映射入参（流程入参）与出参（env_ 变量）、自动连线，并支持生成条件分支（CONDITION）、数据库节点（MYSQL）等 14 种节点
5. 生成后可在画布上继续手工调整，保存/部署后即可对外提供接口

### 支持的节点

START / END / METHOD（接口调用）/ CONDITION（条件分支）/ MERGE（汇聚）/ ASSIGN / CODE / MYSQL（SQL）/ LOOP / DELAY / PARALLEL / NOTIFY / TRANSFORM / SUB_FLOW

---

## 🛠️ 技术栈


| 层级 | 技术                                |
| ---- | ----------------------------------- |
| 后端 | ASP.NET Core 8 / EF Core 8 / SQLite |
| 前端 | Vue3 / Vite / Element Plus / Pinia  |
| 容器 | Docker (multi-stage build)          |
| 认证 | JWT + RBAC 角色权限                 |

---

## 📦 功能特性（30+ 项）

### 核心流程

- ✅ 可视化流程设计器（节点画布）
- ✅ **🤖 AI 智能编排** — 对话式描述需求，大模型自动生成接口流程编排（OpenAI 兼容接口，支持 DeepSeek/通义/Kimi 等）
- ✅ **14 种节点**：START / END / METHOD / CONDITION / MERGE / ASSIGN / CODE / DB / SUB_FLOW / LOOP / DELAY / PARALLEL / NOTIFY / TRANSFORM（模板转换）
- ✅ **对象子属性级联选择**（赋值/方法节点支持 object/array 类型属性树形选择）
- ✅ **数组操作**（赋值节点支持分页/取第n个/转JSON）
- ✅ **方法节点参数树**（API 入参/出参递归展开子属性）
- ✅ 节点超时 & 重试策略
- ✅ 流程版本管理 & 版本对比
- ✅ 流程克隆 / 导入 / 导出（含 Word 文档）
- ✅ 流程分组管理

### 触发方式

- ✅ 同步触发 `GET/POST /open/flow/trigger/{key}`
- ✅ 异步触发 + 结果查询
- ✅ Webhook 触发（含签名验证）
- ✅ 定时任务调度
- ✅ 服务别名（`/open/services/别名` 快捷访问）
- ✅ **SOAP 调用**（`POST /open/flow/soap/{key}`）
- ✅ **WSDL 自动生成**（`GET /open/flow/wsdl/{key}`，无需认证）

### 套件 & 接口

- ✅ 套件 / 接口 / 对象 / 参数管理
- ✅ 接口 Mock 功能
- ✅ WebService（SOAP 1.1 / 1.2）支持
- ✅ **接口导入 / 导出（JSON 格式）**
- ✅ **批量生成接口**（5 种方式：CURL / Swagger URL / Swagger JSON / WSDL URL / WSDL 内容）
- ✅ **cURL 一键复制**（含 Header + 入参完整配置）
- ✅ 对象子属性级联选择（object/array 类型参数关联全局对象）

### 监控 & 测试

- ✅ 监控仪表盘
- ✅ **API 拓扑图**（健康检查/访问统计/DB 调用连线）
- ✅ **告警规则 + 告警记录**
- ✅ 执行日志（含节点级日志）
- ✅ 流程测试用例（断言 + 批量执行）
- ✅ Monaco Editor 代码编辑（JS / SQL 高亮 + 自动补全）
- ✅ **报表模块**（数据视图 + 报表设计器 + 公式引擎）

### 系统管理

- ✅ 用户管理 / 角色管理 / 菜单权限（RBAC）
- ✅ 多租户数据隔离（JWT Claims 驱动）
- ✅ 审计日志 / Token 权限管理
- ✅ 系统配置中心 / 全局异常告警

### 数据库支持

- **系统数据库**：SQLite / MySQL / PostgreSQL / SQLServer
- **业务数据源**：SQLite / MySQL / PostgreSQL / SQLServer / Oracle / 达梦

---

## 📸 界面预览

### 流程设计器

![流程设计器](./images/flowdesign.png)

### 流程设计示例

![流程设计示例](./images/flowdesign01.png)

---

## 📋 更新日志

### v1.8（最新）

- 🤖 **AI 智能编排** — 接入大模型能力（OpenAI 兼容接口，支持 DeepSeek/通义千问/Kimi 等），设计器对话式描述需求自动生成接口流程编排（自动选接口/映射入参出参/连线/条件分支，支持 14 种节点）
- 📊 **报表模块** — 新增数据视图 + 报表设计器 + 公式引擎（SUM/AVG/COUNT/IF 等）；数据集管理支持 4 种数据源与字段树点击填入；A4 分页预览、撤销/重做、单元格直接编辑、行列插入删除
- 📈 **监控模块** — API 拓扑图（健康检查/访问统计/状态红黄绿/DB 节点与连线/流程过滤）+ 告警规则 + 告警记录
- 🧮 **表达式引擎** — 条件/赋值节点支持算术运算 + - * / %、字符串拼接/切片（[..5]/[2..5]）/replace、toString("#.0##") 数字与日期格式化；赋值节点新增 EXPRESSION 表达式来源
- 🔀 **条件节点扩展** — 支持 && || 括号、双变量比较、子属性、数组 length/元素取值，设计器内置语法帮助弹窗
- 🗄️ **数据库节点 SQL 辅助** — 表/视图/存储过程浏览一键生成 SQL（自动带 100 行限制）、单独测试 SQL（查询预览/更改事务回滚不污染数据）、帮助弹窗
- 🔗 **接口直连访问** — 套件接口支持 `/open/api/{code}` 直接调用（Token 授权）+ 访问别名 `/open/api/{alias}` + 停用/启用开关 + 一键复制地址
- 🧩 **WSDL 解析增强** — 支持 generatedXSD 地址、xsd:include/import schemaLocation 递归加载、粘贴 XSD 内容（解决内网 schemaLocation 无法访问）
- 📚 **安装部署文档** — 新增 Docker/Linux/Windows/Nginx/数据库/升级部署指南

### v1.7

- 🧩 **WSDL 自动生成** — 流程版本/别名均支持自动生成 WSDL 描述文件，无需认证即可访问；生成端点同步支持 SOAP 1.1 调用
- 📥 **接口批量生成** — 支持 5 种方式批量创建接口：CURL 命令、Swagger URL、Swagger JSON、WSDL URL、WSDL 内容；WSDL 解析器自动识别 SOAP 1.1/1.2/HTTP GET/HTTP POST 绑定
- 📦 **接口导入/导出** — 支持多选接口导出为 JSON 文件，可跨项目导入复用
- 📋 **cURL 一键复制** — 每个接口自动生成含 Header + 入参完整配置的 cURL 命令
- 🔢 **数组操作** — 赋值节点新增数组操作类型，支持分页/取第n个/转JSON
- 🌳 **对象子属性级联选择** — 赋值节点/方法节点均支持 object/array 类型参数的属性树形浏览和选择
- 🌲 **方法节点参数树** — API 入参/出参递归展开子属性，下拉框缩进展示层级

### v1.6

- 🔄 **TRANSFORM 模板转换节点** — 支持 `${var|pipe}` 模板语法，静态方法/实例方法双模式
- 🌐 **WebService SOAP 1.2** — 支持 SOAP 1.1/1.2 双版本，可配置操作名/命名空间/SOAPAction
- 📍 **参数位置配置** — 入参支持 query/body/rawBody 位置，Debug 调试适配不同位置策略
- 🔧 **流程变量内联编辑** — 变量管理表格支持直接编辑，无需弹窗
- 🎯 **来自对象导入** — 流程参数配置支持从全局对象类型一键导入属性作为入参/出参
- 🔗 **服务别名** — 流程可配置访问别名（`/open/services/alias`），支持 GET/POST

### v1.5

- 📊 **监控仪表盘** — 从流程管理独立为一级菜单
- 🔀 **赋值节点扩展** — 支持常量/变量/静态变量/入参多种来源
- 🧪 **版本触发测试** — 版本管理页增加触发测试弹窗

### v1.0

- 🎨 可视化流程设计器（VueFlow 画布 + 14 种节点）
- 🚀 同步/异步/Webhook/定时 多触发方式
- 🗄️ 多数据库支持（SQLite/MySQL/PostgreSQL/SQLServer/Oracle/达梦）
- 🔐 JWT + RBAC 权限 + 多租户数据隔离
- 🐳 Docker 一键部署（含 CI/CD GitHub Actions）

---

## 📚 文档

- [原项目文档](https://juggle.plus/docs/guide/introduce/introduce.html)（Java 版本参考）
- [Release Notes](./RELEASE.md)

---

## 🙏 致谢

本项目基于 [Juggle](https://github.com/somta/Juggle) 原始 Java 版本的设计思路与架构进行重构实现。

感谢 [@somta](https://github.com/somta) 及原项目团队！

---

<div id="english"></div>

<br><br>

---

<div align="center">

# 🎪 Juggle - API Orchestration Platform

<p>
  <a href="https://github.com/pythonHuang/JuggleNet6/releases"><img src="https://img.shields.io/github/v/release/pythonHuang/JuggleNet6?style=flat-square" alt="Release"></a>
  <a href="https://github.com/pythonHuang/JuggleNet6/blob/main/LICENSE"><img src="https://img.shields.io/github/license/pythonHuang/JuggleNet6?style=flat-square" alt="License"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8"></a>
  <a href="https://vuejs.org/"><img src="https://img.shields.io/badge/Vue-3.0-4FC08D?style=flat-square&logo=vue.js" alt="Vue 3"></a>
</p>

[中文](#-简介) | **English**

</div>

---

## 📖 Introduction

Juggle means "building blocks" and "magic" in Chinese, symbolizing flexibility like building blocks and power like magic — meeting flexible business needs and helping you deliver quickly!

Juggle is a **graphical microservice orchestration tool** that enables rapid API development through simple visual workflow design.

### ✨ Core Capabilities


| Scenario                      | Description                                                                 |
| ----------------------------- | --------------------------------------------------------------------------- |
| 🧩 Microservice Orchestration | Quickly build new APIs by orchestrating existing base APIs                  |
| 🤖 **AI Orchestration**       | **Describe requirements in natural language and let LLM auto-generate flow orchestration** |
| 🔗 System Integration         | Rapidly integrate with third-party platforms, breaking down system barriers |
| 📦 BFF Layer                  | Provide aggregation/adaptation layer for frontend (Backend for Frontend)    |
| 🎨 Custom Development         | Privatized standard function customization without polluting core code      |

---

## 🚀 Quick Start

### Docker One-Click Start

```bash
docker run -d \
  --name juggle \
  -p 9127:9127 \
  -v juggle_data:/data \
  pythonhuang/juggle-net8:v1.0
```

Or use docker-compose:

```bash
docker-compose up -d
```

- Access: http://localhost:9127
- Default credentials: `juggle` / `juggle`

---

## 🤖 AI Orchestration

Generate API flow orchestration from natural language requirements — no manual node dragging needed.

### How to Use

1. Open the **Flow Designer** and click the "AI Generate" button in the toolbar
2. Expand "Model Settings" and fill in an OpenAI-compatible API base URL, API key and model name (DeepSeek / Qwen / Kimi / OpenAI etc.; saved in system config)
3. Describe your requirement, e.g.:
   > Take a userId, call the get-user-info API, then query the user's order list, finally return the user name and the order list
4. Click "Generate & Replace Canvas" — the LLM picks APIs from the available list, auto-maps inputs (flow input params) and outputs (`env_` variables), wires nodes automatically, and supports all 14 node types including CONDITION branches and MYSQL (SQL) nodes
5. Fine-tune the result on the canvas, then save/deploy to expose it as an API

### Supported Node Types

START / END / METHOD / CONDITION / MERGE / ASSIGN / CODE / MYSQL / LOOP / DELAY / PARALLEL / NOTIFY / TRANSFORM / SUB_FLOW

---

## 🛠️ Tech Stack


| Layer     | Technology                           |
| --------- | ------------------------------------ |
| Backend   | ASP.NET Core 8 / EF Core 8 / SQLite  |
| Frontend  | Vue3 / Vite / Element Plus / Pinia   |
| Container | Docker (multi-stage build)           |
| Auth      | JWT + RBAC Role-Based Access Control |

---

## 📦 Features (30+)

### Core Workflow

- ✅ Visual workflow designer (node canvas)
- ✅ **🤖 AI orchestration** — Describe requirements in natural language and let the LLM auto-generate flow orchestration (OpenAI-compatible API; DeepSeek/Qwen/Kimi supported)
- ✅ **14 Node Types**: START / END / METHOD / CONDITION / MERGE / ASSIGN / CODE / DB / SUB_FLOW / LOOP / DELAY / PARALLEL / NOTIFY / TRANSFORM
- ✅ **Object sub-property cascading** (ASSIGN/METHOD nodes support object/array tree property selection)
- ✅ **Array operations** (ASSIGN node: paginate / get by index / to JSON)
- ✅ **Method node param tree** (API I/O params recursively expand sub-properties)
- ✅ Node timeout & retry policies
- ✅ Workflow version management & comparison
- ✅ Clone / Import / Export (with Word docs)
- ✅ Workflow grouping

### Trigger Methods

- ✅ Synchronous trigger `GET/POST /open/flow/trigger/{key}`
- ✅ Asynchronous trigger + result query
- ✅ Webhook trigger (with signature verification)
- ✅ Scheduled task scheduling
- ✅ Service alias (`/open/services/{alias}` quick access)
- ✅ **SOAP trigger** (`POST /open/flow/soap/{key}`)
- ✅ **Auto WSDL generation** (`GET /open/flow/wsdl/{key}`, no auth required)

### Suite & API Management

- ✅ Suite / API / Object / Parameter management
- ✅ API Mock functionality
- ✅ WebService (SOAP 1.1 / 1.2) support
- ✅ **API Import / Export (JSON)**
- ✅ **Batch API generation** (5 sources: CURL / Swagger URL / Swagger JSON / WSDL URL / WSDL content)
- ✅ **cURL one-click copy** (with full Headers + Input params)
- ✅ Object sub-property cascading selection (object/array type params linked to global objects)

### Monitoring & Testing

- ✅ Monitoring dashboard
- ✅ **API topology map** (health check / visit stats / DB call edges)
- ✅ **Alert rules + alert records**
- ✅ Execution logs (with node-level logs)
- ✅ Workflow test cases (assertions + batch execution)
- ✅ Monaco Editor code editing (JS / SQL highlighting + autocomplete)
- ✅ **Report module** (data views + report designer + formula engine)

### System Management

- ✅ User management / Role management / Menu permissions (RBAC)
- ✅ Multi-tenant data isolation (JWT Claims driven)
- ✅ Audit logs / Token permission management
- ✅ System config center / Global exception alerting

### Database Support

- **System Database**: SQLite / MySQL / PostgreSQL / SQLServer
- **Business Data Sources**: SQLite / MySQL / PostgreSQL / SQLServer / Oracle / Dameng

---

## 📋 Changelog

### v1.8（Latest）

- 🤖 **AI orchestration** — LLM integration (OpenAI-compatible API; DeepSeek/Qwen/Kimi supported); describe requirements in the designer to auto-generate flow orchestration (auto API selection, input/output mapping, wiring, condition branches; all 14 node types)
- 📊 **Report module** — Data views + Report designer + Formula engine (SUM/AVG/COUNT/IF etc.); datasets with 4 source types and field-tree click-to-fill; A4 paged preview, undo/redo, inline cell editing, row/column insert & delete
- 📈 **Monitoring module** — API topology map (health check / visit stats / red-yellow-green status / DB nodes & edges / flow filter) + alert rules + alert records
- 🧮 **Expression engine** — CONDITION/ASSIGN nodes support arithmetic + - * / %, string concat / slice ([..5]/[2..5]) / replace, toString("#.0##") number & date formatting; ASSIGN node adds EXPRESSION source type
- 🔀 **Condition node extension** — && || parentheses, variable-vs-variable comparison, sub-properties, array length/element access; built-in syntax help dialog in the designer
- 🗄️ **DB node SQL assist** — Browse tables/views/procedures and one-click generate SQL (auto 100-row limit); standalone SQL test (query preview / transactional rollback for updates); help dialog
- 🔗 **Direct API access** — Suite APIs callable via `/open/api/{code}` (Token auth) + access alias `/open/api/{alias}` + enable/disable switch + one-click copy URL
- 🧩 **WSDL parser enhancements** — generatedXSD location, recursive xsd:include/import schemaLocation loading, paste XSD content (for unreachable internal schemaLocation)
- 📚 **Deployment docs** — Docker / Linux / Windows / Nginx / database / upgrade guides

### v1.7

- 🧩 **WSDL auto-generation** — Flow versions & aliases auto-generate WSDL files, publicly accessible without auth; sync SOAP 1.1 call support
- 📥 **Batch API generation** — 5 sources: CURL / Swagger URL / Swagger JSON / WSDL URL / WSDL content; WSDL parser auto-detects SOAP 1.1/1.2/HTTP GET/HTTP POST bindings
- 📦 **API import/export** — Multi-select export to JSON, cross-project import
- 📋 **cURL one-click copy** — Full cURL command with Headers + Input params
- 🔢 **Array operations** — ASSIGN node: paginate / get by index / to JSON
- 🌳 **Object sub-property cascading** — ASSIGN/METHOD nodes support object/array tree property browser and selection
- 🌲 **Method node param tree** — API I/O params recursively expand sub-properties with indented dropdown

### v1.6

- 🔄 **TRANSFORM node** — `${var|pipe}` template syntax, static/instance method transforms
- 🌐 **WebService SOAP 1.2** — Dual SOAP version support, configurable operation/namespace/SOAPAction
- 📍 **Param position** — Input params: query/body/rawBody; debug adapts per position
- 🔧 **Inline variable editing** — Flow variable table supports inline edit
- 🎯 **Import from object** — One-click import properties from global objects to flow params
- 🔗 **Service alias** — Custom access alias for flows (`/open/services/alias`)

### v1.5

- 📊 **Dashboard** — Promoted to independent top-level menu
- 🔀 **ASSIGN node extensions** — CONSTANT/VARIABLE/STATIC/INPUT source types
- 🧪 **Version trigger test** — Inline trigger test dialog in version management

### v1.0

- 🎨 Visual flow designer (VueFlow canvas + 14 node types)
- 🚀 Sync/Async/Webhook/Scheduled triggers
- 🗄️ Multi-database (SQLite/MySQL/PostgreSQL/SQLServer/Oracle/Dameng)
- 🔐 JWT + RBAC + Multi-tenant isolation
- 🐳 Docker one-click deploy (with CI/CD GitHub Actions)

---

## 📚 Documentation

- [Original Project Docs](https://juggle.plus/docs/guide/introduce/introduce.html) (Java version reference)
- [Release Notes](./RELEASE.md)

---

## 🙏 Acknowledgments

This project is a .NET 8 reimplementation based on the design and architecture of the original [Juggle](https://github.com/somta/Juggle) Java version.

Thanks to [@somta](https://github.com/somta) and the original team!

---

## 📄 License

[MIT License](./LICENSE)
