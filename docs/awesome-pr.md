# Add JuggleNet6 - .NET 8 workflow orchestrator with AI agent support

## JuggleNet6

[![Release](https://img.shields.io/github/v/release/pythonHuang/JuggleNet6?style=flat-square)](https://github.com/pythonHuang/JuggleNet6/releases)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Vue 3](https://img.shields.io/badge/Vue-3.0-4FC08D?style=flat-square&logo=vue.js)](https://vuejs.org/)
[![License](https://img.shields.io/github/license/pythonHuang/JuggleNet6?style=flat-square)](https://github.com/pythonHuang/JuggleNet6/blob/main/LICENSE)

> **.NET 8 + Vue 3 low-code workflow orchestration + AI agent conversation platform** — describe requirements in natural language and let the LLM auto-generate flow orchestration, API integration, and report designs. Supports image/video generation, multi-turn tool calling, built-in marketplace, and multi-tenant isolation.

**GitHub:** https://github.com/pythonHuang/JuggleNet6

**Tags:** `workflow` | `ai-agent` | `low-code` | `net8` | `vue3` | `docker`

---

### Highlights

- **14 node types**: START / END / METHOD / CONDITION / MERGE / ASSIGN / CODE / DB / LOOP / DELAY / PARALLEL / NOTIFY / TRANSFORM / SUB_FLOW
- **AI Flow Orchestration Assistant**: chat to describe requirements, auto-generates flow diagrams with API selection, input/output param mapping, and conditional branches
- **AI API Integration Assistant**: chat to generate suite + API plans with input/Header/output parameter configs
- **AI Report Assistant**: chat to generate A4 reports with dataset binding, formulas (SUM/AVG/IF), and live preview
- **Custom AI Assistants**: multi-turn conversations with system prompts, input/output params, and history
- **Image & Video Generation**: support for text-to-image and text-to-video via DashScope and OpenAI-compatible APIs
- **Built-in Marketplace**: share and import APIs, flows, assistants, skills, and reports across tenants
- **Multi-tenant** data isolation driven by JWT Claims
- **Docker one-click deploy** with GitHub Actions CI/CD
- **Supported databases**: SQLite / MySQL / PostgreSQL / SQLServer / Oracle / Dameng

### Quick Start

```bash
docker run -d \
  --name juggle \
  -p 9127:9127 \
  -v juggle_data:/data \
  pythonhuang/juggle-net8:v1.0
```

Access at http://localhost:9127 — default account: `juggle` / `juggle`

### Tech Stack

| Layer      | Technology                         |
| ---------- | ---------------------------------- |
| Backend    | ASP.NET Core 8 / EF Core 8 / SQLite |
| Frontend   | Vue 3 / Vite / Element Plus / Pinia |
| Container  | Docker (multi-stage build)         |
| Auth       | JWT + RBAC Role-Based Access Control |

---

### Why I recommend it

JuggleNet6 is a genuinely impressive full-stack project that bridges the gap between traditional low-code workflow tools and modern AI agent platforms — the "chat-to-generate-flow" workflow is particularly elegant, and the built-in marketplace and multi-tenant support make it production-ready out of the box.
