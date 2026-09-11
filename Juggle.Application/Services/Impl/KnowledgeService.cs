using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace Juggle.Application.Services.Impl;

/// <summary>
/// 知识库服务：文档解析（txt/markdown/docx/xlsx/pdf）→ 切片 → 文本/向量检索 → 清洗 → 匹配记录。
/// 向量检索通过大模型供应商的 OpenAI 兼容 /embeddings 接口生成向量，本地计算余弦相似度。
/// </summary>
public class KnowledgeService
{
    private readonly JuggleDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiService _aiService;

    public KnowledgeService(JuggleDbContext db, IHttpClientFactory httpClientFactory, AiService aiService)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _aiService = aiService;
    }

    // ==================== 文档解析 ====================

    /// <summary>按文件名与内容解析文档为纯文本。</summary>
    public string ParseDocument(string fileName, byte[] bytes)
    {
        var ext = Path.GetExtension(fileName).ToLower();
        return ext switch
        {
            ".txt" or ".md" or ".markdown" => Encoding.UTF8.GetString(bytes),
            ".docx" => ParseDocx(bytes),
            ".xlsx" => ParseXlsx(bytes),
            ".pdf" => ParsePdf(bytes),
            _ => throw new Exception($"不支持的文档格式: {ext}（支持 txt / markdown / docx / xlsx / pdf）")
        };
    }

    private static string ParseDocx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(ms, false);
        return doc.MainDocumentPart?.Document.Body?.InnerText ?? "";
    }

    private static string ParseXlsx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var sb = new StringBuilder();
        foreach (var ws in wb.Worksheets)
        {
            foreach (var row in ws.RowsUsed())
            {
                foreach (var cell in row.CellsUsed())
                {
                    var v = cell.GetString().Trim();
                    if (v.Length > 0) sb.Append(v).Append(' ');
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string ParsePdf(byte[] bytes)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(bytes);
        foreach (var page in pdf.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    // ==================== 切片 ====================

    /// <summary>固定大小 + 重叠切片。</summary>
    public static List<string> SplitChunks(string text, int chunkSize, int chunkOverlap)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;
        var normalized = text.Replace("\r\n", "\n");
        var step = Math.Max(1, chunkSize - chunkOverlap);
        for (int i = 0; i < normalized.Length; i += step)
        {
            var chunk = normalized.Substring(i, Math.Min(chunkSize, normalized.Length - i)).Trim();
            if (chunk.Length > 0) chunks.Add(chunk);
            if (i + chunkSize >= normalized.Length) break;
        }
        return chunks;
    }

    /// <summary>文档入库并切片（vector 模式自动向量化）。</summary>
    public async Task<object> AddDocumentAsync(long kbId, string fileName, string docType, string content)
    {
        var kb = await GetKbAsync(kbId);
        var doc = new KnowledgeDocumentEntity
        {
            KbId = kbId, DocName = fileName, DocType = docType, Content = content, Status = 1,
            CreatedAt = DateTime.Now.ToString("o")
        };
        _db.KnowledgeDocuments.Add(doc);
        await _db.SaveChangesAsync();

        var chunks = SplitChunks(content, kb.ChunkSize, kb.ChunkOverlap);
        await SaveChunksAsync(kb, doc.Id, chunks);
        return new { doc.Id, doc.DocName, chunkCount = chunks.Count };
    }

    /// <summary>手动增加片段。</summary>
    public async Task<object> AddChunkAsync(long kbId, string content)
    {
        var kb = await GetKbAsync(kbId);
        var doc = new KnowledgeDocumentEntity
        {
            KbId = kbId, DocName = $"手动片段 {DateTime.Now:MM-dd HH:mm}", DocType = "manual", Content = content, Status = 1,
            CreatedAt = DateTime.Now.ToString("o")
        };
        _db.KnowledgeDocuments.Add(doc);
        await _db.SaveChangesAsync();
        var chunks = SplitChunks(content, kb.ChunkSize, kb.ChunkOverlap);
        await SaveChunksAsync(kb, doc.Id, chunks);
        return new { doc.Id, chunkCount = chunks.Count };
    }

    private async Task SaveChunksAsync(KnowledgeBaseEntity kb, long docId, List<string> chunks)
    {
        foreach (var (chunk, i) in chunks.Select((c, i) => (c, i)))
        {
            var entity = new KnowledgeChunkEntity
            {
                KbId = kb.Id, DocId = docId, Content = chunk, SeqNo = i, CreatedAt = DateTime.Now.ToString("o")
            };
            if (kb.RetrieveType == "vector")
            {
                var vector = await EmbedAsync(kb, chunk);
                if (vector != null) entity.VectorJson = JsonSerializer.Serialize(vector);
            }
            _db.KnowledgeChunks.Add(entity);
        }
        kb.ChunkCount = await _db.KnowledgeChunks.CountAsync(c => c.KbId == kb.Id && c.Deleted == 0);
        await _db.SaveChangesAsync();
    }

    // ==================== 检索 ====================

    /// <summary>检索：text=关键词评分；vector=embedding 余弦相似度。返回 topK 片段。</summary>
    public async Task<List<Dictionary<string, object?>>> SearchAsync(long kbId, string query, int topK = 5)
    {
        var kb = await GetKbAsync(kbId);
        var chunks = (await _db.KnowledgeChunks
            .Where(c => c.KbId == kbId && c.Deleted == 0)
            .Select(c => new { c.Id, c.Content, c.VectorJson })
            .ToListAsync())
            .Select(c => (c.Id, c.Content, c.VectorJson))
            .ToList();

        List<(long Id, string Content, double Score)> scored = kb.RetrieveType == "vector"
            ? await VectorSearchAsync(kb, chunks, query)
            : TextSearch(chunks, query);

        var top = scored.OrderByDescending(s => s.Score).Take(topK).ToList();

        // 匹配记录
        _db.KnowledgeMatchLogs.Add(new KnowledgeMatchLogEntity
        {
            KbId = kbId,
            Query = query,
            ResultsJson = JsonSerializer.Serialize(top.Select(t => new { t.Id, t.Content, t.Score })),
            Score = top.FirstOrDefault().Score,
            CreatedAt = DateTime.Now.ToString("o")
        });
        await _db.SaveChangesAsync();

        return top.Select(t => new Dictionary<string, object?>
        {
            ["chunkId"] = t.Id,
            ["content"] = t.Content,
            ["score"] = Math.Round(t.Score, 4)
        }).ToList();
    }

    /// <summary>检索并拼接为上下文文本（KB_SEARCH 节点用）。</summary>
    public async Task<string> SearchAsContextAsync(long kbId, string query, int topK = 5)
    {
        var results = await SearchAsync(kbId, query, topK);
        var sb = new StringBuilder();
        foreach (var r in results)
            sb.AppendLine(r["content"]?.ToString() ?? "");
        return sb.ToString().Trim();
    }

    private static List<(long, string, double)> TextSearch(List<(long Id, string? Content, string? VectorJson)> chunks, string query)
    {
        // 中文按 2 字 + 英文按词 简单切分关键词
        var keywords = Tokenize(query);
        var scored = new List<(long, string, double)>();
        foreach (var c in chunks)
        {
            if (string.IsNullOrEmpty(c.Content)) continue;
            var content = c.Content;
            double score = 0;
            foreach (var kw in keywords)
            {
                var count = 0;
                var idx = 0;
                while ((idx = content.IndexOf(kw, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    count++;
                    idx += kw.Length;
                    if (count > 10) break;
                }
                if (count > 0) score += 1.0 + Math.Log(count);
            }
            if (score > 0) scored.Add((c.Id, content, score));
        }
        return scored;
    }

    private static List<string> Tokenize(string query)
    {
        var tokens = new HashSet<string>();
        // 英文/数字词
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(query, @"[a-zA-Z0-9_]+"))
        {
            var w = m.Value;
            if (w.Length >= 2) tokens.Add(w);
        }
        // 中文二元组
        var zh = System.Text.RegularExpressions.Regex.Replace(query, @"[^一-龥]", "");
        for (int i = 0; i + 1 < zh.Length; i++)
            tokens.Add(zh.Substring(i, 2));
        if (tokens.Count == 0) tokens.Add(query.Trim());
        return tokens.ToList();
    }

    private async Task<List<(long, string, double)>> VectorSearchAsync(KnowledgeBaseEntity kb,
        List<(long Id, string? Content, string? VectorJson)> chunks, string query)
    {
        var qVec = await EmbedAsync(kb, query);
        if (qVec == null) return TextSearch(chunks, query);   // 向量化失败降级文本检索

        var scored = new List<(long, string, double)>();
        foreach (var c in chunks)
        {
            if (string.IsNullOrEmpty(c.VectorJson) || string.IsNullOrEmpty(c.Content)) continue;
            try
            {
                var vec = JsonSerializer.Deserialize<List<float>>(c.VectorJson);
                if (vec == null || vec.Count != qVec.Count) continue;
                scored.Add((c.Id, c.Content, Cosine(qVec, vec)));
            }
            catch { /* 跳过损坏向量 */ }
        }
        return scored;
    }

    /// <summary>调大模型供应商 embeddings 接口生成向量。</summary>
    private async Task<List<float>?> EmbedAsync(KnowledgeBaseEntity kb, string text)
    {
        try
        {
            var providers = await _aiService.GetEnabledProvidersAsync();
            var provider = providers.FirstOrDefault();
            if (provider == null) return null;
            var baseUrl = (provider.BaseUrl ?? "").TrimEnd('/');
            if (!baseUrl.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase))
                baseUrl += "/embeddings";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            using var msg = new HttpRequestMessage(HttpMethod.Post, baseUrl);
            msg.Headers.TryAddWithoutValidation("Authorization", $"Bearer {provider.ApiKey}");
            msg.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = string.IsNullOrWhiteSpace(kb.VectorModel) ? provider.Model : kb.VectorModel,
                input = text.Length > 2000 ? text[..2000] : text
            }), Encoding.UTF8, "application/json");
            using var resp = await client.SendAsync(msg);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
            return data.EnumerateArray().Select(e => (float)e.GetSingle()).ToList();
        }
        catch { return null; }
    }

    private static double Cosine(List<float> a, List<float> b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na > 0 && nb > 0 ? dot / (Math.Sqrt(na) * Math.Sqrt(nb)) : 0;
    }

    // ==================== 清洗 / 向量化 ====================

    /// <summary>清洗：删除空片段、超短片段、完全重复片段。</summary>
    public async Task<object> CleanAsync(long kbId)
    {
        var kb = await GetKbAsync(kbId);
        var chunks = await _db.KnowledgeChunks.Where(c => c.KbId == kbId && c.Deleted == 0).ToListAsync();
        var seen = new HashSet<string>();
        var removed = 0;
        foreach (var c in chunks)
        {
            var key = (c.Content ?? "").Trim();
            if (key.Length < 5 || !seen.Add(key))
            {
                c.Deleted = 1;
                removed++;
            }
        }
        kb.ChunkCount = await _db.KnowledgeChunks.CountAsync(c => c.KbId == kbId && c.Deleted == 0);
        await _db.SaveChangesAsync();
        return new { removed, remaining = kb.ChunkCount };
    }

    /// <summary>重新向量化全部片段（vector 模式）。</summary>
    public async Task<object> RevectorizeAsync(long kbId)
    {
        var kb = await GetKbAsync(kbId);
        if (kb.RetrieveType != "vector") throw new Exception("当前知识库为文本检索模式，请先在知识库配置中切换为向量检索");
        var chunks = await _db.KnowledgeChunks.Where(c => c.KbId == kbId && c.Deleted == 0).ToListAsync();
        var done = 0;
        foreach (var c in chunks)
        {
            var vector = await EmbedAsync(kb, c.Content ?? "");
            if (vector != null)
            {
                c.VectorJson = JsonSerializer.Serialize(vector);
                done++;
            }
        }
        await _db.SaveChangesAsync();
        return new { done, total = chunks.Count };
    }

    private async Task<KnowledgeBaseEntity> GetKbAsync(long kbId)
    {
        return await _db.KnowledgeBases.FirstOrDefaultAsync(k => k.Id == kbId && k.Deleted == 0)
            ?? throw new Exception("知识库不存在");
    }
}
