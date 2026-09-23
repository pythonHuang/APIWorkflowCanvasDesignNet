# 掘金文章：用 .NET 8 打造一款低代码 AI 编排平台，开箱即用

---

## 一、为什么会有这个项目？

在微服务和 API 集成日益复杂的今天，开发者经常需要在不同系统之间"搭桥"：把 A 接口的响应字段映射到 B 接口的入参，再根据条件调 C，最后把结果写入数据库。这类需求如果用纯代码实现，往往要写大量样板逻辑——HTTP 请求、JSON 解析、错误处理、重试机制……重复劳动多，维护成本也高。

市面上有 Zapier、n8n、Dify 等编排工具，但它们要么是 SaaS 服务数据要过别人服务器，要么部署复杂对国内开发者不友好，要么不支持 .NET 生态。

于是我开始做 **JuggleNet6**——一款基于 **.NET 8 + Vue 3** 的开源低代码接口编排平台，支持图形化拖拽设计流程、AI 智能生成编排逻辑、多租户隔离部署，完全自托管，数据不出自己的服务器。

> GitHub 地址：https://github.com/pythonHuang/JuggleNet6  
> MIT 协议，可商用。

---

## 二、核心功能亮点

### 1. 图形化流程编排

基于 `@vue-flow/core` 构建的画布编辑器，支持 **14 种节点类型**自由拖拽连线：

- START / END（流程入口出口）
- METHOD（HTTP 接口调用，支持 GET/POST/PUT/DELETE/SOAP）
- CONDITION（条件分支，支持 && || 括号、算术运算、字符串拼接）
- ASSIGN（变量赋值，常量/变量/表达式/静态变量）
- CODE（JavaScript 脚本，Monaco Editor 高亮 + 自动补全）
- MYSQL / DB（SQL 查询，表视图浏览 + 一键生成 SQL）
- LOOP / PARALLEL（循环 / 并行执行）
- SUB_FLOW（递归调用子流程）
- DELAY / NOTIFY（延迟等待 / 消息通知）
- TRANSFORM（模板转换 `${var|pipe}` 语法）
- KB_SEARCH（知识库检索节点，RAG 问答）

每个 METHOD 节点支持 `InputFillRules` / `OutputFillRules`，自动把变量映射到 API 入参和响应字段，无需手写解析代码。

### 2. AI 智能编排

内置 AI 助手（支持 DeepSeek、通义千问、Kimi、OpenAI 等任意 OpenAI 兼容接口），你只需输入自然语言描述需求，AI 自动生成完整的流程 JSON，直接填入画布编辑。

同时提供三个专用 AI 助手：
- **流程编排助手**：自动选接口、映射入参出参、连线、条件分支
- **接口接入助手**：生成套件 + 接口方案（含入参/Header/出参配置）
- **报表助手**：生成 A4 报表（数据集绑定、公式聚合、预览 HTML）

设计器工具栏也有「AI 生成」按钮，一键对话生成并替换画布。

### 3. AI 智能体对话

多轮对话 + 完整历史管理，支持自定义系统提示词、输入输出参数、辅助提问按钮。

**函数调用（Tool Calling）**：将已配置的接口或流程挂为工具，AI 按需调用（接口直接 HTTP 调用 / 流程递归执行），结果回传后生成最终答案，最多 4 轮。

**模型参数可调**：深度思考开关（DeepSeek-R1 / Qwen-Think 生效）、温度 0-2、最大输出字数、随机种子，助手和会话级别参数独立管理。

### 4. 生图 / 生视频 🎨🎬

这是近期新增的特色功能。

**生图模型**：选择 wanx / dall-e / flux / stable / midjourney 等模型时，发送消息自动走图片生成。通义万相走 DashScope 异步任务轮询（text2image），OpenAI 兼容走 `images/generations`（支持 url 与 b64_json）。

**生视频模型**：选择 t2v / kling / hailuo / sora 等模型时，走 DashScope text2video 异步轮询（约 3 分钟超时）。

生成结果直接渲染在对话气泡中，支持下载和原图查看。

### 5. 知识库 RAG

上传 Word / Excel / PDF / TXT / Markdown 文档，自动切片 + 向量嵌入。AI 节点支持关键词检索 + 向量余弦相似度双路召回，实现有据可查的智能问答。

### 6. 内置应用市场

- **发现**：一键从 GitHub 官方仓库拉取市场索引，导入/更新本地条目
- **收藏**：租户级收藏，支持「只看收藏」过滤
- **发布/分享**：发布后平台级共享；支持 GitHub Token 授权自动生成 Pull Request（或跳转手动填写预填内容）
- **直接应用**：一键导入为流程/接口/助手/Skill/报表

### 7. 监控 + 告警

API 拓扑图实时展示健康状态、访问统计、DB 调用连线；支持告警规则配置和记录查询。

---

## 三、技术架构

后端采用 **.NET 8 最小 API + ASP.NET Core**，遵循 **DDD 四层架构**：

```
Juggle.Api              →  Controllers、JWT 认证、托管 Vue 静态资源
Juggle.Application      →  FlowExecutionService、AiService、JwtService
Juggle.Domain           →  FlowEngine（流程执行引擎）+ 14 种节点执行器
Juggle.Infrastructure   →  EF Core DbContext（Code First）、JsonHelper
```

数据访问支持 **SQLite（默认，零配置）/ MySQL / PostgreSQL / SQLServer** 四种数据库，通过环境变量 `DB_TYPE` 切换。

前端 **Vue 3 + TypeScript + Vite + Element Plus + Pinia + @vue-flow/core + Monaco Editor**。

---

## 四、Docker 一键部署

不需要装 .NET SDK，不需要配 Node.js，一条命令跑起来：

```bash
docker run -d --name juggle -p 9127:9127 \
  -v juggle_data:/data \
  pythonhuang/juggle-net8:v1.0
```

默认账号 `juggle` / `juggle`，浏览器打开 `http://localhost:9127` 即可进入管理后台。数据库文件持久化在 Docker Volume 里，容器重启数据不丢失。

自定义数据库或 JWT 密钥：

```bash
docker run -d --name juggle -p 9127:9127 \
  -v juggle_data:/data \
  -e DB_TYPE=mysql \
  -e DB_CONNECTION_STRING="Server=...;Database=..." \
  -e Jwt__Key="your-super-secret-key-min-32-chars" \
  pythonhuang/juggle-net8:v1.0
```

---

## 五、多租户隔离

平台内置多租户支持，每个租户数据完全隔离（JWT Claims 注入租户 ID，EF Core 全局查询过滤器自动加条件）。同时支持"全局"数据（TenantId 为 null），如公共 API 定义、公共数据源，所有租户均可引用。

---

## 六、与竞品的差异

| 维度 | JuggleNet6 | n8n | Dify | Coze |
|------|-----------|-----|------|------|
| 自托管 | ✅ | ✅ | ✅ | ❌ |
| 可视化工作流引擎 | ✅ 14 节点 | ✅ | ❌ | ❌ |
| AI 函数调用 | ✅ 最多 4 轮 | ⚠️ 有限 | ✅ | ✅ |
| 生图/生视频 | ✅ 内置 | ❌ | ❌ | ✅ |
| 内置应用市场 | ✅ PR 分享 | ❌ | ❌ | ❌ |
| 多租户 | ✅ JWT 严格隔离 | ⚠️ 需配置 | ❌ | ❌ |
| 许可证 | MIT（可商用） | Apache 2.0 | AGPL-3.0 | 闭源 |

JuggleNet6 填补了「传统低代码工作流」和「现代 AI 智能体」之间的空白——既有完善的流程编排能力，又有对话生成流程、工具调用、知识库等 AI 原生能力。

---

## 七、结语

JuggleNet6 从一个个人需求出发，逐步成长为一个功能完整的低代码编排平台。代码量适中（221 次提交持续迭代），架构清晰，非常适合想学习 .NET 8 + Vue 3 全栈开发的开发者参考，也适合企业内网自托管使用。

欢迎 **Star** 和 **Fork**：https://github.com/pythonHuang/JuggleNet6

如有问题或建议，欢迎提 Issue 或 Pull Request！
