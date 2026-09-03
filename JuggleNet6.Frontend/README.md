# JuggleNet6.Frontend — 前端项目

> Vue 3 + TypeScript + Element Plus + @vue-flow/core

---

## 项目概述

`JuggleNet6.Frontend` 是 Juggle 接口编排平台的前端项目，基于 **Vue 3 Composition API + TypeScript** 构建，提供可视化的流程设计、接口管理、系统配置等功能页面。

---

## 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| Vue 3 | ^3.5 | 前端框架（Composition API） |
| TypeScript | ~5.9 | 类型安全 |
| Vite | ^8.0 | 构建工具 |
| Element Plus | ^2.13 | UI 组件库 |
| @vue-flow/core | ^1.48 | 流程画布（拖拽设计器） |
| @vue-flow/minimap | ^1.5 | 流程缩略图 |
| @vue-flow/controls | ^1.1 | 画布控制按钮 |
| Pinia | ^3.0 | 状态管理 |
| Vue Router | ^4.6 | 路由管理 |
| Axios | ^1.13 | HTTP 请求 |

---

## 目录结构

```
JuggleNet6.Frontend/
├── src/
│   ├── views/                  # 页面视图
│   │   ├── flow/               # 流程相关页面
│   │   │   ├── FlowDefinitionList.vue   # 流程定义列表
│   │   │   ├── FlowDesign.vue           # 流程设计器（画布主页面）
│   │   │   ├── FlowInfoList.vue         # 已部署流程列表
│   │   │   ├── FlowLog.vue              # 流程执行日志
│   │   │   └── FlowVersionList.vue      # 流程版本管理
│   │   ├── suite/              # 套件相关页面
│   │   │   ├── ApiDetail.vue            # API 接口详情（入参/出参/Header）
│   │   │   └── SuiteList.vue            # 套件列表
│   │   ├── object/             # 对象管理
│   │   │   └── ObjectList.vue           # 自定义对象列表
│   │   ├── system/             # 系统设置
│   │   │   ├── DataSourceList.vue       # 数据源管理
│   │   │   ├── StaticVariable.vue       # 全局静态变量
│   │   │   └── TokenList.vue            # 访问令牌管理
│   │   ├── Layout.vue          # 主布局（侧边导航 + 内容区）
│   │   └── Login.vue           # 登录页
│   ├── components/             # 可复用组件
│   ├── router/                 # 路由配置
│   ├── utils/                  # 工具函数（axios 封装等）
│   ├── assets/                 # 静态资源
│   ├── App.vue
│   ├── main.ts
│   └── style.css
├── dist/                       # 构建输出目录
├── public/
├── index.html
├── package.json
├── vite.config.ts
├── tsconfig.json
└── tsconfig.app.json
```

---

## 快速开始

### 安装依赖

```bash
npm install
```

### 开发模式启动

```bash
npm run dev
```

访问：http://localhost:5173/

> 开发模式下，`vite.config.ts` 已配置代理，将 `/api` 和 `/open` 请求转发至后端 `http://localhost:9127`。

### 构建生产版本

```bash
npm run build
```

构建产物输出到 `dist/` 目录。

### 集成到后端（一键部署）

```powershell
npm run build
Copy-Item dist\* ..\Juggle.Api\wwwroot\ -Recurse -Force
```

集成后，通过 http://localhost:9127/ 直接访问完整应用。

---

## 页面路由

| 路由 | 组件 | 说明 |
|------|------|------|
| `/login` | Login.vue | 登录页 |
| `/` | Layout.vue | 主布局（需登录） |
| `/flow/list` | FlowDefinitionList.vue | 流程定义列表 |
| `/flow/design/:id/:flowKey` | FlowDesign.vue | 流程设计器 |
| `/flow/info` | FlowInfoList.vue | 已部署流程列表 |
| `/flow/version/:id` | FlowVersionList.vue | 版本管理 |
| `/flow/log` | FlowLog.vue | 执行日志 |
| `/suite/list` | SuiteList.vue | 套件列表 |
| `/suite/api/:suiteCode/:apiId/detail` | ApiDetail.vue | API 接口详情 |
| `/object/list` | ObjectList.vue | 对象管理 |
| `/system/datasource` | DataSourceList.vue | 数据源管理 |
| `/system/token` | TokenList.vue | 令牌管理 |
| `/system/variable` | StaticVariable.vue | 静态变量管理 |

---

## 流程设计器（FlowDesign.vue）

核心功能页面，基于 `@vue-flow/core` 实现可视化流程画布：

### 画布操作

| 操作 | 说明 |
|------|------|
| 拖拽节点 | 从左侧节点面板拖入画布 |
| 移动节点 | 在画布内自由拖动节点 |
| 建立连线 | 拖拽节点连接点到目标节点 |
| 删除节点 | 选中节点后按 `Delete` 键 |
| 删除连线 | 选中连线后按 `Delete` 键 |
| 撤销 | `Ctrl+Z` |
| 重做 | `Ctrl+Y` |
| 自动布局 | 点击工具栏「自动布局」按钮 |
| 缩略图 | 右下角 MiniMap 导航 |

### 节点类型

| 节点 | 颜色标识 | 说明 |
|------|---------|------|
| START | 绿色 | 开始节点 |
| END | 红色 | 结束节点 |
| METHOD | 蓝色 | HTTP API 调用 |
| CONDITION | 橙色 | 条件分支 |
| ASSIGN | 紫色 | 变量赋值 |
| CODE | 深色 | JavaScript 脚本 |
| MYSQL | 青色 | SQL 查询 |
| MERGE | 灰色 | 分支聚合 |

### 调试模式

点击「调试」按钮进入调试模式：

- 填写入参 JSON，点击「运行」
- 已执行节点在画布上高亮显示（✓ 成功 / ✗ 失败）
- 点击「📊 查看输出」查看节点详细输入/输出变量

---

## 执行日志（FlowLog.vue）

- 按 `flowKey` / 执行状态 / 日期范围筛选
- 点击日志行展开右侧抽屉，查看所有节点执行时间轴
- 时间轴显示：节点序号 / 类型 / 耗时 / 输入输出变量快照

---

## 静态变量管理（StaticVariable.vue）

- 全局静态变量，跨流程共享
- 支持行内直接修改当前值
- 支持重置为默认值
- 支持按分组（groupName）组织变量

---

## 数据源管理（DataSourceList.vue）

支持四种数据库类型：

| 类型 | 说明 |
|------|------|
| SQLite | 本地文件型数据库，无需配置网络 |
| MySQL | 标准 MySQL / MariaDB |
| PostgreSQL | PostgreSQL 数据库 |
| SQL Server | Microsoft SQL Server |

- 切换类型时自动切换表单（SQLite 隐藏网络字段）
- 「测试连接」按钮即时验证配置是否正确

---

## HTTP 请求封装

`src/utils/request.ts` 基于 Axios 封装，自动：

- 携带 JWT Token（从 localStorage 读取）
- 401 响应自动跳转登录页
- 统一错误提示

---

## 开发注意事项

1. **API 代理**：开发模式下 `vite.config.ts` 中配置了 `/api` 代理到 `http://localhost:9127`，需保证后端已启动
2. **类型检查**：构建时执行 `vue-tsc` 类型检查，确保类型正确才能构建成功
3. **节点 key**：流程节点使用 `key` 字段（非 `nodeId`），节点类型用大写 `elementType`

---

## 更新日志

### v1.8（最新）

- 📊 **报表模块** — 数据视图管理 + 报表设计器（数据集字段树点击填入/数据预览/单元格直接编辑/撤销重做/行列插入删除/A4 分页预览/公式引擎）
- 📈 **监控模块** — API 拓扑图（健康检查/访问统计/状态红黄绿/DB 节点与连线/流程过滤/缓存）+ 告警规则 + 告警记录
- 🔀 **条件节点增强** — 语法帮助弹窗（&& || 括号、算术 + - * / %、字符串拼接/切片/replace、toString 格式化、数组取值完整 demo）
- 🗄️ **数据库节点增强** — 表/视图/存储过程浏览弹窗（搜索/字段预览/一键生成 SQL）+ 单独测试 SQL 弹窗（参数自动提取/结果表格/错误提示）+ SQL 编写帮助弹窗
- 🧮 **赋值节点增强** — 新增 EXPRESSION 表达式来源（算术/字符串操作/toString 格式化）+ 表达式语法帮助弹窗
