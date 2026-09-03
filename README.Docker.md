# Docker 部署指南

## 快速启动

```bash
# 进入后端目录
cd JuggleNet6

# 一键构建并启动（首次构建约需 3-5 分钟）
docker compose up -d --build

# 查看日志
docker compose logs -f juggle
```

访问：http://localhost:9127  
默认账号：**juggle / juggle**  
Swagger：http://localhost:9127/swagger

---

## 常用命令

```bash
# 停止
docker compose down

# 停止并删除数据卷（⚠️ 数据库数据会丢失）
docker compose down -v

# 重新构建（代码有变更后）
docker compose up -d --build

# 进入容器
docker exec -it juggle sh

# 查看 SQLite 数据库位置（容器内）
docker exec -it juggle ls -la /data/
```

---

## 端口映射

默认映射宿主机 `9127` → 容器 `9127`。

如需修改宿主机端口，编辑 `docker-compose.yml`：
```yaml
ports:
  - "8080:9127"   # 改为 8080
```

---

## 数据持久化

SQLite 数据库存储在 Docker 命名卷 `juggle_data`，路径对应容器内 `/data/juggle.db`。

查看卷位置：
```bash
docker volume inspect juggle_juggle_data
```

备份数据库：
```bash
docker cp juggle:/data/juggle.db ./juggle_backup.db
```

恢复数据库：
```bash
docker cp ./juggle_backup.db juggle:/data/juggle.db
docker compose restart juggle
```

---

## 环境变量

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | 运行环境 |
| `ASPNETCORE_URLS` | `http://+:9127` | 监听地址 |
| `DB_PATH` | `/data/juggle.db` | SQLite 文件路径 |
| `Jwt__Key` | （见 appsettings） | JWT 密钥，生产建议覆盖 |

在 `docker-compose.yml` 的 `environment` 节添加：
```yaml
environment:
  - Jwt__Key=your-production-secret-key-min-32-chars
```

---

## 单独构建镜像

```bash
# 构建镜像
docker build -t juggle-net8:latest .

# 运行（不用 compose）
docker run -d \
  --name juggle \
  -p 9127:9127 \
  -v juggle_data:/data \
  -e DB_PATH=/data/juggle.db \
  juggle-net8:latest
```

---

## 更新日志

### v1.8（最新）

- 📊 **报表模块** — 数据视图 + 报表设计器 + 公式引擎（SUM/AVG/COUNT/IF 等）
- 📈 **监控模块** — API 拓扑图（健康检查/访问统计/状态颜色）+ 告警规则/记录
- 🧮 **表达式引擎** — 条件/赋值节点支持算术 + - * / %、字符串拼接/切片/replace、toString 数字与日期格式化
- 🗄️ **数据库节点 SQL 辅助** — 表/视图/存储过程浏览一键生成 SQL、单独测试 SQL（更改事务回滚）
- 🔀 **条件节点扩展** — && || 括号、双变量比较、子属性、数组 length/元素取值
- 🔗 **接口直连访问** — `/open/api/{code}` + 访问别名 + 停用/启用开关
- 🧩 **WSDL 解析增强** — generatedXSD / xsd:include/import 递归加载 / XSD 内容粘贴

> 镜像更新：重新 `docker compose up -d --build` 即可升级（SQLite 数据卷自动保留）。
