using System.Text.Json;

namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>
/// METHOD 节点执行器：根据 FillRule 填充参数，发起 HTTP 请求，将结果写回变量
/// </summary>
public class MethodNodeExecutor : INodeExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;

    public MethodNodeExecutor(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        if (node.Method == null)
            throw new InvalidOperationException($"METHOD node [{node.Key}] has no method config.");

        var method = node.Method;

        // 1. 根据 inputFillRules 构建入参
        var inputParams = new Dictionary<string, object?>();
        var headerParams = new Dictionary<string, string>();

        foreach (var rule in method.HeaderFillRules)
        {
            var val = ResolveSource(rule, context);
            headerParams[rule.Target] = val?.ToString() ?? "";
        }

        foreach (var rule in method.InputFillRules)
        {
            var val = ResolveSource(rule, context);
            SetNestedTarget(inputParams, rule.Target, val);
        }

        // 2. 发起 HTTP 或 WebService 请求（Mock 模式直接返回预设数据）
        string responseJson;
        try
        {
            var methodType = method.MethodType?.ToUpper() ?? "HTTP";

            // Mock 模式（优先级最高）：节点级 > 接口级 > 真实调用
            if (!string.IsNullOrEmpty(node.MockJson))
            {
                responseJson = node.MockJson;
            }
            else if (!string.IsNullOrEmpty(method.MockJson))
            {
                responseJson = method.MockJson;
            }
            else if (methodType == "WEBSERVICE")
            {
                responseJson = await CallWebServiceAsync(method, inputParams, headerParams);
            }
            else
            {
                responseJson = await CallHttpAsync(method, inputParams, headerParams);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"METHOD node [{node.Key}] HTTP call failed: {ex.Message}", ex);
        }

        // 3. 解析响应，根据 outputFillRules 写回变量
        try
        {
            var responseDoc = JsonSerializer.Deserialize<JsonElement>(responseJson);
            foreach (var rule in method.OutputFillRules)
            {
                var targetType = (rule.TargetType ?? "VARIABLE").ToUpper();
                var value = ExtractJsonValue(responseDoc, rule.Source);

                switch (targetType)
                {
                    case "OUTPUT":
                        context.SetOutputParameter(rule.Target, value);
                        break;
                    case "SUB_PROPERTY":
                        context.SetNestedProperty(rule.Target, rule.TargetPath, value);
                        break;
                    default:
                        // VARIABLE / INPUT 等
                        context.SetVariable(rule.Target, value);
                        break;
                }
            }
        }
        catch
        {
            // 响应不是JSON，忽略 outputFillRules
        }

        return node.Outgoings.FirstOrDefault();
    }

    private static object? ResolveSource(FillRule rule, FlowContext context)
    {
        var srcType = (rule.SourceType ?? "VARIABLE").ToUpper();
        switch (srcType)
        {
            case "CONSTANT":
                return rule.Source;
            case "STATIC":
                return context.GetStaticVariable(rule.Source);
            case "SUB_PROPERTY":
                return context.GetNestedProperty(rule.Source, rule.SourcePath);
            default:
                // VARIABLE / INPUT 等
                return context.GetVariable(rule.Source);
        }
    }

    private async Task<string> CallHttpAsync(
        MethodConfig method, Dictionary<string, object?> inputParams, Dictionary<string, string> headerParams)
    {
        var client = _httpClientFactory.CreateClient();
        var requestType = method.RequestType?.ToUpper() ?? "GET";
        var url = method.Url;

        foreach (var h in headerParams)
            client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

        if (requestType == "GET" || requestType == "DELETE")
        {
            if (inputParams.Count > 0)
            {
                var query = string.Join("&", inputParams.Select(kv =>
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value?.ToString() ?? "")}"));
                url = url.Contains('?') ? $"{url}&{query}" : $"{url}?{query}";
            }
            var resp = requestType == "GET" ? await client.GetAsync(url) : await client.DeleteAsync(url);
            return await resp.Content.ReadAsStringAsync();
        }
        else
        {
            var content = new StringContent(
                JsonSerializer.Serialize(inputParams),
                System.Text.Encoding.UTF8, "application/json");
            var resp = requestType == "PUT"
                ? await client.PutAsync(url, content)
                : await client.PostAsync(url, content);
            return await resp.Content.ReadAsStringAsync();
        }
    }

    private async Task<string> CallWebServiceAsync(
        MethodConfig method, Dictionary<string, object?> inputParams, Dictionary<string, string> headerParams)
    {
        var uri = new Uri(method.Url);

        // 从查询参数 ?op=MethodName 中提取操作名（手动解析，避免额外依赖）
        var methodName = "Request";
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "op" && !string.IsNullOrEmpty(kv[1]))
            {
                methodName = Uri.UnescapeDataString(kv[1]);
                break;
            }
        }

        // 构建参数 XML
        var paramXml = string.Join("", inputParams.Select(kv =>
            $"<{kv.Key}>{System.Security.SecurityElement.Escape(kv.Value?.ToString() ?? "")}</{kv.Key}>"));

        var ns = uri.GetLeftPart(UriPartial.Path);
        var soapBody = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <{methodName} xmlns=""{ns}"">
      {paramXml}
    </{methodName}>
  </soap:Body>
</soap:Envelope>";

        var client = _httpClientFactory.CreateClient();
        var content = new StringContent(soapBody, System.Text.Encoding.UTF8, "text/xml");

        if (headerParams.TryGetValue("SOAPAction", out var soapAction))
            content.Headers.Add("SOAPAction", $"\"{soapAction}\"");

        foreach (var h in headerParams)
            if (h.Key != "SOAPAction")
                client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

        var resp = await client.PostAsync(method.Url, content);
        return await resp.Content.ReadAsStringAsync();
    }

    private static object? ExtractJsonValue(JsonElement doc, string path)
    {
        // 支持简单 path，如 "data.token" 或 "loginFlag"
        var parts = path.Split('.');
        JsonElement current = doc;
        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
                current = next;
            else
                return null;
        }
        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.TryGetInt64(out var l) ? l : current.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => current.ToString()
        };
    }

    /// <summary>按点号分隔的路径设置嵌套字典值，如 "user.name" → dict["user"]["name"]</summary>
    private static void SetNestedTarget(Dictionary<string, object?> dict, string path, object? value)
    {
        var parts = path.Split('.');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!dict.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> nested)
            {
                nested = new Dictionary<string, object?>();
                dict[parts[i]] = nested;
            }
            dict = nested;
        }
        dict[parts[^1]] = value;
    }
}
