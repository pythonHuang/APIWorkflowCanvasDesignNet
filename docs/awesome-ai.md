# Add JuggleNet6 - AI agent workflow platform with image/video generation

## JuggleNet6

[![Release](https://img.shields.io/github/v/release/pythonHuang/JuggleNet6?style=flat-square)](https://github.com/pythonHuang/JuggleNet6/releases)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Vue 3](https://img.shields.io/badge/Vue-3.0-4FC08D?style=flat-square&logo=vue.js)](https://vuejs.org/)
[![License](https://img.shields.io/github/license/pythonHuang/JuggleNet6?style=flat-square)](https://github.com/pythonHuang/JuggleNet6/blob/main/LICENSE)

> **An AI-powered workflow orchestration platform** — combine natural-language chat with executable flow diagrams. Describe a requirement in chat and watch the LLM generate a complete API orchestration with tool calling, image/video generation, and multi-turn reasoning. Self-hostable, multi-tenant, Docker-ready.

**GitHub:** https://github.com/pythonHuang/JuggleNet6

**Tags:** `ai-agent` | `llm-app` | `workflow` | `multimodal` | `function-calling` | `rags` | `net8` | `vue3`

---

### Highlights

- **Conversational Flow Orchestration**: describe requirements in natural language; the LLM generates complete workflow diagrams with METHOD nodes, condition branches, loop structures, and variable mappings — no manual drag-and-drop needed
- **Function Calling / Tool Use**: attach existing APIs and flows as function-calling tools; the LLM calls them on demand (up to 4 rounds), feeding results back for the final answer
- **Knowledge Base (RAG)**: upload documents (Word / Excel / PDF / TXT / Markdown), auto-chunked and embedded; AI nodes perform keyword + vector cosine-similarity retrieval for grounded Q&A
- **Image Generation**: text-to-image via DashScope (通义万相) async tasks or OpenAI-compatible `images/generations` endpoint; results rendered inline with download/open buttons
- **Video Generation**: text-to-video via DashScope async tasks with polling (up to 3 min timeout); rendered in chat bubbles
- **Custom AI Assistants**: configure system prompts, input/output params, quick-prompt buttons; multi-turn chat with full history and structured JSON outputs
- **AI-Powered Report Generation**: chat to describe report needs; the LLM designs datasets, query parameters, and A4 layouts with formulas (`SUM`, `IF`, etc.) and live HTML preview
- **Multi-modal Input**: AI nodes accept image inputs (data URLs) for vision model description and file-parse nodes
- **Self-hosted & Multi-tenant**: JWT-based auth, tenant-scoped data isolation, Docker one-click deploy

### Supported LLM Providers

OpenAI-compatible interface — works with DeepSeek, Qwen (通义千问), Kimi, OpenAI, GLM, Ollama, and any custom endpoint.

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

| Layer      | Technology                              |
| ---------- | --------------------------------------- |
| Backend    | ASP.NET Core 8 / EF Core 8 / SQLite     |
| Frontend   | Vue 3 / Vite / Element Plus / Pinia     |
| AI Runtime | OpenAI-compatible SDK / DashScope       |
| Embeddings | Vector cosine-similarity search         |
| Container  | Docker (multi-stage build)              |
| Auth       | JWT + RBAC Role-Based Access Control    |

---

### Why I recommend it

JuggleNet6 is one of the most complete open-source AI agent platforms I've seen: it goes beyond simple chatbots by letting you turn conversational requirements into executable workflows with real tool calling, RAG knowledge bases, and multimodal generation (image/video) — all self-hostable with a single Docker command.
