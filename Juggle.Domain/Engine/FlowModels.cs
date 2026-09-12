using System.Text.Json.Serialization;

namespace Juggle.Domain.Engine;

/// <summary>流程节点定义</summary>
public class FlowNode
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("elementType")]
    public string ElementType { get; set; } = "";  // START END METHOD CONDITION ASSIGN CODE MYSQL(DB) MERGE SUB_FLOW LOOP DELAY(WAIT)

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("incomings")]
    public List<string> Incomings { get; set; } = new();

    [JsonPropertyName("outgoings")]
    public List<string> Outgoings { get; set; } = new();

    [JsonPropertyName("method")]
    public MethodConfig? Method { get; set; }

    [JsonPropertyName("conditions")]
    public List<ConditionConfig>? Conditions { get; set; }

    [JsonPropertyName("assignRules")]
    public List<AssignRule>? AssignRules { get; set; }

    [JsonPropertyName("codeConfig")]
    public CodeConfig? CodeConfig { get; set; }

    [JsonPropertyName("mysqlConfig")]
    public MysqlConfig? MysqlConfig { get; set; }

    [JsonPropertyName("aiConfig")]
    public AiConfig? AiConfig { get; set; }

    [JsonPropertyName("fileParseConfig")]
    public FileParseConfig? FileParseConfig { get; set; }

    [JsonPropertyName("excelReadConfig")]
    public ExcelReadConfig? ExcelReadConfig { get; set; }

    [JsonPropertyName("fileWriteConfig")]
    public FileWriteConfig? FileWriteConfig { get; set; }

    [JsonPropertyName("redisGetConfig")]
    public RedisGetConfig? RedisGetConfig { get; set; }

    [JsonPropertyName("kbSearchConfig")]
    public KbSearchConfig? KbSearchConfig { get; set; }

    [JsonPropertyName("dataExtractConfig")]
    public DataExtractConfig? DataExtractConfig { get; set; }

    [JsonPropertyName("redisSetConfig")]
    public RedisSetConfig? RedisSetConfig { get; set; }

    [JsonPropertyName("subFlowConfig")]
    public SubFlowConfig? SubFlowConfig { get; set; }

    /// <summary>节点执行超时时间（毫秒），0或不设置表示不限制</summary>
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 0;

    /// <summary>失败重试次数，0或不设置表示不重试</summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    /// <summary>重试间隔（毫秒），默认1000ms</summary>
    [JsonPropertyName("retryInterval")]
    public int RetryInterval { get; set; } = 1000;

    /// <summary>Mock 模式：设置后不发起真实请求，直接返回此 JSON 作为响应</summary>
    [JsonPropertyName("mockJson")]
    public string? MockJson { get; set; }

    /// <summary>循环节点配置（LOOP 节点）</summary>
    [JsonPropertyName("loopConfig")]
    public LoopConfig? LoopConfig { get; set; }

    /// <summary>延迟节点配置（DELAY/WAIT 节点）</summary>
    [JsonPropertyName("delayConfig")]
    public DelayConfig? DelayConfig { get; set; }

    /// <summary>并行节点配置（PARALLEL 节点）</summary>
    [JsonPropertyName("parallelConfig")]
    public ParallelConfig? ParallelConfig { get; set; }

    /// <summary>通知节点配置（NOTIFY 节点）</summary>
    [JsonPropertyName("notifyConfig")]
    public NotifyConfig? NotifyConfig { get; set; }

    /// <summary>模板转换节点配置（TRANSFORM 节点）</summary>
    [JsonPropertyName("transformConfig")]
    public TransformConfig? TransformConfig { get; set; }
}

/// <summary>方法节点配置</summary>
public class MethodConfig
{
    [JsonPropertyName("suiteCode")]
    public string SuiteCode { get; set; } = "";

    [JsonPropertyName("methodCode")]
    public string MethodCode { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("requestType")]
    public string RequestType { get; set; } = "GET";

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "JSON";

    /// <summary>HTTP WEBSERVICE</summary>
    [JsonPropertyName("methodType")]
    public string MethodType { get; set; } = "HTTP";

    [JsonPropertyName("inputFillRules")]
    public List<FillRule> InputFillRules { get; set; } = new();

    [JsonPropertyName("outputFillRules")]
    public List<FillRule> OutputFillRules { get; set; } = new();

    [JsonPropertyName("headerFillRules")]
    public List<FillRule> HeaderFillRules { get; set; } = new();

    /// <summary>Mock 模式返回的预设 JSON 数据，非空则跳过真实调用</summary>
    [JsonPropertyName("mockJson")]
    public string? MockJson { get; set; }
}

/// <summary>条件节点分支配置</summary>
public class ConditionConfig
{
    [JsonPropertyName("conditionName")]
    public string ConditionName { get; set; } = "";

    /// <summary>CUSTOM / DEFAULT</summary>
    [JsonPropertyName("conditionType")]
    public string ConditionType { get; set; } = "CUSTOM";

    /// <summary>条件表达式，如: env_isLogin == true</summary>
    [JsonPropertyName("expression")]
    public string? Expression { get; set; }

    [JsonPropertyName("outgoing")]
    public string? Outgoing { get; set; }
}

/// <summary>数据填充规则</summary>
public class FillRule
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>VARIABLE / CONSTANT / STATIC / INPUT / SUB_PROPERTY</summary>
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "VARIABLE";

    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    /// <summary>INPUT_PARAM / OUTPUT_PARAM / HEADER / VARIABLE / OUTPUT（流程输出参数）/ SUB_PROPERTY</summary>
    [JsonPropertyName("targetType")]
    public string TargetType { get; set; } = "INPUT_PARAM";

    /// <summary>目标子对象属性路径（targetType 为 SUB_PROPERTY 时使用）</summary>
    [JsonPropertyName("targetPath")]
    public string TargetPath { get; set; } = "";
}

/// <summary>赋值规则（ASSIGN 节点）</summary>
public class AssignRule
{
    /// <summary>来源：变量名或常量值</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>VARIABLE / CONSTANT / STATIC（全局静态变量）/ INPUT / SUB_PROPERTY / ARRAY_OPERATION / EXPRESSION</summary>
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "VARIABLE";

    /// <summary>子对象属性路径（sourceType/targetType 为 SUB_PROPERTY 时使用），如 "name" 或 "user.id"</summary>
    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = "";

    /// <summary>目标变量名</summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    /// <summary>目标类型：VARIABLE（流程变量）/ STATIC（全局静态变量）/ OUTPUT / INPUT / SUB_PROPERTY</summary>
    [JsonPropertyName("targetType")]
    public string TargetType { get; set; } = "VARIABLE";

    /// <summary>目标子对象属性路径（targetType 为 SUB_PROPERTY 时使用），如 "name" 或 "user.id"</summary>
    [JsonPropertyName("targetPath")]
    public string TargetPath { get; set; } = "";

    /// <summary>数据类型（用于常量解析）：string/integer/double/boolean</summary>
    [JsonPropertyName("dataType")]
    public string? DataType { get; set; }

    /// <summary>数组操作类型（sourceType/targetType 为 ARRAY_OPERATION 时使用）</summary>
    [JsonPropertyName("arrayOpType")]
    public string? ArrayOpType { get; set; }

    /// <summary>数组操作-页码（分页操作）</summary>
    [JsonPropertyName("arrayOpPageNum")]
    public string? ArrayOpPageNum { get; set; }

    /// <summary>数组操作-每页数量（分页操作）</summary>
    [JsonPropertyName("arrayOpPageSize")]
    public string? ArrayOpPageSize { get; set; }

    /// <summary>数组操作-索引（获取第几个/插入）</summary>
    [JsonPropertyName("arrayOpIndex")]
    public string? ArrayOpIndex { get; set; }
}

/// <summary>代码节点配置（CODE 节点）</summary>
public class CodeConfig
{
    /// <summary>脚本语言：javascript / groovy（当前实现支持简化 JS 语法）</summary>
    [JsonPropertyName("scriptType")]
    public string ScriptType { get; set; } = "javascript";

    /// <summary>脚本内容</summary>
    [JsonPropertyName("script")]
    public string Script { get; set; } = "";
}

/// <summary>子流程节点配置（SUB_FLOW 节点）</summary>
public class SubFlowConfig
{
    /// <summary>被调用的子流程 Key</summary>
    [JsonPropertyName("subFlowKey")]
    public string SubFlowKey { get; set; } = "";

    /// <summary>入参映射：从当前流程变量填充子流程入参</summary>
    [JsonPropertyName("inputMappings")]
    public List<SubFlowMapping> InputMappings { get; set; } = new();

    /// <summary>出参映射：将子流程输出变量写回当前流程变量</summary>
    [JsonPropertyName("outputMappings")]
    public List<SubFlowMapping> OutputMappings { get; set; } = new();
}

/// <summary>子流程变量映射规则</summary>
public class SubFlowMapping
{
    /// <summary>来源名称（当前流程变量名 或 子流程输出变量名）</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>VARIABLE / CONSTANT</summary>
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "VARIABLE";

    /// <summary>目标名称（子流程入参名 或 当前流程变量名）</summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = "";
}

/// <summary>AI 大模型节点配置（AI 节点）</summary>
public class AiConfig
{
    /// <summary>输入变量名（作为用户消息发送）</summary>
    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    /// <summary>系统提示词（人设与任务说明）</summary>
    [JsonPropertyName("systemPrompt")]
    public string SystemPrompt { get; set; } = "";

    /// <summary>输出目标名（模型回复写入）</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = "";

    /// <summary>输出目标类型：VARIABLE（中间变量）/ OUTPUT（出参）/ INPUT（入参）</summary>
    [JsonPropertyName("outputTargetType")]
    public string OutputTargetType { get; set; } = "VARIABLE";

    /// <summary>图片输入变量（逗号分隔多个，data URL / http 地址，供视觉模型识别）</summary>
    [JsonPropertyName("inputImages")]
    public string InputImages { get; set; } = "";

    /// <summary>供应商 ID（0=第一个启用供应商）</summary>
    [JsonPropertyName("providerId")]
    public long ProviderId { get; set; }

    /// <summary>模型名（空=供应商默认模型）</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    /// <summary>技能 ID 列表（逗号分隔，执行时技能内容拼入系统提示词）</summary>
    [JsonPropertyName("skills")]
    public string Skills { get; set; } = "";

    /// <summary>是否启用深度思考（发送 enable_thinking 参数，Qwen/DeepSeek 等推理模型生效）</summary>
    [JsonPropertyName("enableThinking")]
    public bool EnableThinking { get; set; }

    /// <summary>温度（0-2，越大越发散；空=服务默认）</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>最大输出字数（max_tokens，0/空=不限制）</summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    /// <summary>随机种子（seed，固定后输出更稳定；空=随机）</summary>
    [JsonPropertyName("seed")]
    public int? Seed { get; set; }
}

/// <summary>文件解析节点配置（FILE_PARSE 节点）</summary>
public class FileParseConfig
{
    /// <summary>输入变量（文件内容：data URL / base64 / 原文）</summary>
    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    /// <summary>文件类型：auto / text / json / xml / csv</summary>
    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = "auto";

    /// <summary>输出变量（解析结果：JSON 为对象/数组、CSV 为行数组、文本为字符串）</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = "";
}

/// <summary>Excel 读取节点配置（EXCEL_READ 节点）</summary>
public class ExcelReadConfig
{
    /// <summary>输入变量（Excel 文件内容：base64 / data URL）</summary>
    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    /// <summary>工作表名（空=第一个工作表）</summary>
    [JsonPropertyName("sheetName")]
    public string SheetName { get; set; } = "";

    /// <summary>输出变量（行字典数组，首行为表头）</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = "";
}

/// <summary>文件写入节点配置（FILE_WRITE 节点）</summary>
public class FileWriteConfig
{
    /// <summary>内容变量（对象自动序列化为 JSON）</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>文件名（可选，同样写入 data URL）</summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    /// <summary>文件类型：text / json / csv</summary>
    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = "text";

    /// <summary>输出变量（data URL，可下载或供后续节点使用）</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = "";
}

/// <summary>Redis 缓存查询节点配置（REDIS_GET 节点）</summary>
public class RedisGetConfig
{
    /// <summary>Redis 实例 ID（0=默认实例，系统设置 → Redis 配置中维护）</summary>
    [JsonPropertyName("redisId")]
    public long RedisId { get; set; }

    /// <summary>key（支持 ${变量} 模板）</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>输出变量（JSON 字符串自动解析为对象，无值时为 null）</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = "";
}

/// <summary>Redis 缓存设置节点配置（REDIS_SET 节点）</summary>
public class RedisSetConfig
{
    /// <summary>Redis 实例 ID（0=默认实例，系统设置 → Redis 配置中维护）</summary>
    [JsonPropertyName("redisId")]
    public long RedisId { get; set; }

    /// <summary>key（支持 ${变量} 模板）</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>值变量（对象自动序列化为 JSON 字符串）</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    /// <summary>过期秒数（0=永不过期）</summary>
    [JsonPropertyName("expireSeconds")]
    public int ExpireSeconds { get; set; } = 0;

    /// <summary>输出变量（写入是否成功 true/false）</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = "";
}

/// <summary>知识库检索节点配置（KB_SEARCH 节点）</summary>
public class KbSearchConfig
{
    /// <summary>知识库 ID（知识库管理中创建）</summary>
    [JsonPropertyName("kbId")]
    public long KbId { get; set; }

    /// <summary>查询内容（支持 ${变量} 模板）</summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    /// <summary>返回片段数（默认 5）</summary>
    [JsonPropertyName("topK")]
    public int TopK { get; set; } = 5;

    /// <summary>输出变量（检索片段拼接的上下文文本）</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = "";
}

/// <summary>数据提取节点配置（DATA_EXTRACT 节点）</summary>
public class DataExtractConfig
{
    /// <summary>输入变量（流程入参/中间变量/出参）</summary>
    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    /// <summary>提取类型：json / code / keyword / between / length</summary>
    [JsonPropertyName("extractType")]
    public string ExtractType { get; set; } = "json";

    /// <summary>关键字（keyword 类型）</summary>
    [JsonPropertyName("keyword")]
    public string Keyword { get; set; } = "";

    /// <summary>开始标志（between 类型）</summary>
    [JsonPropertyName("startFlag")]
    public string StartFlag { get; set; } = "";

    /// <summary>结束标志（between 类型，空=到结尾）</summary>
    [JsonPropertyName("endFlag")]
    public string EndFlag { get; set; } = "";

    /// <summary>偏移（length 类型）</summary>
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    /// <summary>长度（length 类型，0=到结尾）</summary>
    [JsonPropertyName("length")]
    public int Length { get; set; }

    /// <summary>输出目标名</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = "";

    /// <summary>输出目标类型：VARIABLE / OUTPUT / INPUT</summary>
    [JsonPropertyName("outputTargetType")]
    public string OutputTargetType { get; set; } = "VARIABLE";
}

/// <summary>MySQL 节点配置（MYSQL 节点）</summary>
public class MysqlConfig
{
    /// <summary>数据源名称（关联系统设置中的数据源）</summary>
    [JsonPropertyName("dataSourceName")]
    public string DataSourceName { get; set; } = "";

    /// <summary>数据源类型：mysql / sqlite</summary>
    [JsonPropertyName("dataSourceType")]
    public string? DataSourceType { get; set; }

    /// <summary>SQL 语句，支持 ${varName} 模板变量</summary>
    [JsonPropertyName("sql")]
    public string Sql { get; set; } = "";

    /// <summary>操作类型：QUERY（查询）/ UPDATE（更改）</summary>
    [JsonPropertyName("operationType")]
    public string OperationType { get; set; } = "QUERY";

    /// <summary>查询结果写入的变量名（QUERY 类型有效）</summary>
    [JsonPropertyName("outputVariable")]
    public string? OutputVariable { get; set; }

    /// <summary>影响行数写入的变量名（UPDATE/INSERT/DELETE 类型有效）</summary>
    [JsonPropertyName("affectedRowsVariable")]
    public string? AffectedRowsVariable { get; set; }

    /// <summary>查询结果目标类型：VARIABLE（变量）/ OUTPUT（输出参数）</summary>
    [JsonPropertyName("outputTargetType")]
    public string? OutputTargetType { get; set; }

    /// <summary>影响行数目标类型：VARIABLE（变量）/ OUTPUT（输出参数）</summary>
    [JsonPropertyName("affectedTargetType")]
    public string? AffectedTargetType { get; set; }
}

/// <summary>循环节点配置（LOOP 节点）</summary>
public class LoopConfig
{
    /// <summary>要遍历的数组变量名（从流程上下文读取）</summary>
    [JsonPropertyName("arrayVariable")]
    public string ArrayVariable { get; set; } = "";

    /// <summary>当前元素写入的变量名，默认 _loop_item</summary>
    [JsonPropertyName("itemVariable")]
    public string ItemVariable { get; set; } = "_loop_item";

    /// <summary>当前索引写入的变量名，默认 _loop_index</summary>
    [JsonPropertyName("indexVariable")]
    public string IndexVariable { get; set; } = "_loop_index";

    /// <summary>数组总长度写入的变量名，默认 _loop_total</summary>
    [JsonPropertyName("totalVariable")]
    public string TotalVariable { get; set; } = "_loop_total";

    /// <summary>遍历结果写入的变量名，默认 _loop_results</summary>
    [JsonPropertyName("outputVariable")]
    public string OutputVariable { get; set; } = "_loop_results";
}

/// <summary>延迟节点配置（DELAY/WAIT 节点）</summary>
public class DelayConfig
{
    /// <summary>延迟时间（毫秒），默认 1000</summary>
    [JsonPropertyName("delayMs")]
    public int DelayMs { get; set; } = 1000;

    /// <summary>是否从变量读取延迟时间</summary>
    [JsonPropertyName("variableMode")]
    public bool VariableMode { get; set; } = false;

    /// <summary>延迟时间变量名（variableMode=true 时生效）</summary>
    [JsonPropertyName("delayVariable")]
    public string? DelayVariable { get; set; }
}

/// <summary>并行节点配置（PARALLEL 节点）</summary>
public class ParallelConfig
{
    /// <summary>等待模式：ALL_WAIT（等所有分支完成）/ ANY_FAST（任一完成即继续）</summary>
    [JsonPropertyName("waitMode")]
    public string WaitMode { get; set; } = "ALL_WAIT";

    /// <summary>并行超时时间（毫秒），0=不限</summary>
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 0;

    /// <summary>分支定义（可选，也可用 outgoings + 画布连线）</summary>
    [JsonPropertyName("branches")]
    public List<ParallelBranch>? Branches { get; set; }
}

/// <summary>并行分支定义</summary>
public class ParallelBranch
{
    /// <summary>分支名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>分支起始节点 Key（对应 outgoings 中的 key）</summary>
    [JsonPropertyName("nodeKey")]
    public string NodeKey { get; set; } = "";
}

/// <summary>通知节点配置（NOTIFY 节点）</summary>
public class NotifyConfig
{
    /// <summary>通知类型：WEBHOOK（HTTP回调）/ EMAIL（邮件）</summary>
    [JsonPropertyName("notifyType")]
    public string NotifyType { get; set; } = "WEBHOOK";

    /// <summary>Webhook URL（WEBHOOK 类型必填）</summary>
    [JsonPropertyName("webhookUrl")]
    public string WebhookUrl { get; set; } = "";

    /// <summary>Webhook 请求方法：POST / GET</summary>
    [JsonPropertyName("webhookMethod")]
    public string WebhookMethod { get; set; } = "POST";

    /// <summary>请求头（JSON格式，如 {"Authorization":"Bearer xxx"}）</summary>
    [JsonPropertyName("webhookHeaders")]
    public string? WebhookHeaders { get; set; }

    /// <summary>通知内容模板，支持 ${varName} 变量替换</summary>
    [JsonPropertyName("bodyTemplate")]
    public string BodyTemplate { get; set; } = "{\"flowKey\":\"${flowKey}\",\"status\":\"success\"}";

    /// <summary>邮件收件人（EMAIL 类型，多个用逗号分隔）</summary>
    [JsonPropertyName("emailTo")]
    public string? EmailTo { get; set; }

    /// <summary>邮件主题（EMAIL 类型）</summary>
    [JsonPropertyName("emailSubject")]
    public string? EmailSubject { get; set; }

    /// <summary>失败时是否中断流程，false=仅记录日志继续执行</summary>
    [JsonPropertyName("failOnError")]
    public bool FailOnError { get; set; } = false;
}

/// <summary>模板转换节点配置（TRANSFORM 节点）</summary>
public class TransformConfig
{
    /// <summary>目标类型：INPUT=入参, OUTPUT=出参, VARIABLE=变量, STATIC=静态变量</summary>
    [JsonPropertyName("targetType")]
    public string TargetType { get; set; } = "VARIABLE";

    /// <summary>目标名称（变量/参数 key）</summary>
    [JsonPropertyName("targetCode")]
    public string TargetCode { get; set; } = "";

    /// <summary>模板文本，支持 ${varName} 和 ${varName.sub.path | transform1 | transform2}</summary>
    [JsonPropertyName("template")]
    public string Template { get; set; } = "";
}
