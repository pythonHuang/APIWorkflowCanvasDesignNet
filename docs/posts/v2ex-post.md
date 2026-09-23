# V2EX 分享帖

---

**标题：分享创造 | JuggleNet6：一个 .NET 8 + Vue 3 的 AI 接口编排+对话平台，开源可商用**

---

做一个自己用的低代码编排工具，边做边迭代到现在 221 个提交，开源了，贴个链接：

https://github.com/pythonHuang/JuggleNet6

**它是什么：** 图形化拖拽设计业务流程，连 API、查数据库、条件判断、循环并行都支持，14 种节点类型。亮点是内置了 AI——你描述需求，AI 自动生成流程 JSON 填入画布；还支持 AI 智能体多轮对话，带工具调用，能生图生视频。

**差异化：**
- 完全自托管，数据不经过第三方服务器
- 内置应用市场，流程可以发布/分享/PR 贡献
- 多租户隔离，企业可用
- Docker 一条命令启动，`.env` 配置切换数据库（SQLite/MySQL/PG/SQLServer）

**技术栈：** .NET 8 + Vue 3 + Element Plus + EF Core，DDD 四层架构，MIT 协议。

欢迎 Star，有问题/issues 直接提。
