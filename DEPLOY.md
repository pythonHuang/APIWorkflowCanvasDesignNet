# Juggle 接口编排平台 — 安装部署文档

## 环境要求

| 组件 | 版本 |
|------|------|
| .NET SDK | 8.0+ |
| Node.js | 18+ / 20+ |
| 数据库（可选） | SQLite（默认）/ MySQL 8.0+ / PostgreSQL 15+ / SQLServer 2019+ |

---

## 一、Docker 部署（推荐）

### 1.1 一键启动

```bash
docker run -d \
  --name juggle \
  -p 9127:9127 \
  -v juggle_data:/data \
  pythonhuang/juggle-net8:v1.0
```

### 1.2 docker-compose

```yaml
version: '3.8'
services:
  juggle:
    image: pythonhuang/juggle-net8:v1.0
    container_name: juggle
    ports:
      - "9127:9127"
    volumes:
      - juggle_data:/data
    environment:
      - DB_TYPE=sqlite
      - DB_PATH=/data/juggle.db
    restart: unless-stopped

volumes:
  juggle_data:
```

```bash
docker-compose up -d
```

### 1.3 访问

- 地址：`http://localhost:9127`
- 默认账号：`juggle` / `juggle`

---

## 二、Windows 部署

### 2.1 一键启动脚本

```batch
启动Juggle.bat
```

交互式菜单，支持：
1. 启动后端 (Juggle.Api)
2. 启动前端 (Vue Dev Server)
3. 同时启动前后端
4. 构建前端并启动后端（生产模式）

### 2.2 手动部署

#### 后端

```bash
cd Juggle.Api
dotnet run
# 默认监听 http://localhost:9127
```

#### 前端

```bash
cd JuggleNet6.Frontend
npm install
npm run dev
# 开发模式 http://localhost:5173
```

#### 生产构建

```bash
# 构建前端 → 输出到 Juggle.Api/wwwroot/
cd JuggleNet6.Frontend
npm run build

# 后端自动 serve 前端静态文件
cd ../Juggle.Api
dotnet run
# 访问 http://localhost:9127 即可
```

---

## 三、Linux 部署

### 3.1 .NET 后端

```bash
# 安装 .NET 8 SDK
# Ubuntu:
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install -y dotnet-sdk-8.0

# 构建运行
cd Juggle.Api
dotnet publish -c Release -o /opt/juggle
cd /opt/juggle
ASPNETCORE_URLS="http://+:9127" dotnet Juggle.Api.dll
```

### 3.2 systemd 服务

```ini
# /etc/systemd/system/juggle.service
[Unit]
Description=Juggle API Orchestration Platform
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/juggle
ExecStart=/usr/bin/dotnet /opt/juggle/Juggle.Api.dll
Environment=ASPNETCORE_URLS=http://+:9127
Environment=DB_TYPE=sqlite
Environment=DB_PATH=/data/juggle/juggle.db
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable juggle
sudo systemctl start juggle
```

---

## 四、数据库配置

### 4.1 SQLite（默认，无需额外配置）

```bash
DB_TYPE=sqlite
DB_PATH=/data/juggle.db
```

### 4.2 MySQL

```bash
DB_TYPE=mysql
DB_HOST=localhost
DB_PORT=3306
DB_NAME=juggle
DB_USER=root
DB_PASS=your_password
```

或使用完整连接字符串：

```bash
DB_CONNECTION_STRING="Server=localhost;Port=3306;Database=juggle;User=root;Password=your_password;"
```

### 4.3 PostgreSQL

```bash
DB_TYPE=postgresql
DB_HOST=localhost
DB_PORT=5432
DB_NAME=juggle
DB_USER=postgres
DB_PASS=your_password
```

### 4.4 SQLServer

```bash
DB_TYPE=sqlserver
DB_CONNECTION_STRING="Server=localhost;Database=juggle;User Id=sa;Password=your_password;TrustServerCertificate=true;"
```

---

## 五、环境变量参考

| 变量 | 说明 | 默认值 |
|------|------|--------|
| `DB_TYPE` | 数据库类型：sqlite / mysql / postgresql / sqlserver | `sqlite` |
| `DB_PATH` | SQLite 文件路径 | `juggle.db` |
| `DB_HOST` | 数据库主机 | — |
| `DB_PORT` | 数据库端口 | — |
| `DB_NAME` | 数据库名 | — |
| `DB_USER` | 数据库用户 | — |
| `DB_PASS` | 数据库密码 | — |
| `DB_CONNECTION_STRING` | 完整连接字符串（覆盖上述参数） | — |
| `Jwt__Key` | JWT 签名密钥 | 内置默认值 |
| `ASPNETCORE_URLS` | 监听地址 | `http://+:9127` |

### JWT 密钥配置（生产环境必须修改）

```bash
# 至少32字符
Jwt__Key="your-super-secret-key-at-least-32-characters-long!"
```

---

## 六、Nginx 反向代理

```nginx
server {
    listen 80;
    server_name juggle.example.com;

    location / {
        proxy_pass http://127.0.0.1:9127;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        client_max_body_size 50m;
    }
}
```

---

## 七、健康检查

```bash
curl http://localhost:9127/api/health
# 返回: {"code":200,"message":"ok"}
```

Docker 健康检查自动使用此端点。

---

## 八、升级指南

### Docker 升级

```bash
docker pull pythonhuang/juggle-net8:v1.0
docker stop juggle
docker rm juggle
docker run -d --name juggle -p 9127:9127 -v juggle_data:/data pythonhuang/juggle-net8:v1.0
```

### 手动升级

```bash
# 1. 停止服务
sudo systemctl stop juggle

# 2. 备份数据库
cp /data/juggle.db /data/juggle.db.bak.$(date +%Y%m%d)

# 3. 替换文件
cd JuggleNet6.Frontend && npm run build
cd ../Juggle.Api && dotnet publish -c Release -o /opt/juggle

# 4. 启动服务
sudo systemctl start juggle
```

---

## 九、故障排查

### 端口被占用

```bash
# 查看端口占用
netstat -ano | findstr 9127   # Windows
lsof -i :9127                  # Linux

# 修改监听端口
ASPNETCORE_URLS="http://+:8080"
```

### 数据库连接失败

1. 检查 `DB_TYPE` 是否正确
2. 检查数据库服务是否运行
3. SQLite 默认自动创建，确保目录可写

### 前端无法访问后端

1. 开发模式：检查 Vite 代理配置（`vite.config.ts`）
2. 生产模式：前端已构建到 `wwwroot/`，后端直接 serve

### 日志查看

```bash
# systemd
journalctl -u juggle -f

# Docker
docker logs -f juggle
```

---

## 十、市场发现与分享配置（v1.8）

### 10.1 本地 market 目录

- 市场「发现」拉取官方市场时，条目文件落在后端运行目录下的 `market/` 目录（`market/{type}/{id}.json` + `market/index.json`）。
- 分享时也会在该目录生成 `{id}.json` 并更新 `index.json`（同名条目复用原 id）。
- Docker 部署建议将该目录挂载到卷以持久化：

```bash
docker run -d --name juggle -p 9127:9127 \
  -v juggle_data:/data \
  -v juggle_market:/app/market \
  pythonhuang/juggle-net8:v1.0
```

> 官方市场仓库：`https://github.com/pythonHuang/APIWorkflowCanvasDesignNet`（market 目录由管理员审核 PR 后维护）。

### 10.2 分享到官方市场（GitHub 授权）

1. 在 GitHub 创建 Personal Access Token（classic）：
   `Settings → Developer settings → Personal access tokens → Tokens (classic) → Generate new token`，
   勾选 **repo** 权限（fork/写文件/创建 PR 所需）。
2. 分享弹窗中粘贴 Token（仅保存在本机浏览器 localStorage，不落库、不上传服务器），填写作者与版本号。
3. 系统自动完成：校验令牌 → 确保 fork → 创建分支 → 写入 `market/{type}/{id}.json` → 合并官方最新 `index.json` → 创建 Pull Request。
4. 管理员审核合并 PR 后，条目进入官方市场；其他用户通过「市场 → 发现」拉取。

### 10.3 常见问题

| 问题 | 处理 |
|------|------|
| 发现提示 index.json 获取失败 404 | 官方仓库尚未建立 market 目录/网络受限，稍后重试 |
| 分享提示 Fork 创建超时 | 登录 GitHub 手动 fork 一次后重试 |
| PR 创建失败（token 无权限） | 确认 Token 勾选了 repo 权限且未过期 |
| 大模型节点报"未配置 AI 大模型" | 系统设置 → 大模型设置 添加并启用供应商（支持 DeepSeek/通义千问/Kimi/OpenAI/GLM/Ollama） |
