# Hacker News / Reddit 英文帖

---

**Title:** Show HN: JuggleNet6 – .NET 8 + Vue 3 low-code workflow orchestrator with AI agent support (image/video generation, function calling)

---

I built JuggleNet6 out of a personal need: gluing APIs together without writing repetitive integration code every time. It's a self-hosted workflow orchestration platform with a visual node-based editor, AI-powered flow generation from natural language, and an AI agent capable of tool calling, image generation, and video generation.

**What it does:**
- Drag-and-drop flow design with 14 node types (HTTP calls, conditionals, loops, parallel execution, SQL queries, code scripts, etc.)
- AI assistant generates complete workflow JSON from a text description
- AI agent supports multi-turn conversation with function calling, integrated with DeepSeek, Qwen, Kimi, OpenAI and any OpenAI-compatible endpoint
- Image generation (via Tongyi Wanxiang/DashScope async polling or OpenAI images API) and video generation
- Built-in marketplace for sharing, importing and publishing flow templates
- Multi-tenant data isolation with JWT auth
- Report designer and API topology monitoring with alerting

**Tech stack:** .NET 8 backend (DDD, EF Core Code First), Vue 3 + TypeScript + Element Plus + @vue-flow/core frontend. Supports SQLite (default), MySQL, PostgreSQL, SQLServer.

**One-command Docker deploy:**
```bash
docker run -d --name juggle -p 9127:9127 -v juggle_data:/data pythonhuang/juggle-net8:v1.0
```
Default login: juggle / juggle

MIT licensed, fully self-hostable. Feedback and PRs welcome!

https://github.com/pythonHuang/JuggleNet6
