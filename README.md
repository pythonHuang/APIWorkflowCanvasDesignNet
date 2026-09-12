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

通过自然语言对话，让大模型自动完成流程编排、接口接入与报表设计，无需手动操作。

### 一、大模型设置（模型供应商设置）

系统设置 → 大模型设置，接入任意 **OpenAI 兼容接口**（DeepSeek / 通义千问 / Kimi / OpenAI / 智谱GLM / Ollama 等）：

- **供应商下拉可选可直接输入**：预置 DeepSeek、通义千问、Kimi、OpenAI、智谱GLM、Ollama，选择后自动填默认接口地址，也支持输入自定义供应商
- **获取模型 / 配置测试**：填写 API Key 后一键验证密钥有效性（兼作配置测试），成功即拉取该供应商的可用模型列表
- **默认模型**：从拉取的模型列表下拉选择（也可手动输入）；**可用模型**：多选下拉（对话中可切换），保存为模型清单
- **多供应商并存 + 启停开关**：任一助手对话时可在启用的供应商与模型中自由切换

### 二、模型助手管理（自定义模型助手）

自定义智能助手，可配置：

| 配置项 | 说明 |
|--------|------|
| 名称 / 描述 | 菜单显示名与用途说明 |
| 系统提示词 | 助手人设与任务要求（如"你是一名文案专家…"） |
| 输入参数列表 | 运行页表单，类型支持文本/数字/日期/开关/下拉（下拉可配选项+默认值） |
| 输出参数列表 | 结束对话时要求模型按 JSON 返回并按参数分栏展示 |
| 辅助提问词按钮 | 运行页快捷按钮，点击即填入常用提问（换行追加可组合） |
| 启停 | 启用后自动出现在「模型助手」菜单 |

运行页支持**多轮对话**：聊天气泡、每轮携带完整历史、回车发送；「结束对话」时模型基于完整对话生成最终 JSON 结果；「新对话」随时重开；「历史」抽屉查看全部会话（继续进行中对话 / 查看已结束结果 / 删除）。

### 三、流程智能编排助手

对话描述需求，AI 自动完成接口流程编排：

1. 填写需求与流程名称，选择供应商/模型，点击「生成编排」
2. AI 从平台已接入的接口清单中选择接口：自动映射入参（流程入参 input_xxx）、出参（env_ 变量）、自动连线，支持条件分支（CONDITION）、数据库节点（MYSQL）等 14 种节点
3. **预览确认**：展示节点清单（类型/名称/连线/详情）+ **流程入参/出参定义表**（code/名称/类型/必填/说明，与节点映射严格对应）
4. 确认生成：创建流程定义并写入编排内容，入参/出参随流程入库，可跳设计器继续调整后保存部署

示例需求：
> 接收用户id，先调用获取用户信息接口，再根据用户id查询该用户的订单列表，最后返回用户名称和订单列表

### 四、接口智能接入助手

对话描述接入需求，AI 生成套件与接口方案：

1. 填写接入需求（如"接入用户中心相关接口：查询用户信息、修改昵称、查询收货地址"），选择供应商/模型，点击「生成接口方案」
2. AI 规划套件（code/名称/描述）与接口（code/名称/描述/路径/GET|POST），并自动设计每个接口的**入参配置**（参数/类型/位置 body|query/必填/说明）、**Header 配置**（如 Authorization 鉴权头）、**出参配置**（响应字段）
3. **预览确认**：套件表 + 接口表（展开行显示入参/Header/出参三段明细）
4. 确认接入：套件与接口写入平台，参数随接口入库；按 code 去重，已存在的自动跳过

### 五、报表智能助手

对话描述报表需求，AI 自动完成报表设计：

1. 填写需求与报表名称（如"生成月度销售统计报表，按日期范围筛选，按销售员汇总订单金额和数量，需要合计行"），点击「生成报表」
2. **数据集**：优先复用已有数据视图（sourceType=dataview），不满足时用自定义 SQL（自动匹配可用数据源与方言），或复用已发布流程/接口数据
3. **查询参数**：自动设计筛选参数（text/number/date/select），与数据集 SQL 的 @参数名 对应
4. **排版**：标题行（合并居中加粗）→ 表头行 → 数据行（绑定数据集自动扩展+斑马纹）→ 汇总行；合计用 `${字段:SUM}` 聚合，条件计算用 `=IF(条件,真,假)` 公式
5. **生成即预览**：展示数据集/参数清单 + 报表 HTML 实际渲染效果；**确认后才创建报表**，可继续去设计器调整或打开正式访问页

### 流程设计器内 AI 生成

流程设计器工具栏同样提供「AI 生成」按钮，对话生成后直接替换画布，可继续手工调整。

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
- ✅ **14 种节点**：START / END / METHOD / CONDITION / MERGE / ASSIGN / CODE / DB / SUB_FLOW / LOOP / DELAY / PARALLEL / NOTIFY / TRANSFORM（模板转换）
- ✅ **对象子属性级联选择**（赋值/方法节点支持 object/array 类型属性树形选择）
- ✅ **数组操作**（赋值节点支持分页/取第n个/转JSON）
- ✅ **方法节点参数树**（API 入参/出参递归展开子属性）
- ✅ 节点超时 & 重试策略
- ✅ 流程版本管理 & 版本对比
- ✅ 流程克隆 / 导入 / 导出（含 Word 文档）
- ✅ 流程分组管理

### 模型助手（AI）

- ✅ **大模型设置** — 多供应商管理（预置下拉/模型拉取/配置测试/启停），支持任意 OpenAI 兼容接口（DeepSeek/通义千问/Kimi/OpenAI/智谱/Ollama）
- ✅ **流程智能编排助手** — 对话生成流程（自动选接口/入参出参映射/条件分支/14 种节点），预览确认入库
- ✅ **接口智能接入助手** — 对话生成套件与接口（含入参/Header/出参配置），预览确认接入
- ✅ **报表智能助手** — 对话生成报表（复用数据视图/数据源/流程/接口 + 查询参数 + 排版公式），预览确认创建
- ✅ **自定义智能助手** — 名称/系统提示词/输入输出参数/辅助提问词，启用后加入菜单；多轮对话 + 结束输出最终结果 + 历史记录
- ✅ **流程设计器 AI 生成** — 工具栏一键生成并替换画布

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

- ⚙️ **大模型设置** — 接入大模型能力（OpenAI 兼容接口，支持 DeepSeek/通义千问/Kimi/OpenAI/智谱GLM/Ollama）：供应商预置下拉可直接输入并自动填默认接口地址、获取模型/配置测试一键验证密钥并拉取可用模型、默认模型下拉+可用模型多选、多供应商并存启停
- 🤖 **模型助手管理（自定义智能助手）** — 名称/描述/系统提示词/输入参数列表（文本/数字/日期/开关/下拉）/输出参数列表/辅助提问词按钮，启用后自动加入模型助手菜单；运行页支持多轮对话（气泡消息/完整历史）、结束对话按输出参数生成最终 JSON 结果、开启新对话、对话历史抽屉（查看/继续/删除）
- 🧩 **流程智能编排助手** — 对话生成接口流程编排：自动选择已有接口、映射入参出参、连线与条件分支、14 种节点，生成**流程入参/出参定义**并在预览中展示，确认后创建流程（参数随流程入库）
- 🔌 **接口智能接入助手** — 对话生成套件与接口方案：自动设计**入参/Header/出参配置**（位置/必填/说明），预览展开三段展示，确认后写入平台（参数入库、按 code 去重）
- 📊 **报表智能助手** — 对话生成报表：数据集优先复用数据视图/数据源/流程/接口，自动设计查询参数与排版（汇总聚合/条件公式），生成即预览 HTML，确认后创建报表
- 🎨 **设计器 AI 生成** — 流程设计器工具栏一键对话生成并替换画布
- 📊 **报表模块** — 新增数据视图 + 报表设计器 + 公式引擎（SUM/AVG/COUNT/IF 等）；数据集管理支持 4 种数据源与字段树点击填入；A4 分页预览、撤销/重做、单元格直接编辑、行列插入删除
- 📈 **监控模块** — API 拓扑图（健康检查/访问统计/状态红黄绿/DB 节点与连线/流程过滤）+ 告警规则 + 告警记录
- 🧮 **表达式引擎** — 条件/赋值节点支持算术运算 + - * / %、字符串拼接/切片（[..5]/[2..5]）/replace、toString("#.0##") 数字与日期格式化；赋值节点新增 EXPRESSION 表达式来源
- 🔀 **条件节点扩展** — 支持 && || 括号、双变量比较、子属性、数组 length/元素取值，设计器内置语法帮助弹窗
- 🗄️ **数据库节点 SQL 辅助** — 表/视图/存储过程浏览一键生成 SQL（自动带 100 行限制）、单独测试 SQL（查询预览/更改事务回滚不污染数据）、帮助弹窗
- 🔗 **接口直连访问** — 套件接口支持 `/open/api/{code}` 直接调用（Token 授权）+ 访问别名 `/open/api/{alias}` + 停用/启用开关 + 一键复制地址
- 🧩 **WSDL 解析增强** — 支持 generatedXSD 地址、xsd:include/import schemaLocation 递归加载、粘贴 XSD 内容（解决内网 schemaLocation 无法访问）
- 📚 **安装部署文档** — 新增 Docker/Linux/Windows/Nginx/数据库/升级部署指南
- 🧠 **大模型节点增强** — 可设置供应商与模型、输入变量可选流程入出参及中间变量、图片输入（多模态视觉识别，多选 data URL 变量）、输出目标可选变量/出参/入参；文件解析节点支持图片类型（视觉模型识别描述）
- 🎛️ **大模型节点模型参数** — 深度思考开关（enable_thinking，deepseek-reasoner/Qwen 思考模式生效）、温度 0-2、最大输出字数（max_tokens）、随机种子（seed）
- 🔧 **大模型节点工具调用** — 勾选接口/流程作为函数调用 tools，模型按需调用工具（接口直接 HTTP 调用/流程递归执行），结果回传后生成最终回答（最多 4 轮）
- 📚 **知识库子系统** — 多知识库 CRUD 与配置（切片大小/重叠、文本或向量检索、向量模型），文档上传自动解析（word/excel/pdf/txt/markdown）切片入库、手动片段、文本关键词检索与向量余弦相似度检索（embeddings）、清洗（去空去重超短）与重新向量化、匹配记录查询；流程设计器新增知识库检索节点（KB_SEARCH）供 AI 节点 RAG 问答
- ✂️ **数据提取节点** — 输入可选流程入出参与中间变量，提取类型 json（首个对象数组）/code（代码块）/keyword（关键字后到行尾）/between（起止标志）/length（偏移长度），输出可赋值变量/出参/入参
- 🔎 **Redis 缓存节点** — 查询/设置节点（key 支持变量模板/JSON 自动解析/过期秒数）+ 系统设置 Redis 多实例配置（地址/端口/密码/库/测试连接/默认实例），未配置时节点执行给出明确报错
- 🛠️ **Skill 管理** — 技能 CRUD（名称/分组/描述/内容/启停）、单个导入（JSON 或 markdown，首个 # 标题为技能名）、批量导入、单个/批量导出；大模型节点可勾选多个技能，执行时拼入系统提示词
- 🛒 **市场系统** — 接口/流程/模型助手/Skills/报表五个市场：发布后平台级共享，任意租户一键导入（按名称/code 去重 + 下载计数）；条目展示图标/名称/描述/作者/版本/更新日期；收藏/取消收藏 + 只看收藏过滤；条目 JSON 下载与直接应用
- 🔍 **市场发现** — 从 GitHub 官方仓库（pythonHuang/APIWorkflowCanvasDesignNet）market 目录拉取 index.json 及各类型 {id}.json 到本地，按 id 导入（id 相同更新，否则新增）
- 🚀 **市场分享** — 分享到官方 GitHub 市场：填写作者/版本，本地生成 market/{type}/{id}.json 并更新 index.json，GitHub Token 授权后自动 fork → 建分支 → 写条目文件 → 合并官方最新索引 → 生成 Pull Request，管理员审核合并进入官方市场
- 🔗 **模型能力绑定** — 供应商可设置支持的能力（Skills 多选/接口多选/流程多选/工具列表），流程 AI 节点按所选供应商过滤技能列表

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

- [系统需求文档](./REQUIREMENTS.md)
- [系统架构文档](./Architecture.md)
- [系统详细设计文档](./DesignDoc.md)
- [系统部署文档](./DEPLOY.md)
- [数据库表结构](./docs/表结构/表列表.md)
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

Use natural language to let the LLM auto-complete flow orchestration, API integration and report design.

### 1. Model Settings (Providers)

System Settings → AI Models: connect any **OpenAI-compatible API** (DeepSeek / Qwen / Kimi / OpenAI / GLM / Ollama etc.):

- **Provider dropdown or free input**: preset DeepSeek, Qwen, Kimi, OpenAI, GLM and Ollama with auto-filled default base URL; custom providers supported
- **Fetch models / Config test**: one click verifies the API key and pulls the provider's available model list
- **Default model** dropdown + **available models** multi-select (switchable during chats)
- **Multiple providers with enable/disable** — switch provider and model freely in any assistant chat

### 2. Assistant Management (Custom Assistants)

Create custom assistants with:

| Config | Description |
|--------|-------------|
| Name / Description | Menu label and purpose |
| System prompt | Persona and task instructions (e.g. "You are a copywriting expert…") |
| Input params | Run-page form: text / number / date / switch / select (options + defaults) |
| Output params | End-conversation JSON fields, shown in the final result panel |
| Quick-prompt buttons | One-click buttons that fill common questions (append with newlines to combine) |
| Enable | Auto-appears in the "AI Assistants" menu |

The run page supports **multi-turn conversations**: chat bubbles with full history per round; "End Conversation" produces final JSON results from output params; "New Conversation" restarts anytime; the "History" drawer lists all conversations (resume ongoing / review ended / delete).

### 3. Flow Orchestration Assistant

Describe requirements and let the LLM build the flow:

1. Enter the requirement and flow name, pick provider/model, click "Generate"
2. The LLM picks from existing platform APIs: auto-maps inputs (flow input params) and outputs (`env_` variables), wires nodes, supports condition branches (CONDITION), DB nodes (MYSQL) — all 14 node types
3. **Preview & confirm**: node list (type/name/wiring/details) + flow input/output param tables (strictly matching node mappings)
4. Confirm to create the flow definition; params are saved with the flow; continue tuning in the designer

Example:
> Take a userId, call the get-user-info API, then query the user's order list, finally return the user name and the order list

### 4. API Integration Assistant

Describe integration needs and the LLM plans suites & APIs:

1. Enter the requirement (e.g. "Integrate user-center APIs: query user info, update nickname, list shipping addresses"), pick provider/model, click "Generate API Plan"
2. The LLM plans suites (code/name/desc) and APIs (code/name/desc/path/GET|POST) with **input params** (code/type/position body|query/required/desc), **Header configs** (e.g. Authorization) and **output params** (response fields)
3. **Preview & confirm**: suite table + API table with expandable rows showing input/Header/output details
4. Confirm to import; params saved with the APIs; deduplicated by code (existing ones skipped)

### 5. Report Assistant

Describe report needs and the LLM designs the report:

1. Enter the requirement and report name (e.g. "Monthly sales statistics by seller with date-range filter, amount/qty totals and a grand-total row"), click "Generate Report"
2. **Datasets**: reuse existing data views first (sourceType=dataview); fall back to custom SQL with a matching data source (dialect-aware), or reuse published flows/APIs
3. **Query params**: auto-designed filters (text/number/date/select) bound to `@paramName` in dataset SQL
4. **Layout**: title row (merged, centered, bold) → header row → data rows (dataset-bound auto-expand + zebra) → footer row; totals via `${field:SUM}` aggregates, conditional logic via `=IF(condition, a, b)` formulas
5. **Preview on generation**: dataset/param lists + actual rendered HTML; **confirm to create the report**, then continue in the designer or open the public view page

### In-Designer AI Generation

The Flow Designer toolbar also has an "AI Generate" button — chat to generate and replace the canvas, then fine-tune manually.

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
- ✅ **14 Node Types**: START / END / METHOD / CONDITION / MERGE / ASSIGN / CODE / DB / SUB_FLOW / LOOP / DELAY / PARALLEL / NOTIFY / TRANSFORM
- ✅ **Object sub-property cascading** (ASSIGN/METHOD nodes support object/array tree property selection)
- ✅ **Array operations** (ASSIGN node: paginate / get by index / to JSON)
- ✅ **Method node param tree** (API I/O params recursively expand sub-properties)
- ✅ Node timeout & retry policies
- ✅ Workflow version management & comparison
- ✅ Clone / Import / Export (with Word docs)
- ✅ Workflow grouping

### AI Assistants

- ✅ **Model settings** — Multi-provider management (preset dropdown / model fetch / config test / enable-disable), any OpenAI-compatible API (DeepSeek/Qwen/Kimi/OpenAI/GLM/Ollama)
- ✅ **Flow orchestration assistant** — Chat to generate flows (auto API selection, I/O param mapping, condition branches, 14 node types), preview then confirm
- ✅ **API integration assistant** — Chat to generate suites & APIs (input / Header / output param configs), preview then confirm
- ✅ **Report assistant** — Chat to generate reports (reuses data views / data sources / flows / APIs + query params + layout formulas), preview then confirm
- ✅ **Custom assistants** — Name / system prompt / I/O params / quick prompts, auto-added to menu; multi-turn chat + final structured outputs + history
- ✅ **In-designer AI generation** — One-click generate and replace the canvas

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

- ⚙️ **Model settings** — LLM integration (OpenAI-compatible API; DeepSeek/Qwen/Kimi/OpenAI/GLM/Ollama): preset provider dropdown with auto-filled base URL (free input supported), fetch-models/config-test to verify keys and pull models, default model dropdown + available models multi-select, multi-provider enable/disable
- 🤖 **Assistant management (custom assistants)** — name/description/system prompt/input params (text/number/date/switch/select)/output params/quick-prompt buttons; enabled assistants auto-join the AI Assistants menu; run page supports multi-turn chats (bubbles + full history), end-conversation with final JSON outputs, new conversation, and a history drawer (view/resume/delete)
- 🧩 **Flow orchestration assistant** — chat to generate flows: auto API selection, I/O mapping, wiring, condition branches, 14 node types, plus flow input/output param definitions shown in preview; confirm to create the flow with params saved
- 🔌 **API integration assistant** — chat to generate suites & APIs with input/Header/output param configs (position/required/desc), expandable three-section preview; confirm to import (params saved, deduplicated by code)
- 📊 **Report assistant** — chat to generate reports: datasets reuse data views/data sources/flows/APIs first, auto query params and layout (aggregates & formulas), instant HTML preview; confirm to create the report
- 🎨 **In-designer AI generation** — one-click chat to generate and replace the canvas
- 📊 **Report module** — Data views + Report designer + Formula engine (SUM/AVG/COUNT/IF etc.); datasets with 4 source types and field-tree click-to-fill; A4 paged preview, undo/redo, inline cell editing, row/column insert & delete
- 📈 **Monitoring module** — API topology map (health check / visit stats / red-yellow-green status / DB nodes & edges / flow filter) + alert rules + alert records
- 🧮 **Expression engine** — CONDITION/ASSIGN nodes support arithmetic + - * / %, string concat / slice ([..5]/[2..5]) / replace, toString("#.0##") number & date formatting; ASSIGN node adds EXPRESSION source type
- 🔀 **Condition node extension** — && || parentheses, variable-vs-variable comparison, sub-properties, array length/element access; built-in syntax help dialog in the designer
- 🗄️ **DB node SQL assist** — Browse tables/views/procedures and one-click generate SQL (auto 100-row limit); standalone SQL test (query preview / transactional rollback for updates); help dialog
- 🔗 **Direct API access** — Suite APIs callable via `/open/api/{code}` (Token auth) + access alias `/open/api/{alias}` + enable/disable switch + one-click copy URL
- 🧩 **WSDL parser enhancements** — generatedXSD location, recursive xsd:include/import schemaLocation loading, paste XSD content (for unreachable internal schemaLocation)
- 📚 **Deployment docs** — Docker / Linux / Windows / Nginx / database / upgrade guides
- 🧠 **LLM node enhancement** — Provider & model selection, input variable from flow in/out/intermediate vars, image input (multimodal vision, multi-select data URLs), output target variable/out-param/in-param; file-parse node supports image type (vision description)
- 🎛️ **LLM node model params** — Deep-thinking switch (enable_thinking for reasoning models), temperature 0-2, max output tokens, random seed
- 🔧 **LLM node tool calling** — Select APIs/flows as function-calling tools; the model calls tools on demand (APIs invoked directly over HTTP, flows run recursively), results are fed back to produce the final answer (max 4 rounds)
- 📚 **Knowledge base subsystem** — Multi-KB CRUD & config (chunk size/overlap, text or vector retrieval, embedding model), auto document parsing (word/excel/pdf/txt/markdown) & chunking, manual chunks, keyword search & vector cosine-similarity search, cleaning & re-embedding, match-log query; new KB_SEARCH flow node for RAG Q&A with AI nodes
- ✂️ **Data-extract node** — json (first object/array) / code (code block) / keyword (text after keyword to line end) / between (start-end markers) / length (offset-length) extraction types; output to variable/out-param/in-param
- 🔎 **Redis cache nodes** — Get/Set nodes (variable templates in key / auto JSON parse / TTL seconds) + multi-instance Redis config page (host/port/password/db/test-connection/default instance); clear error when not configured
- 🛠️ **Skill management** — Skill CRUD (name/group/desc/content/enable), single import (JSON or markdown), batch import, single/batch export; AI nodes can attach multiple skills appended to the system prompt at runtime
- 🛒 **Market system** — Five markets (APIs/flows/assistants/skills/reports): publish for platform-wide sharing, one-click import with dedup & download count; items show icon/name/desc/author/version/date; favorite/unfavorite with filter; JSON download & direct apply
- 🔍 **Market discovery** — Pull index.json and {id}.json files from the official GitHub repo (pythonHuang/APIWorkflowCanvasDesignNet) market directory to local; import by id (update if same id, else insert)
- 🚀 **Market sharing** — Share to the official GitHub market: fill author/version, generate local market/{type}/{id}.json and update index.json, GitHub token auth, then auto fork → branch → write item file → merge latest official index → create Pull Request; admins review and merge into the official market
- 🔗 **Model capability binding** — Providers can set supported capabilities (skills/APIs/flows/tools); the flow AI node filters its skill list by the selected provider

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

- [Requirements](./REQUIREMENTS.md) / [Architecture](./Architecture.md) / [Detailed Design](./DesignDoc.md) / [Deployment](./DEPLOY.md) / [DB Tables](./docs/表结构/表列表.md)（中文）
- [Original Project Docs](https://juggle.plus/docs/guide/introduce/introduce.html) (Java version reference)
- [Release Notes](./RELEASE.md)

---

## 🙏 Acknowledgments

This project is a .NET 8 reimplementation based on the design and architecture of the original [Juggle](https://github.com/somta/Juggle) Java version.

Thanks to [@somta](https://github.com/somta) and the original team!

---

## 📄 License

[MIT License](./LICENSE)
