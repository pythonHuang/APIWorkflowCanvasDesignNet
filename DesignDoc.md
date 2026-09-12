# Juggle 接口编排平台 - 系统详细设计文档

> 版本：v1.6  
> 日期：2026-05-19  
> 技术栈：ASP.NET Core 8 + Vue3 + SQLite/Multi-DB + EF Core 8（DDD 四层架构）

---

## 一、数据库详细设计

### 1.1 t_user（用户表）

```sql
CREATE TABLE t_user (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    user_name   TEXT(30),
    password    TEXT(60),       -- MD5 加密
    deleted     INTEGER DEFAULT 0,
    created_at  TEXT,           -- ISO 8601 datetime
    created_by  INTEGER,
    updated_at  TEXT,
    updated_by  INTEGER
);
-- 初始数据：juggle / juggle (MD5: 24cb6bcbc65730e9650745d379613563)
```

### 1.2 t_suite（套件表）

```sql
CREATE TABLE t_suite (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    suite_code           TEXT(60),       -- 套件唯一标识
    suite_name           TEXT(30),       -- 套件名称
    suite_classify_id    INTEGER,        -- 分类ID（可为空）
    suite_image          TEXT,           -- 图片（Base64 或 URL）
    suite_version        TEXT(10),       -- 版本号（如 v1.0.0）
    suite_desc           TEXT(140),      -- 描述
    suite_help_doc_json  TEXT(300),      -- 帮助文档 JSON
    suite_flag           INTEGER,        -- 0:自定义, 1:官方
    deleted              INTEGER DEFAULT 0,
    created_at           TEXT,
    created_by           INTEGER,
    updated_at           TEXT,
    updated_by           INTEGER
);
```

### 1.3 t_api（API 接口表）

```sql
CREATE TABLE t_api (
    id                       INTEGER PRIMARY KEY AUTOINCREMENT,
    suite_id                 INTEGER,        -- 所属套件 ID
    api_code                 TEXT(100),      -- 接口编码（URL Path + Method 的 MD5）
    api_protocol             TEXT(10),       -- 协议（HTTP/HTTPS）
    api_url                  TEXT(150),      -- 接口完整地址
    api_name                 TEXT(50),       -- 接口名称
    api_desc                 TEXT(200),      -- 接口描述
    api_request_type         TEXT(10),       -- 请求类型（GET/POST/PUT/DELETE）
    api_request_content_type TEXT(40),       -- 请求体类型
    deleted                  INTEGER DEFAULT 0,
    created_at               TEXT,
    created_by               INTEGER,
    updated_at               TEXT,
    updated_by               INTEGER
);
```

### 1.4 t_parameter（参数表）

统一管理 API 入参/出参/Header、对象属性、流程入参/出参：

```sql
CREATE TABLE t_parameter (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    param_type   INTEGER(2),     -- 1:API入参, 2:API出参, 3:对象属性, 4:Header, 5:流程入参, 6:流程出参
    param_key    TEXT(40),       -- 参数 key
    param_name   TEXT(40),       -- 参数名称
    param_position TEXT(20),     -- 参数位置（query/body/header/path）
    param_desc   TEXT(200),      -- 参数描述
    data_type    TEXT,           -- 数据类型 JSON（如 {"type":"String"}）
    required     INTEGER(2),     -- 是否必填（1:是, 0:否）
    source_type  TEXT(8),        -- 来源类型（api/object/flow）
    source_id    INTEGER,        -- 来源 ID（ApiId/ObjectId/FlowDefinitionId）
    deleted      INTEGER DEFAULT 0,
    created_at   TEXT,
    created_by   INTEGER,
    updated_at   TEXT,
    updated_by   INTEGER
);
```

**param_type 枚举值：**

| 值 | 说明 |
|----|------|
| 1 | API 接口入参 |
| 2 | API 接口出参 |
| 3 | 对象属性 |
| 4 | API Header |
| 5 | 流程入参（source_type=flow） |
| 6 | 流程出参（source_type=flow） |

**data_type JSON 结构：**

```json
// 基础类型
{"type": "String"}
{"type": "Integer"}
{"type": "Double"}
{"type": "Boolean"}
{"type": "Date"}

// 集合类型
{"type": "List", "itemType": "String"}
{"type": "List", "itemType": "Object", "objectKey": "OrderDTO", "objectStructure": [...]}

// 对象类型
{"type": "Object", "objectKey": "OrderDTO", "objectStructure": [
  {"propKey": "orderNo", "propName": "订单号", "dataType": {"type": "String"}},
  {"propKey": "userId", "propName": "用户ID", "dataType": {"type": "Integer"}}
]}
```

### 1.5 t_object（自定义对象表）

```sql
CREATE TABLE t_object (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    object_key  TEXT(30),       -- 对象唯一标识（全局唯一）
    object_name TEXT(50),       -- 对象名称
    object_desc TEXT(200),      -- 描述
    deleted     INTEGER DEFAULT 0,
    created_at  TEXT,
    created_by  INTEGER,
    updated_at  TEXT,
    updated_by  INTEGER
);
-- 对象属性存储在 t_parameter 中（param_type=3, source_type='object', source_id=ObjectId）
```

### 1.6 t_flow_definition（流程定义表）

```sql
CREATE TABLE t_flow_definition (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    flow_key     TEXT(20),       -- 流程唯一标识（全局唯一，自动生成）
    flow_name    TEXT(60),       -- 流程名称
    flow_type    TEXT(8),        -- 流程类型（sync/async）
    flow_content TEXT,           -- 流程内容（节点列表 JSON）
    remark       TEXT(200),      -- 备注
    deleted      INTEGER DEFAULT 0,
    created_at   TEXT,
    created_by   INTEGER,
    updated_at   TEXT,
    updated_by   INTEGER
);
-- 流程入参/出参存储在 t_parameter 中（source_type='flow', source_id=FlowDefinitionId）
```

### 1.7 t_variable_info（变量信息表）

```sql
CREATE TABLE t_variable_info (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    flow_definition_id   INTEGER,        -- 所属流程定义 ID
    variable_key         TEXT(30),       -- 变量唯一 key
    variable_name        TEXT(30),       -- 变量名称
    variable_type        INTEGER(1),     -- 1:输入变量, 2:输出变量, 3:环境变量
    data_type            TEXT,           -- 数据类型 JSON
    created_at           TEXT,
    created_by           INTEGER,
    updated_at           TEXT,
    updated_by           INTEGER
);
```

### 1.8 t_flow_info（流程信息表）

流程定义部署后生成：

```sql
CREATE TABLE t_flow_info (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    flow_key    TEXT(20),       -- 流程唯一标识（与 flow_definition 一致）
    flow_name   TEXT(60),
    flow_type   TEXT(8),
    remark      TEXT(200),
    deleted     INTEGER DEFAULT 0,
    created_at  TEXT,
    created_by  INTEGER,
    updated_at  TEXT,
    updated_by  INTEGER
);
```

### 1.9 t_flow_version（流程版本表）

```sql
CREATE TABLE t_flow_version (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    flow_id              INTEGER,        -- 所属 FlowInfo ID
    flow_version         TEXT(8),        -- 版本号（v1, v2, ...）
    flow_version_status  INTEGER DEFAULT 0,  -- 0:禁用, 1:启用
    flow_version_remark  TEXT(200),
    flow_content         TEXT,           -- 部署时的流程内容快照
    inputs               TEXT,           -- 入参 JSON 快照
    outputs              TEXT,           -- 出参 JSON 快照
    variables            TEXT,           -- 变量 JSON 快照
    deleted              INTEGER DEFAULT 0,
    created_at           TEXT,
    created_by           INTEGER,
    updated_at           TEXT,
    updated_by           INTEGER
);
```

### 1.10 t_token（访问令牌表）

```sql
CREATE TABLE t_token (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    token_value TEXT(150),      -- 令牌值（UUID 生成）
    token_desc  TEXT(200),
    deleted     INTEGER DEFAULT 0,
    created_at  TEXT,
    created_by  INTEGER,
    updated_at  TEXT,
    updated_by  INTEGER
);
```

### 1.11 t_data_source（数据源表）

```sql
CREATE TABLE t_data_source (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    data_source_name     TEXT(150),
    data_source_type     TEXT(20),       -- 数据库类型（MySQL/PostgreSQL）
    data_source_desc     TEXT(200),
    address              TEXT(200),      -- 主机地址
    port                 TEXT(10),
    user_name            TEXT(40),
    password             TEXT(40),       -- 建议加密存储
    database_name        TEXT(40),
    connect_ext_info     TEXT(200),      -- 额外连接参数
    min_pool_size        INTEGER,
    max_pool_size        INTEGER,
    query_timeout        INTEGER,
    data_source_ext_info TEXT(200),
    deleted              INTEGER DEFAULT 0,
    created_at           TEXT,
    created_by           INTEGER,
    updated_at           TEXT,
    updated_by           INTEGER
);
```

### 1.12 t_flow_log（流程执行主日志表）

```sql
CREATE TABLE t_flow_log (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    flow_key        TEXT(20),           -- 流程唯一标识
    flow_version    TEXT(8),            -- 执行版本号
    status          TEXT(10),           -- SUCCESS / FAILED
    execute_time    INTEGER,            -- 执行耗时（ms）
    inputs          TEXT,               -- 入参 JSON 快照
    outputs         TEXT,               -- 出参 JSON 快照
    error_message   TEXT,               -- 错误信息（失败时）
    created_at      TEXT
);
```

### 1.13 t_flow_node_log（节点执行明细日志表）

```sql
CREATE TABLE t_flow_node_log (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    flow_log_id     INTEGER,            -- 所属主日志 ID
    node_key        TEXT(20),           -- 节点 key
    node_type       TEXT(20),           -- 节点类型（START/END/METHOD等）
    seq             INTEGER,            -- 执行顺序序号
    status          TEXT(10),           -- SUCCESS / FAILED
    execution_time  INTEGER,            -- 节点耗时（ms）
    input_snapshot  TEXT,               -- 节点执行前变量快照（JSON）
    output_snapshot TEXT,               -- 节点执行后变量快照（JSON）
    detail          TEXT,               -- 节点执行详情
    error_message   TEXT,               -- 节点错误信息
    start_time      TEXT,
    end_time        TEXT
);
```

### 1.14 t_static_variable（全局静态变量表）

```sql
CREATE TABLE t_static_variable (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    var_code        TEXT(30) UNIQUE,    -- 变量唯一标识
    var_name        TEXT(50),           -- 变量名称
    data_type       TEXT(20),           -- string/integer/double/boolean/date/json
    value           TEXT,               -- 当前值
    default_value   TEXT,               -- 默认值
    group_name      TEXT(50),           -- 分组名称（可为空）
    remark          TEXT(200),          -- 备注
    deleted         INTEGER DEFAULT 0,
    created_at      TEXT,
    created_by      INTEGER,
    updated_at      TEXT,
    updated_by      INTEGER
);
```

---

## 二、后端 API 详细设计

### 2.1 统一响应格式

**成功响应：**
```json
{
  "code": 200,
  "message": "success",
  "data": { }
}
```

**分页响应：**
```json
{
  "code": 200,
  "message": "success",
  "total": 100,
  "data": [ ]
}
```

**错误响应：**
```json
{
  "code": 40001,
  "message": "流程定义不存在",
  "data": null
}
```

### 2.2 错误码规范

| 错误码区间 | 说明 |
|-----------|------|
| 200 | 成功 |
| 40001 ~ 40099 | 流程相关错误 |
| 40101 ~ 40199 | API/套件相关错误 |
| 40201 ~ 40299 | 对象相关错误 |
| 40301 ~ 40399 | 系统相关错误 |
| 50000 | 服务器内部错误 |

### 2.3 认证 JWT 设计

**登录接口：**
```
POST /api/user/login
Body: { "userName": "juggle", "password": "juggle" }
Response: { "code": 200, "data": { "token": "eyJ..." } }
```

**后续请求携带 Token：**
```
Authorization: Bearer eyJ...
```

**JWT Payload：**
```json
{
  "userId": 1,
  "userName": "juggle",
  "exp": 1704067200
}
```

---

## 三、核心业务流程详细设计

### 3.1 流程设计到部署完整流程

```
1. 创建流程定义
   POST /api/flow/definition/add
   Body: { flowName, flowType, remark, inputs[], outputs[] }
   → 生成 flow_key（格式：flowType_随机10位）
   → 初始化默认流程内容（开始节点 + 结束节点）
   → 保存入参/出参到 t_parameter（source_type='flow'）

2. 流程设计（拖拽编辑）
   PUT /api/flow/definition/save
   Body: { flowDefinitionId, flowContent, variableList[] }
   → 更新 flow_content（JSON 节点列表）
   → 更新/删除 t_variable_info

3. 调试流程
   POST /api/flow/definition/debug/{flowKey}
   Body: { flowData: { input_userName: "juggle", ... } }
   → 直接基于 flow_definition 中的 flow_content 执行流程引擎
   → 返回执行结果

4. 部署流程
   POST /api/flow/definition/deploy
   Body: { flowDefinitionId, versionRemark }
   → 读取 flow_definition 的当前内容
   → 在 t_flow_info 中创建/找到对应记录
   → 计算新版本号（v1, v2, ...）
   → 在 t_flow_version 中创建新版本（快照 inputs/outputs/variables/flow_content）
   → 新版本默认禁用（flow_version_status=0）
```

### 3.2 流程执行（触发）详细设计

```
触发接口（带版本）：POST /api/flow/version/trigger/{version}/{key}
开放接口（带版本）：GET/POST /open/flow/trigger/{version}/{key}
开放接口（不带版本，取最新已发布版本）：GET/POST /open/flow/trigger/{key}

执行步骤：
1. 根据 flow_key + flow_version 查询 t_flow_version
   （不带版本号时：查询 flow_version_status=1 的最新版本）
2. 检查版本状态（必须为启用状态）
3. 反序列化 flow_content → List<FlowElement>
4. 反序列化 variables → 初始化变量上下文
5. 反序列化 inputs → 验证并填充输入变量到上下文
6. 从 START 节点开始执行（findNextNode）
7. 循环执行节点直到 END（见节点类型说明）
8. 从变量上下文中读取 outputs 对应的变量值
9. 保存执行日志（t_flow_log + t_flow_node_log）
10. 回写被修改的静态变量到 t_static_variable
11. 构建并返回 FlowResult
```

### 3.3 流程引擎 C# 核心类设计

```csharp
// 流程执行上下文
public class FlowExecutionContext
{
    public string FlowKey { get; set; }
    public string FlowVersion { get; set; }
    public Dictionary<string, object> Variables { get; set; }  // 运行时变量
    public List<FlowElement> Elements { get; set; }            // 节点列表
}

// 基础节点
public abstract class FlowElement
{
    public string Key { get; set; }
    public string Name { get; set; }
    public ElementType ElementType { get; set; }
    public List<string> Incomings { get; set; }
    public List<string> Outgoings { get; set; }
}

// 方法节点
public class MethodNode : FlowElement
{
    public MethodConfig Method { get; set; }
}

public class MethodConfig
{
    public string SuiteCode { get; set; }
    public string MethodCode { get; set; }
    public string Url { get; set; }
    public string RequestType { get; set; }
    public string RequestContentType { get; set; }
    public List<InputFillRule> InputFillRules { get; set; }
    public List<OutputFillRule> OutputFillRules { get; set; }
    public List<HeaderFillRule> HeaderFillRules { get; set; }
}

// 条件节点
public class ConditionNode : FlowElement
{
    public List<ConditionItem> Conditions { get; set; }
}

public class ConditionItem
{
    public string ConditionName { get; set; }
    public string ConditionType { get; set; }  // CUSTOM / DEFAULT
    public string Outgoing { get; set; }
    public string Expression { get; set; }
    public List<List<ConditionExpression>> ConditionExpressions { get; set; }
}

// 填充规则
public class FillRule
{
    public string Source { get; set; }
    public string SourceType { get; set; }   // VARIABLE / CONSTANT / OUTPUT_PARAM / INPUT_PARAM
    public DataType SourceDataType { get; set; }
    public string Target { get; set; }
    public string TargetType { get; set; }
    public DataType TargetDataType { get; set; }
}

// 赋值节点
public class AssignNode : FlowNode
{
    public List<AssignRule> AssignRules { get; set; }
}

public class AssignRule
{
    public string Source { get; set; }        // 来源值（变量key或常量值）
    public string SourceType { get; set set; }  // CONSTANT / VARIABLE / STATIC
    public string Target { get; set; }        // 目标变量 key
    public string TargetType { get; set; }    // VARIABLE / STATIC
    public string DataType { get; set; }      // string/integer/double/boolean/date
}

// 代码节点
public class CodeNode : FlowNode
{
    public CodeConfig CodeConfig { get; set; }
}

public class CodeConfig
{
    public string ScriptType { get; set; }   // javascript
    public string Script { get; set; }       // JS 代码（支持 $var / $static 对象）
}

// 数据库节点
public class MysqlNode : FlowNode
{
    public MysqlConfig MysqlConfig { get; set; }
}

public class MysqlConfig
{
    public long DataSourceId { get; set; }   // 数据源 ID
    public string SqlType { get; set; }      // SELECT / UPDATE / INSERT / DELETE
    public string Sql { get; set; }          // SQL 模板，支持 ${varName} 替换
    public List<OutputFillRule> OutputFillRules { get; set; }
}

// 子流程节点（迭代五新增）
public class SubFlowNode : FlowNode
{
    public SubFlowConfig SubFlowConfig { get; set; }
}

public class SubFlowConfig
{
    public string SubFlowKey { get; set; }               // 被调用的子流程 key
    public List<SubFlowMapping> InputMappings { get; set; }   // 入参映射（当前变量→子流程入参）
    public List<SubFlowMapping> OutputMappings { get; set; }  // 出参映射（子流程出参→当前变量）
}

public class SubFlowMapping
{
    public string Source { get; set; }   // 来源变量 key
    public string Target { get; set; }   // 目标变量 key
}

// 模板转换节点
public class TransformNode : FlowNode
{
    public TransformConfig TransformConfig { get; set; }
}

public class TransformConfig
{
    public string TargetType { get; set; }   // INPUT/OUTPUT/VARIABLE/STATIC
    public string TargetCode { get; set; }   // 目标变量/参数 key
    public string Template { get; set; }     // 模板文本，支持${var|pipe}
}

// 流程引擎
public class FlowEngine
{
    public async Task<FlowResult> ExecuteAsync(FlowExecutionContext context);
    private FlowElement FindStartNode(List<FlowElement> elements);
    private FlowElement FindNextNode(FlowElement current, FlowExecutionContext context);
    private async Task ExecuteMethodNodeAsync(MethodNode node, FlowExecutionContext context);
    private FlowElement ExecuteConditionNode(ConditionNode node, FlowExecutionContext context);
    private bool EvaluateExpression(string expression, Dictionary<string, object> variables);
}
```

### 3.4 HTTP API 调用器设计

```csharp
public class HttpApiCaller
{
    // 根据 API 配置和入参发起 HTTP 请求
    public async Task<Dictionary<string, object>> CallAsync(
        string url,
        string requestType,        // GET/POST/PUT/DELETE
        string contentType,        // application/json / application/x-www-form-urlencoded
        Dictionary<string, string> headers,
        Dictionary<string, object> queryParams,
        Dictionary<string, object> bodyParams
    );
}
```

调用逻辑：
1. 根据 inputFillRules 从变量上下文提取参数
2. 根据参数 position（query/body/header）分类
3. 发起 HTTP 请求
4. 解析响应 JSON
5. 根据 outputFillRules 将响应字段写入变量上下文

### 3.5 条件表达式求值

由 `Juggle.Domain/Engine/ExpressionEvaluator.cs` 递归下降解析（条件节点与赋值节点 EXPRESSION 共用），支持的语法：

| 类型 | 语法 | 示例 |
|--------|---------|------|
| 比较运算 | == != > < >= <= | `env_score >= 60`、`input_name == '张三'` |
| 逻辑运算 | &&（且） \|\|（或） !（非） 括号 | `env_score >= 60 && env_score < 90`、`(env_a > 1 \|\| env_b > 2) && env_c == 'yes'` |
| 算术运算 | + - * / %（先运算再比较） | `(env_price - env_cost) * env_qty >= 100`、`env_total % 2 == 0` |
| 变量对变量 | 左右两边都可以是变量 | `env_total >= input_min` |
| 子属性 | 变量.属性 | `input_user.name == '张三'` |
| 数组/字符串长度 | 变量.length | `input_list.length > 0`、`env_name.length == 6` |
| 数组元素 | 变量[索引]（从 0 开始），可继续取子属性 | `input_list[0].name == '张三'` |
| 字符串拼接 | +（两边都是数字时做加法，否则拼接） | `env_name + '_done' == 'abc_done'` |
| 字符串切片 | [..5] 前5位 / [2..5] 第2~5位 / [2..] 到最后 | `input_phone[..3] == '138'`、`env_code[2..5] == 'abc'` |
| 字符串替换 | replace('旧','新') | `env_name.replace('a','b') == 'xbcdef'` |
| 数字格式化 | toString("#.0##") / toString("0.00") | `env_amount.toString('0.00') == '12.35'` |
| 日期格式化 | toString("yyyy-MM-dd HH:mm:ss") | `env_time.toString('yyyy-MM-dd') == '2024-01-02'` |
| 裸变量 | 无比较符时按真值判断 | `env_flag` 等价于 `env_flag == true` |

规则：字面量支持数字、单/双引号字符串、`true`/`false`/`null`；`*` 会把数字字符串转为数字计算；未加引号且不存在的标识符按字符串字面量处理（兼容 `env_status == paid` 旧写法）；除零返回 null；解析失败返回 false（条件节点走默认分支）。

赋值节点（ASSIGN）来源类型新增 `EXPRESSION`：规则 source 填表达式，求值结果写入目标变量（支持以上全部语法，无比较符时返回计算结果值）。

### 3.6 数据库节点 SQL 设计辅助

流程设计器中数据库节点提供 SQL 辅助能力（后端 `DataSourceService` + `api/system/datasource` 端点）：

| 接口 | 说明 |
|--------|------|
| `POST api/system/datasource/metadata` | 获取数据源的表/视图/存储过程列表（按 6 种数据库类型适配元数据 SQL） |
| `POST api/system/datasource/columns` | 获取表/视图字段列表（统一用 `SELECT * FROM t WHERE 1=0` 取元数据，跨库通用） |
| `POST api/system/datasource/test-sql` | 单独测试 SQL：QUERY 返回前 100 行预览；UPDATE 在事务中执行并回滚，不污染数据。模板变量 `${varName}` 由测试参数（JSON）提供值，未提供的替换为空字符串 |

前端设计器：点击"表/视图/存储过程"浏览对象、查看字段、一键生成 SELECT（按数据源类型自动带 `LIMIT 100` / `SELECT TOP 100` / `ROWNUM <= 100`）或 CALL/EXEC 调用语句；点击"测试 SQL"弹出测试参数（自动提取 SQL 中的 `${varName}`）并展示结果或数据库错误信息。

---

## 四、前端详细设计

### 4.1 流程设计器核心交互

**节点列表（左侧面板）：**
- 开始节点（只能有一个）
- 结束节点
- 方法节点（调用 HTTP API）
- 条件节点（多分支判断）
- 赋值节点（变量赋值/类型转换，支持静态变量读写）
- 代码节点（执行 JavaScript 脚本，支持 `$var`/`$static` 对象）
- 数据库节点（SQL 查询，支持 `${varName}` 模板替换）
- 聚合节点（多分支汇聚）
- **子流程节点**（调用已发布的其他流程，支持入参/出参变量映射）
- **模板转换节点**（${var|pipe} 语法替换占位符，支持静态/实例方法调用，结果赋值到入参/出参/变量/静态）

**画布区：**
- 节点拖拽放置
- 节点连线（incomings/outgoings）
- 节点点击展开配置面板

**配置面板（右侧抽屉）：**
- 方法节点：选择套件 → 选择 API → 配置输入规则 → 配置输出规则 → 配置 Header
- 条件节点：配置多个条件分支 + 每个分支的表达式

**左侧菜单：**
- 流程入参/出参配置
- 变量管理（添加/删除变量）

### 4.2 数据类型选择器

```typescript
// 数据类型树形结构
interface DataType {
  type: 'String' | 'Integer' | 'Double' | 'Boolean' | 'Date' | 'List' | 'Object';
  itemType?: string;        // List 的元素类型
  objectKey?: string;       // Object 的对象 key
  objectStructure?: Property[];  // Object 的属性列表
}
```

### 4.3 参数配置组件（ParamSetting）

通用参数配置表格，支持：
- 新增/删除参数行
- 参数 key、名称、数据类型、是否必填
- 数据类型支持级联选择（基础类型/集合类型/对象类型）

### 4.4 输入规则配置（InputRuleSetting）

用于配置「变量 → API 入参」或「常量 → API 入参」的映射：

```typescript
interface InputFillRule {
  source: string;           // 来源（变量 key 或常量值）
  sourceType: 'VARIABLE' | 'CONSTANT';
  sourceDataType?: DataType;
  target: string;           // 目标 API 入参 key
  targetType: 'INPUT_PARAM';
  targetDataType: DataType;
}
```

### 4.5 输出规则配置（OutputRuleSetting）

用于配置「API 出参 → 变量」的映射：

```typescript
interface OutputFillRule {
  source: string;           // 来源 API 出参 key
  sourceType: 'OUTPUT_PARAM';
  sourceDataType: DataType;
  target: string;           // 目标变量 key
  targetType: 'VARIABLE';
  targetDataType: DataType;
}
```

---

## 五、接口详细设计

### 5.1 用户登录

**接口：** `POST /api/user/login`

**请求：**
```json
{
  "userName": "juggle",
  "password": "juggle"
}
```

**响应：**
```json
{
  "code": 200,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "userId": 1,
    "userName": "juggle"
  }
}
```

### 5.2 创建流程定义

**接口：** `POST /api/flow/definition/add`

**请求：**
```json
{
  "flowName": "用户登录送礼品",
  "flowType": "sync",
  "remark": "示例流程",
  "inputs": [
    { "paramKey": "userName", "paramName": "用户名", "dataType": {"type": "String"}, "required": true }
  ],
  "outputs": [
    { "paramKey": "orderName", "paramName": "订单名称", "dataType": {"type": "String"} }
  ]
}
```

**响应：**
```json
{
  "code": 200,
  "data": true
}
```

### 5.3 保存流程内容

**接口：** `PUT /api/flow/definition/save`

**请求：**
```json
{
  "flowDefinitionId": 1,
  "flowContent": "[{...节点列表JSON...}]",
  "variableList": [
    {
      "variableKey": "env_isLogin",
      "variableName": "是否登录成功",
      "variableType": 3,
      "dataType": {"type": "Boolean"}
    }
  ]
}
```

### 5.4 调试流程

**接口：** `POST /api/flow/definition/debug/{flowKey}`

**请求：**
```json
{
  "flowData": {
    "userName": "juggle",
    "password": "123456",
    "deposit": 50000.0
  }
}
```

**响应：**
```json
{
  "code": 200,
  "data": {
    "success": true,
    "result": {
      "userName": "juggle",
      "age": 18,
      "orderName": "送10元话费"
    },
    "errorMessage": null
  }
}
```

### 5.5 部署流程

**接口：** `POST /api/flow/definition/deploy`

**请求：**
```json
{
  "flowDefinitionId": 1,
  "versionRemark": "第一版"
}
```

**响应：**
```json
{
  "code": 200,
  "data": true
}
```

### 5.6 触发流程（开放接口）

**接口（带版本号）：** `GET/POST /open/flow/trigger/{version}/{key}`

**接口（不带版本号，自动取最新已发布版本）：** `GET/POST /open/flow/trigger/{key}`

**示例：** 
- `POST /open/flow/trigger/v1/sync_abcdefghij`
- `POST /open/flow/trigger/sync_abcdefghij`

**请求头：**
```
X-Access-Token: your-api-token
Content-Type: application/json
```

**请求：**
```json
{
  "flowData": {
    "userName": "juggle",
    "password": "123456",
    "deposit": 200000.0
  }
}
```

**同步响应：**
```json
{
  "code": 200,
  "data": {
    "success": true,
    "result": {
      "userName": "juggle",
      "age": 18,
      "orderName": "送一双耐克的鞋"
    }
  }
}
```

### 5.7 新增套件

**接口：** `POST /api/suite/add`

**请求：**
```json
{
  "suiteName": "微信服务",
  "suiteVersion": "v1.0.0",
  "suiteDesc": "微信相关接口套件",
  "suiteImage": "base64..."
}
```

### 5.8 新增 API 接口

**接口：** `POST /api/api/add`

**请求：**
```json
{
  "suiteId": 1,
  "apiName": "发送模板消息",
  "apiUrl": "https://api.weixin.qq.com/cgi-bin/message/template/send",
  "apiRequestType": "POST",
  "apiRequestContentType": "application/json",
  "apiDesc": "微信模板消息推送",
  "inputs": [
    { "paramKey": "touser", "paramName": "目标用户", "dataType": {"type": "String"}, "position": "body", "required": true }
  ],
  "outputs": [
    { "paramKey": "msgid", "paramName": "消息ID", "dataType": {"type": "Integer"} }
  ],
  "headers": [
    { "headerKey": "access_token", "headerName": "AccessToken", "defaultValue": "" }
  ]
}
```

### 5.9 调试 API

**接口：** `POST /api/api/debug/{apiId}`

**请求：**
```json
{
  "inputParams": {
    "userId": 1
  },
  "headers": {
    "Authorization": "Bearer xxx"
  }
}
```

**响应：**
```json
{
  "code": 200,
  "data": {
    "id": 1,
    "name": "张三",
    "age": 18,
    "birthday": "2000-01-01"
  }
}
```

### 5.10 流程定义导出

**接口：** `GET /api/flow/definition/export/{id}`

**响应：** 触发浏览器下载 JSON 文件，文件名格式 `flow_{flowKey}.json`

**导出文件结构：**
```json
{
  "exportType": "flow",
  "flowKey": "sync_abcdefghij",
  "flowName": "示例流程",
  "flowType": "sync",
  "flowContent": "[{...节点列表...}]",
  "inputs": [...],
  "outputs": [...],
  "variables": [...]
}
```

### 5.11 流程定义导入

**接口：** `POST /api/flow/definition/import`

**请求：** 上传 JSON 文件（multipart/form-data，字段名 `file`）

**导入规则：**
- 校验 `exportType == "flow"`
- 自动生成新 flowKey（避免与已有流程冲突）
- 流程名自动添加 `_imported` 后缀标识

**响应：**
```json
{ "code": 200, "data": true }
```

### 5.12 套件导出

**接口：** `GET /api/suite/export/{id}`

**导出文件结构：**
```json
{
  "exportType": "suite",
  "suiteName": "示例套件",
  "suiteVersion": "v1.0.0",
  "apis": [
    {
      "apiName": "接口名",
      "apiUrl": "https://...",
      "apiRequestType": "POST",
      "inputs": [...],
      "outputs": [...],
      "headers": [...]
    }
  ]
}
```

### 5.13 套件导入

**接口：** `POST /api/suite/import`

**请求：** 上传 JSON 文件（multipart/form-data，字段名 `file`）

**导入规则：**
- 校验 `exportType == "suite"`
- 自动生成新 suiteCode 和各接口 methodCode
- 完整导入接口的入参/出参/Header 参数

### 5.14 流程调试（改进版）

**接口：** `POST /api/flow/definition/debug/{flowKey}`

**响应（统一返回 HTTP 200）：**
```json
{
  "code": 200,
  "data": {
    "success": true,
    "errorMessage": null,
    "outputs": { "orderName": "送10元话费" },
    "nodeLogs": [
      {
        "nodeKey": "n1",
        "nodeType": "START",
        "seq": 1,
        "status": "SUCCESS",
        "executionTime": 2,
        "inputSnapshot": "{}",
        "outputSnapshot": "{}",
        "startTime": "2026-04-01T10:00:00",
        "endTime": "2026-04-01T10:00:00.002"
      }
    ]
  }
}
```

> 失败时 `success=false`，`errorMessage` 包含错误详情，`nodeLogs` 中失败节点的 `status=FAILED` 且含 `errorMessage`，前端据此高亮红色并显示错误提示。

### 5.15 全局静态变量管理

| 接口 | 方法 | 说明 |
|------|------|------|
| `/api/system/static-variable/list` | GET | 查询静态变量列表 |
| `/api/system/static-variable/add` | POST | 新增静态变量 |
| `/api/system/static-variable/update/{id}` | PUT | 编辑静态变量 |
| `/api/system/static-variable/delete/{id}` | DELETE | 删除静态变量 |
| `/api/system/static-variable/reset/{id}` | POST | 重置当前值为默认值 |

---

## 六、C# 项目结构详细设计

> 本项目采用 DDD 四层架构，已从原始单体结构迁移完成。

### 6.1 后端项目结构

```
JuggleNet6/
├── Juggle.Api/                             # 入口层
│   ├── Controllers/
│   │   ├── Api/                            # 业务管理接口（JWT认证）
│   │   │   ├── FlowDefinitionController.cs # 流程定义（含导入导出）
│   │   │   ├── FlowInfoController.cs
│   │   │   ├── FlowVersionController.cs
│   │   │   ├── FlowLogController.cs        # 执行日志查询
│   │   │   ├── ApiController.cs
│   │   │   ├── SuiteController.cs          # 套件管理（含导入导出）
│   │   │   ├── ObjectController.cs
│   │   │   ├── ParameterController.cs
│   │   │   ├── VariableInfoController.cs
│   │   │   ├── StaticVariableController.cs # 全局静态变量
│   │   │   ├── DataSourceController.cs
│   │   │   ├── TokenController.cs
│   │   │   └── UserController.cs
│   │   └── Open/
│   │       └── FlowOpenController.cs       # 开放触发接口（带版本/不带版本）
│   ├── wwwroot/                            # 前端构建产物
│   └── Program.cs
│
├── Juggle.Application/                     # 应用层
│   ├── Services/
│   │   ├── Flow/
│   │   │   └── FlowExecutionService.cs    # 流程执行核心服务
│   │   └── Impl/
│   │       ├── DataSourceService.cs        # 数据源连接字符串构建+测试
│   │       └── JwtService.cs
│   └── Models/
│       ├── Request/Requests.cs             # 所有请求 DTO
│       └── Response/ApiResult.cs          # 统一响应模型
│
├── Juggle.Domain/                          # 领域层
│   ├── Entities/                           # 15个数据库实体
│   └── Engine/                             # 流程引擎
│       ├── FlowEngine.cs                   # 引擎入口（支持flowContentLoader）
│       ├── FlowContext.cs                  # 执行上下文（含静态变量/节点日志）
│       ├── FlowModels.cs                   # 节点模型定义
│       └── NodeExecutors/                  # 9个节点执行器
│           ├── StartNodeExecutor.cs
│           ├── EndNodeExecutor.cs
│           ├── MethodNodeExecutor.cs
│           ├── ConditionNodeExecutor.cs
│           ├── MergeNodeExecutor.cs
│           ├── AssignNodeExecutor.cs       # 支持STATIC变量读写
│           ├── CodeNodeExecutor.cs         # 支持$var/$static对象
│           ├── MysqlNodeExecutor.cs        # 多数据库支持
│           └── SubFlowNodeExecutor.cs      # 子流程递归执行（迭代五新增）
│
└── Juggle.Infrastructure/                  # 基础设施层
    ├── Persistence/JuggleDbContext.cs      # EF Core DbContext
    └── Common/
        ├── JsonHelper.cs
        └── Md5Helper.cs
```



### 6.2 前端项目结构

```
JuggleNet6.Frontend/
├── index.html
├── package.json
├── vite.config.ts
│
└── src/
    ├── App.vue
    ├── main.ts
    │
    ├── views/
    │   ├── LoginView.vue
    │   ├── LayoutView.vue
    │   ├── flow/
    │   │   ├── FlowDefinitionList.vue  # 流程定义列表（含导入/导出）
    │   │   ├── FlowDesign.vue          # 流程设计器（9种节点，调试，自动布局）
    │   │   ├── FlowLog.vue             # 执行日志（节点时间轴+变量快照）
    │   │   └── FlowVersionList.vue     # 版本列表（启用/禁用）
    │   ├── suite/
    │   │   ├── SuiteList.vue           # 套件列表（含导入/导出）
    │   │   └── ApiDetail.vue           # 接口入参/出参/Header管理
    │   ├── object/
    │   │   └── ObjectList.vue
    │   └── system/
    │       ├── TokenList.vue
    │       ├── DataSourceList.vue      # 四类数据库，测试连接
    │       └── StaticVariable.vue      # 全局静态变量管理
    │
    ├── router/index.ts
    ├── stores/                         # Pinia 状态管理
    └── utils/
```



---

## 七、系统配置设计

### 7.1 后端配置文件（appsettings.json）

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=juggle.db"
  },
  "Jwt": {
    "Secret": "your-jwt-secret-key-at-least-32-chars",
    "ExpireHours": 24
  },
  "Juggle": {
    "ApiTimeout": 30,
    "MaxFlowExecutionSeconds": 60
  },
  "AllowedHosts": "*",
  "Urls": "http://0.0.0.0:9127"
}
```

### 7.2 前端配置文件（.env.development）

```
VITE_API_BASE_URL=http://localhost:9127
```

### 7.3 前端配置文件（.env.production）

```
VITE_API_BASE_URL=/
```

---

## 八、初始化数据设计

系统初始化时需要插入以下数据：

```sql
-- 默认用户：账号 juggle，密码 juggle（MD5 加密）
INSERT INTO t_user (user_name, password, deleted) 
VALUES ('juggle', '24cb6bcbc65730e9650745d379613563', 0);

-- 示例套件
INSERT INTO t_suite (suite_code, suite_name, suite_flag, suite_version, deleted)
VALUES ('example_suite', '系统示例接口套件', 0, 'v1.0.0', 0);

-- 示例接口（7个，对应示例用户/商品/订单接口）
-- （参考原 data.sql）

-- 示例对象
INSERT INTO t_object (object_key, object_name, object_desc, deleted)
VALUES ('OrderDTO', '订单传输对象', '用于示例接口的对象', 0);

-- 示例流程定义
INSERT INTO t_flow_definition (flow_key, flow_name, flow_type, flow_content, remark, deleted)
VALUES ('sync_example', '示例流程', 'sync', '...JSON...', '', 0);
```

---

## 九、编码规范

### 9.1 后端 C# 规范

- 使用 `record` 类型定义 DTO（不可变）
- 服务层使用接口 + 实现分离
- 使用 `ILogger<T>` 做日志记录
- 异步方法统一使用 `async/await` + `Async` 后缀
- 异常通过全局中间件统一处理，业务异常使用自定义 `BizException`
- EF Core 使用 Code First + Migration

### 9.2 前端 Vue3 规范

- 全部使用 `<script setup>` 语法（Composition API）
- 使用 TypeScript 强类型
- 组件命名：大驼峰（PascalCase）
- 函数命名：小驼峰（camelCase）
- 常量命名：大写下划线（UPPER_SNAKE_CASE）
- 响应式数据优先使用 `ref`，对象使用 `reactive`
- API 调用统一通过 service 层，不在组件中直接使用 axios

---

## 十、测试策略

### 10.1 示例流程测试

系统内置示例流程（sync_example）用于验证核心功能：

**测试场景 1：登录失败**
```json
输入：{ "userName": "wrong", "password": "wrong", "deposit": 1000 }
预期：流程在条件节点走"默认else分支"直接结束，无订单返回
```

**测试场景 2：存款 < 10万**
```json
输入：{ "userName": "juggle", "password": "123456", "deposit": 50000 }
预期：返回 orderName = "送10元话费"
```

**测试场景 3：存款 > 10万**
```json
输入：{ "userName": "juggle", "password": "123456", "deposit": 300000 }
预期：返回 orderName = "送一双耐克的鞋"
```

---

## 十一、版本迭代计划

### v1.0（基础版本）✅ 已完成
- [x] 数据库 SQLite + EF Core 初始化
- [x] 用户登录（JWT）
- [x] 套件 CRUD
- [x] API 接口 CRUD
- [x] 自定义对象 CRUD
- [x] 流程定义 CRUD
- [x] 变量管理

### v1.1（流程引擎）✅ 已完成
- [x] 流程引擎核心（START/END/METHOD/CONDITION 节点）
- [x] 流程调试接口
- [x] 流程部署接口
- [x] 流程版本管理（启用/禁用）
- [x] 流程触发（同步）

### v1.2（完善功能）✅ 已完成
- [x] 开放接口（/open）+ Token 认证
- [x] 数据源管理（SQLite/MySQL/PostgreSQL/SQL Server）+ 测试连接
- [x] API 调试
- [x] Swagger 接口文档
- [x] ASSIGN（赋值）/ CODE（代码）/ MYSQL（数据库）/ MERGE（聚合）节点

### v1.3（前端）✅ 已完成
- [x] 登录页
- [x] 流程定义列表 + 编辑
- [x] 流程设计器（@vue-flow/core 拖拽式，自动布局，MiniMap）
- [x] 流程调试（节点高亮、错误提示、nodeLogs 时间轴）
- [x] 套件/API 管理（含接口入参/出参/Header 可视化）
- [x] 对象管理
- [x] 系统设置（Token、数据源）

### v1.4（监控与变量）✅ 已完成
- [x] 全局静态变量（t_static_variable，支持跨流程共享读写）
- [x] 执行日志（t_flow_log + t_flow_node_log，含变量快照时间轴）
- [x] 调试接口改进（统一 HTTP 200，失败节点携带 errorMessage）
- [x] DDD 四层架构重构（Domain/Application/Infrastructure/Api）

### v1.5（子流程与协作）✅ 已完成（2026-04-01）
- [x] SUB_FLOW 子流程节点（递归调用已发布流程，支持入参/出参映射）
- [x] 流程定义导入/导出（JSON 格式，自动生成新 flowKey）
- [x] 套件管理导入/导出（含接口+参数全量导出/导入）
- [x] 开放接口支持不带版本号（自动取最新已发布版本）

### v1.6（模板转换与服务增强）✅ 已完成（2026-05-19）
- [x] TRANSFORM 模板转换节点（${var|pipe} 语法，支持静态/实例方法调用）
- [x] 流程访问别名（通过 /open/services/{alias} 触发最新版本）
- [x] WebService 扩展支持 SOAP 1.2 + 独立配置操作名/命名空间/SOAPAction
- [x] 入参增加参数位置（body/query/rawBody）+ Header 增加默认值
- [x] 流程变量管理支持内联编辑
- [x] 流程参数配置增加"来自对象"按钮（入参/出参）
- [x] 赋值节点增加入参类型来源，方法节点入/出规则增加静态+入参类型
- [x] 接口详情入参/出参/Header 增加"来自对象"按钮
- [x] 接口列表增加接口测试功能（在线调试）
- [x] object/array 类型可关联全局对象类型
- [x] 赋值节点 INPUT_PROPERTY/OUTPUT_PROPERTY 支持级联属性选择
- [x] 流程变量仅保留"中间变量"类型
- [x] 监控仪表盘独立菜单，置于首位
- [x] 复制 URL 剪贴板兼容非 HTTPS 环境

### v1.7（AI 助手与报表）✅ 已完成
- [x] 大模型设置（供应商/模型拉取测试/启停）
- [x] 模型助手管理（自定义助手 + 多轮对话 + 对话历史）
- [x] 流程/接口/报表智能编排助手
- [x] 报表模块（数据视图 + 报表设计器 + 公式引擎 + PDF/Excel 导出）
- [x] 数据库节点 SQL 辅助（表/视图/存储过程浏览 + 测试 SQL）
- [x] 条件/赋值节点表达式增强（算术/字符串/toString/&&||/子属性/数组）

### v1.8（AI 深化与开放生态）✅ 已完成（2026-09）
- [x] AI 节点增强：供应商/模型、图片输入（多模态）、输出目标三选
- [x] AI 节点模型参数：深度思考/温度/最大输出字数/随机种子
- [x] AI 节点工具调用：接口/流程函数调用（tools，最多 4 轮）
- [x] 知识库子系统（4 表 + 文档解析切片 + 文本/向量检索 + 清洗 + 匹配记录）+ KB_SEARCH 节点
- [x] 数据提取节点（json/code/keyword/between/length）
- [x] Redis 缓存查询/设置节点 + Redis 多实例配置
- [x] Skill 管理（CRUD/导入导出）+ AI 节点多技能拼接
- [x] 市场系统（5 类条目/发布/导入去重/收藏/下载/元信息）
- [x] 市场发现（GitHub index.json → 本地 market 目录 → 按 id 导入更新）
- [x] 市场分享（本地生成 id.json+index.json → GitHub fork/分支/PR）
- [x] 模型能力绑定（供应商 capabilities：skills/apis/flows/tools）

---

## 十二、v1.8 新增模块详细设计

### 12.1 新增数据表

#### t_ai_provider（AI 模型供应商表）

| 字段 | 类型 | 说明 |
|------|------|------|
| id | INTEGER | 主键 |
| provider_name | TEXT | 供应商名称 |
| base_url | TEXT | 接口地址（OpenAI 兼容 /v1 一级） |
| api_key | TEXT | API Key |
| model | TEXT | 默认模型 |
| models | TEXT | 可用模型（逗号分隔，对话中可切换） |
| capabilities | TEXT | 能力绑定 JSON：`{"skills":[..],"apis":["code",..],"flows":["key",..],"tools":["web_search"]}` |
| enabled | INTEGER | 1=启用 0=禁用 |

#### t_ai_assistant（AI 自定义助手表）

| 字段 | 类型 | 说明 |
|------|------|------|
| id | INTEGER | 主键 |
| assistant_name / description | TEXT | 名称 / 描述 |
| system_prompt | TEXT | 系统提示词（人设与任务） |
| input_params / output_params | TEXT | 入参/出参定义 JSON（含数据类型/是否必填） |
| quick_prompts | TEXT | 辅助提问词 JSON（按钮列表） |
| icon | TEXT | 图标（emoji/上传/模型生成） |
| enabled | INTEGER | 启用后自动加入模型助手菜单 |

#### t_ai_conversation（对话会话表）

| 字段 | 类型 | 说明 |
|------|------|------|
| assistant_id / assistant_name | INTEGER/TEXT | 关联助手 |
| messages | TEXT | 多轮消息 JSON（气泡历史） |
| outputs | TEXT | 最终输出 JSON（按输出参数生成） |
| provider_id / model | INTEGER/TEXT | 对话所用供应商/模型（支持切换） |
| status | INTEGER | 0=进行中 1=已结束 |

#### t_skill（技能表）

| 字段 | 类型 | 说明 |
|------|------|------|
| skill_name / group_name | TEXT | 名称 / 分组 |
| description / content | TEXT | 描述 / 技能内容（拼入系统提示词） |
| enabled | INTEGER | 启停 |
| market_skill_id | INTEGER | 市场导入来源标记 |

#### t_kb / t_kb_document / t_kb_chunk / t_kb_match_log（知识库 4 表）

| 表 | 关键字段 | 说明 |
|------|------|------|
| t_kb | chunk_size / chunk_overlap / retrieve_type(text\|vector) / vector_model / chunk_count | 库配置 |
| t_kb_document | kb_id / doc_name / doc_type / content / status | 文档（上传解析后原文） |
| t_kb_chunk | kb_id / doc_id / content / seq_no / vector_json | 切片（向量 JSON 数组） |
| t_kb_match_log | kb_id / query / results_json / score | 检索匹配记录 |

#### t_redis_config（Redis 多实例配置表）

| 字段 | 类型 | 说明 |
|------|------|------|
| config_name | TEXT | 实例名称 |
| host / port / password / db | TEXT | 连接参数 |
| is_default | INTEGER | 默认实例（全局唯一，0 号=默认） |

#### t_market_item（市场条目表）

| 字段 | 类型 | 说明 |
|------|------|------|
| item_type | TEXT | api / flow / assistant / skill / report |
| item_name / description / group_name | TEXT | 名称 / 描述 / 分组 |
| content_json | TEXT | 内容快照（按类型结构不同） |
| download_count | INTEGER | 导入次数 |
| market_item_id | INTEGER | 官方市场条目 ID（发现导入；本地发布为 null） |
| icon / author / version | TEXT | 图标 / 作者 / 版本号（7.5 元信息） |
| enabled | INTEGER | 上架/下架 |

#### t_market_favorite（市场收藏表）

| 字段 | 类型 | 说明 |
|------|------|------|
| market_item_id | INTEGER | 收藏的市场条目 ID |
| tenant_id | INTEGER | 收藏租户（严格隔离） |

### 12.2 新增后端 API

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | /api/market/discover | 发现官方市场：拉取 GitHub index.json + 条目文件到本地 market 目录 |
| GET | /api/market/local-files | 本地 market 目录条目列表（含是否已导入） |
| POST | /api/market/import-file | 按 (type, marketId) 导入本地文件（id 相同更新否则新增） |
| POST | /api/market/favorite/{id} / unfavorite/{id} | 收藏 / 取消收藏（幂等） |
| GET | /api/market/list?favorite=1 | 列表（含元信息 + favorited 标记 + 收藏过滤） |
| POST | /api/market/generate-share-file | 生成分享文件：本地写 market/{type}/{id}.json + 更新 index.json（同名复用 id） |
| GET | /api/ai/providers · /api/ai/provider/save · /api/ai/fetch-models | 供应商 CRUD + 模型拉取/配置测试（capabilities 能力绑定） |
| GET/POST | /api/skill/list · save · delete · toggle · import · batch-import · export · batch-export | Skill 管理 |
| GET | /api/redis-config/configs · config · save · delete · config/test | Redis 多实例 |
| GET/POST | /api/knowledge/* | 知识库（kb/document/chunk/search/clean/revectorize/match-logs） |

### 12.3 AI 节点函数调用流程（工具调用）

```
1. 执行器解析 aiConfig：toolApis（接口 methodCode 列表）+ toolFlows（流程 flowKey 列表）
2. toolsResolver 回调 → 加载接口/流程定义（含参数说明）→ OpenAI tools JSON
3. _chat(new AiChatRequest(..., ToolsJson, History)) → AiChatResult{Text, ToolCalls}
4. ToolCalls 非空（且 <4 轮）：
   a. history 追加 assistant 消息（tool_calls）
   b. toolRunner(name, args) 逐个执行：
      - api:xxx → 按 RequestType 拼 query/body 直接 HTTP 调用（MockJson 优先），响应截断 8000 字符
      - flow:xxx → flowContentLoader 加载 → 递归 BuildEngineAsync → ExecuteAsync(args) → 出参 JSON
   c. history 追加 tool 消息（tool_call_id + 结果）
   d. 回到步骤 3 携带 History 继续
5. 取最终文本（无文本时回退最近非空文本）写入输出目标
```

### 12.4 市场分享（GitHub PR）流程

```
1. 前端校验作者 → POST /api/market/generate-share-file：
   后端本地落盘 market/{type}/{id}.json（同名复用 id）+ upsert market/index.json
2. 前端 GitHub API（api.github.com，Token 存 localStorage）：
   GET /user 校验 → GET/POST forks 确保 fork → POST git/refs 建分支
   → PUT contents/market/{type}/{id}.json（条目文件）
   → GET raw index.json（官方最新）合并本条索引 → PUT contents/market/index.json
   → POST pulls 创建 PR（head=用户名:分支, base=默认分支）→ 打开 PR 页面
3. 管理员审核合并 → 进入官方 market 目录 → 其他用户「发现」拉取
```

