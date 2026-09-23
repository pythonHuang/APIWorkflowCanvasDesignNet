# CSDN 短文章：2 分钟上手，一个开箱即用的 AI 接口编排平台

---

JuggleNet6 是一款开源的低代码接口编排平台，基于 .NET 8 + Vue 3 开发，支持图形化拖拽设计业务流程、AI 自动生成流程、AI 智能体对话、生图生视频等功能，可 Docker 一键部署，数据完全自托管。

**GitHub 地址：** https://github.com/pythonHuang/JuggleNet6

---

## 3 步 Docker 启动

```bash
# 1. 拉取镜像并启动
docker run -d --name juggle -p 9127:9127 -v juggle_data:/data pythonhuang/juggle-net8:v1.0

# 2. 浏览器打开 http://localhost:9127

# 3. 登录：账号 juggle，密码 juggle
```

---

## 核心功能一览

**[截图：流程编排画布 - 拖拽节点连线设计流程]**

1. **图形化流程设计**：14 种节点类型，拖拽连线，所见即所得
2. **HTTP 接口编排**：支持任意外部 API 调用，自动变量映射，Mock 测试
3. **AI 智能生成**：自然语言描述需求，AI 自动生成完整流程（接口+条件+循环+SQL）
4. **AI 智能体对话**：多轮对话 + 工具调用（接口/流程作为函数），支持生图/生视频
5. **多数据库支持**：SQLite / MySQL / PostgreSQL / SQLServer 一键切换

---

## 更多亮点

- 🤖 **函数调用**：接口/流程作为工具，AI 按需调用（最多 4 轮）
- 📚 **知识库 RAG**：上传文档自动切片，向量检索增强问答
- 🛒 **内置市场**：发现/导入/收藏/发布/分享，GitHub PR 自动生成
- 🎨🎬 **生图/生视频**：选对模型自动触发，气泡内直接渲染下载
- 📊 **报表设计器**：A4 分页、公式引擎、数据视图，对话生成
- 📈 **监控告警**：API 拓扑图 + 告警规则 + 告警记录
- 🔐 **多租户**：JWT Claims 驱动严格数据隔离，企业可用
- 📄 **MIT 协议**：可商用，无限制

---

**快速体验：** https://github.com/pythonHuang/JuggleNet6
