namespace Juggle.Domain.Entities;

/// <summary>知识库</summary>
public class KnowledgeBaseEntity : BaseEntity
{
    /// <summary>知识库名称</summary>
    public string? KbName { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>切片大小（字符数，默认 500）</summary>
    public int ChunkSize { get; set; } = 500;

    /// <summary>切片重叠（字符数，默认 50）</summary>
    public int ChunkOverlap { get; set; } = 50;

    /// <summary>检索方式：text=文本关键词 / vector=向量相似度</summary>
    public string? RetrieveType { get; set; } = "text";

    /// <summary>向量模型（vector 模式使用，如 text-embedding-ada-002 / bge-large-zh）</summary>
    public string? VectorModel { get; set; }

    /// <summary>启用：1=启用 0=禁用</summary>
    public int Enabled { get; set; } = 1;

    /// <summary>片段总数（冗余计数）</summary>
    public int ChunkCount { get; set; }
}

/// <summary>知识库文档（上传解析后的原始文本）</summary>
public class KnowledgeDocumentEntity : BaseEntity
{
    public long KbId { get; set; }

    /// <summary>文档名称</summary>
    public string? DocName { get; set; }

    /// <summary>文档类型：txt/markdown/docx/xlsx/pdf/manual</summary>
    public string? DocType { get; set; }

    /// <summary>解析后的原始文本</summary>
    public string? Content { get; set; }

    /// <summary>状态：0=待切片 1=已切片</summary>
    public int Status { get; set; }
}

/// <summary>知识库片段（切片结果）</summary>
public class KnowledgeChunkEntity : BaseEntity
{
    public long KbId { get; set; }
    public long DocId { get; set; }

    /// <summary>片段内容</summary>
    public string? Content { get; set; }

    /// <summary>片段序号</summary>
    public int SeqNo { get; set; }

    /// <summary>向量（JSON 数组，vector 模式使用）</summary>
    public string? VectorJson { get; set; }
}

/// <summary>知识库匹配记录</summary>
public class KnowledgeMatchLogEntity : BaseEntity
{
    public long KbId { get; set; }

    /// <summary>查询内容</summary>
    public string? Query { get; set; }

    /// <summary>匹配结果（JSON）</summary>
    public string? ResultsJson { get; set; }

    /// <summary>最高匹配分</summary>
    public double Score { get; set; }
}
