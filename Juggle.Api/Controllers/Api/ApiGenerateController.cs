using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Juggle.Application.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Juggle.Api.Controllers.Api;

/// <summary>
/// 批量生成接口：从 Swagger/WSDL/CURL 解析出接口定义
/// </summary>
[ApiController]
[Route("api/suite/api/generate")]
[Authorize]
public class ApiGenerateController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ApiGenerateController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>解析 CURL 命令</summary>
    [HttpPost("from-curl")]
    public ApiResult FromCurl([FromBody] GenerateFromCurlRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Curl)) return ApiResult.Fail("CURL 命令不能为空");
        var apis = ParseCurl(req.Curl);
        return ApiResult.Success(apis);
    }

    /// <summary>从 Swagger URL 获取并解析</summary>
    [HttpPost("from-swagger-url")]
    public async Task<ApiResult> FromSwaggerUrl([FromBody] GenerateFromUrlRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Url)) return ApiResult.Fail("URL 不能为空");
        try
        {
            var client = _httpClientFactory.CreateClient();
            var json = await client.GetStringAsync(req.Url);
            var apis = ParseSwaggerJson(json);
            return ApiResult.Success(apis);
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"获取或解析 Swagger 失败: {ex.Message}");
        }
    }

    /// <summary>解析 Swagger JSON</summary>
    [HttpPost("from-swagger-json")]
    public ApiResult FromSwaggerJson([FromBody] GenerateFromJsonRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Json)) return ApiResult.Fail("JSON 不能为空");
        try
        {
            var apis = ParseSwaggerJson(req.Json);
            return ApiResult.Success(apis);
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"解析 Swagger JSON 失败: {ex.Message}");
        }
    }

    /// <summary>从 WSDL URL 获取并解析</summary>
    [HttpPost("from-wsdl-url")]
    public async Task<ApiResult> FromWsdlUrl([FromBody] GenerateFromUrlRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Url)) return ApiResult.Fail("URL 不能为空");
        try
        {
            var client = _httpClientFactory.CreateClient();
            var xml = await client.GetStringAsync(req.Url);
            var apis = ParseWsdl(xml, req.Url);
            return ApiResult.Success(apis);
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"获取或解析 WSDL 失败: {ex.Message}");
        }
    }

    /// <summary>解析 WSDL 内容</summary>
    [HttpPost("from-wsdl-content")]
    public ApiResult FromWsdlContent([FromBody] GenerateFromWsdlRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.WsdlContent)) return ApiResult.Fail("WSDL 内容不能为空");
        try
        {
            var apis = ParseWsdl(req.WsdlContent, req.Url ?? "");
            return ApiResult.Success(apis);
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"解析 WSDL 失败: {ex.Message}");
        }
    }

    // ========== CURL 解析 ==========

    private List<GeneratedApiItem> ParseCurl(string curl)
    {
        var result = new List<GeneratedApiItem>();
        var api = new GeneratedApiItem { MethodName = "", RequestType = "GET", Url = "" };
        var headers = new Dictionary<string, string>();
        var queryParams = new Dictionary<string, string>();
        var bodyParams = new Dictionary<string, string>();

        // 去掉换行和多余空格
        curl = curl.Trim();
        // 去掉开头的 "curl "
        if (curl.StartsWith("curl ", StringComparison.OrdinalIgnoreCase))
            curl = curl[5..];

        // 按标识符拆分
        var tokens = TokenizeCurl(curl);
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t == "-X" || t == "--request")
            {
                if (i + 1 < tokens.Count) api.RequestType = tokens[++i].ToUpper();
            }
            else if (t == "-H" || t == "--header")
            {
                if (i + 1 < tokens.Count)
                {
                    var h = tokens[++i];
                    var colonIdx = h.IndexOf(':');
                    if (colonIdx > 0)
                        headers[h[..colonIdx].Trim()] = h[(colonIdx + 1)..].Trim();
                }
            }
            else if (t == "-d" || t == "--data" || t == "--data-raw")
            {
                if (i + 1 < tokens.Count)
                {
                    var body = tokens[++i];
                    TryParseJsonBody(body, bodyParams);
                }
            }
            else if (t.StartsWith("http://") || t.StartsWith("https://"))
            {
                api.Url = ParseUrlAndQuery(t, queryParams);
            }
        }

        api.MethodName = GenerateMethodName(api.Url, api.RequestType);
        api.InputParams = queryParams.Select(kv => new GeneratedParam { ParamCode = kv.Key, ParamName = kv.Key, DataType = "string" }).ToList();
        if (bodyParams.Count > 0)
        {
            api.InputParams.AddRange(bodyParams.Select(kv => new GeneratedParam { ParamCode = kv.Key, ParamName = kv.Key, DataType = "string" }));
        }
        api.Headers = headers.Select(kv => new GeneratedParam { ParamCode = kv.Key, ParamName = kv.Key, DataType = "string" }).ToList();

        result.Add(api);
        return result;
    }

    private List<string> TokenizeCurl(string curl)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false; char quoteChar = '"';
        for (int i = 0; i < curl.Length; i++)
        {
            var c = curl[i];
            if (!inQuote && (c == '"' || c == '\''))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString().Trim()); sb.Clear(); }
                inQuote = true; quoteChar = c;
            }
            else if (inQuote && c == quoteChar && (i == 0 || curl[i - 1] != '\\'))
            {
                tokens.Add(sb.ToString()); sb.Clear();
                inQuote = false;
            }
            else if (!inQuote && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString().Trim()); sb.Clear(); }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) tokens.Add(sb.ToString().Trim());
        return tokens;
    }

    private string ParseUrlAndQuery(string url, Dictionary<string, string> queryParams)
    {
        var qIdx = url.IndexOf('?');
        if (qIdx < 0) return url;
        var query = url[(qIdx + 1)..];
        url = url[..qIdx];
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2)
                queryParams[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
        }
        return url;
    }

    private void TryParseJsonBody(string body, Dictionary<string, string> bodyParams)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    bodyParams[prop.Name] = prop.Value.ToString();
                }
            }
        }
        catch { /* 非JSON body，忽略 */ }
    }

    // ========== Swagger/OpenAPI 解析 ==========

    private List<GeneratedApiItem> ParseSwaggerJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var baseUrl = "";
        if (root.TryGetProperty("host", out var hostEl))
        {
            var scheme = "http";
            if (root.TryGetProperty("schemes", out var schemesEl) && schemesEl.GetArrayLength() > 0)
                scheme = schemesEl[0].GetString() ?? "http";
            var basePath = "";
            if (root.TryGetProperty("basePath", out var bpEl)) basePath = bpEl.GetString() ?? "";
            baseUrl = $"{scheme}://{hostEl.GetString()}{basePath}";
        }
        // OpenAPI 3.x
        if (root.TryGetProperty("servers", out var serversEl) && serversEl.GetArrayLength() > 0)
        {
            baseUrl = serversEl[0].GetProperty("url").GetString() ?? "";
            baseUrl = baseUrl.Replace("{", "").Replace("}", ""); // 去掉变量占位
        }

        var result = new List<GeneratedApiItem>();
        var pathsEl = root.TryGetProperty("paths", out var p) ? p : default;
        if (pathsEl.ValueKind != JsonValueKind.Object) return result;

        foreach (var pathProp in pathsEl.EnumerateObject())
        {
            var pathUrl = baseUrl + pathProp.Name;
            foreach (var methodProp in pathProp.Value.EnumerateObject())
            {
                var method = methodProp.Name.ToUpper();
                if (method != "GET" && method != "POST" && method != "PUT" && method != "DELETE" && method != "PATCH")
                    continue;

                var api = new GeneratedApiItem
                {
                    RequestType = method,
                    Url = pathUrl,
                    MethodName = methodProp.Value.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : ""
                };
                if (string.IsNullOrEmpty(api.MethodName))
                    api.MethodName = methodProp.Value.TryGetProperty("operationId", out var oid) ? oid.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(api.MethodName))
                    api.MethodName = $"{method}{pathProp.Name.Replace("/", "_")}";

                // 参数
                if (methodProp.Value.TryGetProperty("parameters", out var paramsEl))
                {
                    foreach (var paramEl in paramsEl.EnumerateArray())
                    {
                        var pCode = paramEl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var pType = paramEl.TryGetProperty("type", out var t) ? t.GetString() ?? "string" : "string";
                        var pIn = paramEl.TryGetProperty("in", out var pin) ? pin.GetString() ?? "" : "";
                        var pRequired = 0;
                        if (paramEl.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.True) pRequired = 1;
                        if (pIn == "header")
                            api.Headers.Add(new GeneratedParam { ParamCode = pCode, ParamName = pCode, DataType = pType, Required = pRequired });
                        else
                            api.InputParams.Add(new GeneratedParam { ParamCode = pCode, ParamName = pCode, DataType = pType, Required = pRequired });
                    }
                }

                // Request body (OpenAPI 3.x)
                if (methodProp.Value.TryGetProperty("requestBody", out var rbEl) &&
                    rbEl.TryGetProperty("content", out var ctEl))
                {
                    foreach (var ctProp in ctEl.EnumerateObject())
                    {
                        if (ctProp.Value.TryGetProperty("schema", out var schemaEl) &&
                            schemaEl.TryGetProperty("properties", out var propsEl))
                        {
                            foreach (var pp in propsEl.EnumerateObject())
                            {
                                var pType = pp.Value.TryGetProperty("type", out var pt) ? pt.GetString() ?? "string" : "string";
                                api.InputParams.Add(new GeneratedParam { ParamCode = pp.Name, ParamName = pp.Name, DataType = pType });
                            }
                        }
                        break; // 只取第一个 content type
                    }
                }

                // Response → 出参 (OpenAPI 3.x & Swagger 2.0)
                ExtractResponseParams(methodProp.Value, api);

                result.Add(api);
            }
        }
        return result;
    }

    /// <summary>从 responses 中提取出参（支持 OpenAPI 3.x 和 Swagger 2.0）</summary>
    private void ExtractResponseParams(JsonElement methodEl, GeneratedApiItem api)
    {
        if (!methodEl.TryGetProperty("responses", out var responsesEl)) return;

        // 取第一个成功响应 (200/201/default)
        JsonElement targetResp = default;
        foreach (var respProp in responsesEl.EnumerateObject())
        {
            if (respProp.Name == "200" || respProp.Name == "201" || respProp.Name == "default" || respProp.Name == "2XX")
            {
                targetResp = respProp.Value;
                break;
            }
        }
        if (targetResp.ValueKind != JsonValueKind.Object) return;

        // OpenAPI 3.x: responses.200.content.application/json.schema.properties
        if (targetResp.TryGetProperty("content", out var contentEl))
        {
            foreach (var ctProp in contentEl.EnumerateObject())
            {
                if (ctProp.Value.TryGetProperty("schema", out var schemaEl))
                {
                    ExtractSchemaProps(schemaEl, api.OutputParams);
                    return;
                }
            }
        }

        // Swagger 2.0: responses.200.schema.properties
        if (targetResp.TryGetProperty("schema", out var swSchemaEl))
        {
            ExtractSchemaProps(swSchemaEl, api.OutputParams);
        }
    }

    private void ExtractSchemaProps(JsonElement schema, List<GeneratedParam> target)
    {
        if (schema.TryGetProperty("properties", out var propsEl))
        {
            foreach (var pp in propsEl.EnumerateObject())
            {
                var pType = pp.Value.TryGetProperty("type", out var pt) ? pt.GetString() ?? "string" : "string";
                target.Add(new GeneratedParam { ParamCode = pp.Name, ParamName = pp.Name, DataType = pType });
            }
        }
        // 处理 allOf / oneOf 等组合 schema
        if (schema.TryGetProperty("allOf", out var allOfEl))
        {
            foreach (var sub in allOfEl.EnumerateArray())
                ExtractSchemaProps(sub, target);
        }
    }

    // ========== WSDL 解析 ==========

    private List<GeneratedApiItem> ParseWsdl(string xml, string sourceUrl)
    {
        var result = new List<GeneratedApiItem>();
        XDocument xdoc;
        try { xdoc = XDocument.Parse(xml); }
        catch { return result; }

        XNamespace wsdl = "http://schemas.xmlsoap.org/wsdl/";
        XNamespace soap = "http://schemas.xmlsoap.org/wsdl/soap/";
        XNamespace soap12 = "http://schemas.xmlsoap.org/wsdl/soap12/";
        XNamespace http = "http://schemas.xmlsoap.org/wsdl/http/";
        XNamespace xs = "http://www.w3.org/2001/XMLSchema";

        if (!xdoc.Root!.Name.NamespaceName.Contains("schemas.xmlsoap.org"))
            wsdl = xdoc.Root.Name.Namespace;

        // ===== 解析 XSD types → 构建 element 定义查找表 =====
        var typesEl = xdoc.Descendants(wsdl + "types").FirstOrDefault();
        var schemaNs = "";
        // xsdElements: elementQName → [{ ParamCode, ParamName, DataType }]
        var xsdElements = new Dictionary<string, List<GeneratedParam>>();
        if (typesEl != null)
        {
            var schemaEl = typesEl.Elements().FirstOrDefault(e => e.Name == xs + "schema");
            if (schemaEl != null)
            {
                schemaNs = schemaEl.Attribute("targetNamespace")?.Value ?? "";

                // 遍历所有顶级 element 定义
                foreach (var elemEl in schemaEl.Elements(xs + "element"))
                {
                    var elemName = elemEl.Attribute("name")?.Value ?? "";
                    if (string.IsNullOrEmpty(elemName)) continue;
                    var params_ = new List<GeneratedParam>();
                    var complexEl = elemEl.Element(xs + "complexType");
                    if (complexEl != null)
                    {
                        var seqEl = complexEl.Element(xs + "sequence")
                            ?? complexEl.Element(xs + "all");
                        if (seqEl != null)
                        {
                            foreach (var childEl in seqEl.Elements(xs + "element"))
                            {
                                var childName = childEl.Attribute("name")?.Value ?? "";
                                var childType = childEl.Attribute("type")?.Value ?? "string";
                                if (childType.Contains(':')) childType = childType.Split(':').Last();
                                var childMin = childEl.Attribute("minOccurs")?.Value;
                                var childMax = childEl.Attribute("maxOccurs")?.Value;
                                // 数组类型
                                var childDataType = childType;
                                if (childMax == "unbounded")
                                    childDataType = "array";
                                params_.Add(new GeneratedParam
                                {
                                    ParamCode = childName,
                                    ParamName = childName,
                                    DataType = childDataType
                                });
                            }
                        }
                    }
                    xsdElements[elemName] = params_;
                }
            }
        }

        // ===== 获取所有 messages =====
        var messages = new Dictionary<string, List<GeneratedParam>>();
        foreach (var msgEl in xdoc.Descendants(wsdl + "message"))
        {
            var msgName = msgEl.Attribute("name")?.Value ?? "";
            var paramList = new List<GeneratedParam>();
            foreach (var partEl in msgEl.Elements(wsdl + "part"))
            {
                var pName = partEl.Attribute("name")?.Value ?? "";
                var elemRef = partEl.Attribute("element")?.Value ?? "";
                var typeRef = partEl.Attribute("type")?.Value ?? "";

                // element 属性 → 展开为 XSD element 的子元素
                if (!string.IsNullOrEmpty(elemRef))
                {
                    var elemName = elemRef;
                    if (elemName.Contains(':')) elemName = elemName.Split(':').Last();
                    if (xsdElements.TryGetValue(elemName, out var expanded))
                    {
                        paramList.AddRange(expanded);
                    }
                    else
                    {
                        // fallback: 无法解析则用 element 名作为单个参数
                        paramList.Add(new GeneratedParam { ParamCode = pName, ParamName = pName, DataType = elemName });
                    }
                }
                else if (!string.IsNullOrEmpty(typeRef))
                {
                    // type 属性 → 简单类型，直接用 part name
                    var dt = typeRef;
                    if (dt.Contains(':')) dt = dt.Split(':').Last();
                    paramList.Add(new GeneratedParam { ParamCode = pName, ParamName = pName, DataType = dt });
                }
                else
                {
                    // 无 element 也无 type → 用 part name 作为参数名
                    paramList.Add(new GeneratedParam { ParamCode = pName, ParamName = pName, DataType = "string" });
                }
            }
            messages[msgName] = paramList;
        }

        // 解析 bindings → 获取每个操作的协议信息
        // bindingOps: operationName → [绑定信息列表]，支持同一操作多种绑定
        var bindingOps = new Dictionary<string, List<WsdlBindingInfo>>();

        foreach (var bindingEl in xdoc.Descendants(wsdl + "binding"))
        {
            var bindingName = bindingEl.Attribute("name")?.Value ?? "";
            // 检查 binding 类型
            var soapBinding = bindingEl.Element(soap + "binding");
            var soap12Binding = bindingEl.Element(soap12 + "binding");
            var httpBinding = bindingEl.Element(http + "binding");

            // 允许一个 binding 同时支持 SOAP1.1 和 SOAP1.2
            var bindTypes = new List<string>();
            if (soap12Binding != null) bindTypes.Add("SOAP12");
            if (soapBinding != null) bindTypes.Add("SOAP11");
            if (httpBinding != null)
            {
                var verb = httpBinding.Attribute("verb")?.Value?.ToUpper() ?? "GET";
                bindTypes.Add(verb == "POST" ? "HTTP_POST" : "HTTP_GET");
            }
            if (bindTypes.Count == 0) continue;

            // 遍历 binding 中的每个 operation
            foreach (var bOpEl in bindingEl.Elements(wsdl + "operation"))
            {
                var opName = bOpEl.Attribute("name")?.Value ?? "";
                if (string.IsNullOrEmpty(opName)) continue;

                // SOAP 扩展信息
                var soapOp = bOpEl.Element(soap + "operation");
                var soap12Op = bOpEl.Element(soap12 + "operation");
                var httpOp = bOpEl.Element(http + "operation");

                // SOAP header
                var bInput = bOpEl.Element(wsdl + "input");
                string? headerMessage = null, headerPart = null;
                if (bInput != null)
                {
                    var soapHeader = bInput.Element(soap + "header");
                    var soap12Header = bInput.Element(soap12 + "header");
                    var headerEl = soap12Header ?? soapHeader;
                    if (headerEl != null)
                    {
                        headerMessage = headerEl.Attribute("message")?.Value ?? "";
                        headerPart = headerEl.Attribute("part")?.Value ?? "";
                    }
                }

                if (!bindingOps.ContainsKey(opName))
                    bindingOps[opName] = new List<WsdlBindingInfo>();

                foreach (var bindType in bindTypes)
                {
                    var info = new WsdlBindingInfo
                    {
                        Type = bindType,
                        SoapAction = soap12Op?.Attribute("soapAction")?.Value
                            ?? soapOp?.Attribute("soapAction")?.Value ?? "",
                        Location = httpOp?.Attribute("location")?.Value ?? "",
                        HeaderMessage = headerMessage,
                        HeaderPart = headerPart
                    };
                    bindingOps[opName].Add(info);
                }
            }
        }

        // 解析 service → port → address，获取各 binding 对应的 URL
        var bindingUrls = new Dictionary<string, string>(); // bindingName → url
        foreach (var serviceEl in xdoc.Descendants(wsdl + "service"))
        {
            foreach (var portEl in serviceEl.Elements(wsdl + "port"))
            {
                var bName = portEl.Attribute("binding")?.Value ?? "";
                if (bName.Contains(':')) bName = bName.Split(':').Last();
                var addr = portEl.Elements(soap + "address").FirstOrDefault()
                    ?? portEl.Elements(soap12 + "address").FirstOrDefault()
                    ?? portEl.Elements(http + "address").FirstOrDefault();
                if (addr != null)
                {
                    var loc = addr.Attribute("location")?.Value ?? sourceUrl;
                    bindingUrls[bName] = loc;
                }
            }
        }

        // 构建 binding → URL 映射（通过 portType 关联）
        var bindingPortTypes = new Dictionary<string, string>(); // bindingName → portTypeName
        foreach (var bEl in xdoc.Descendants(wsdl + "binding"))
        {
            var bName = bEl.Attribute("name")?.Value ?? "";
            var ptName = bEl.Attribute("type")?.Value ?? "";
            if (ptName.Contains(':')) ptName = ptName.Split(':').Last();
            bindingPortTypes[bName] = ptName;
        }

        // 获取所属 portType（通过 wsdl:portType 下的 operation name 匹配）
        var portTypeOps = new Dictionary<string, string>(); // operationName → portTypeName
        foreach (var ptEl in xdoc.Descendants(wsdl + "portType"))
        {
            var ptName = ptEl.Attribute("name")?.Value ?? "";
            foreach (var opEl in ptEl.Elements(wsdl + "operation"))
            {
                var oName = opEl.Attribute("name")?.Value ?? "";
                if (!string.IsNullOrEmpty(oName)) portTypeOps[oName] = ptName;
            }
        }

        // 获取所有 portType operations，为每个支持的 binding 生成一个接口
        foreach (var opEl in xdoc.Descendants(wsdl + "operation"))
        {
            var opName = opEl.Attribute("name")?.Value ?? "";
            // 只处理 portType 下的 operation（排除 binding 下的同名 operation）
            if (opEl.Parent?.Name != wsdl + "portType") continue;

            var inputMsg = opEl.Element(wsdl + "input")?.Attribute("message")?.Value ?? "";
            var outputMsg = opEl.Element(wsdl + "output")?.Attribute("message")?.Value ?? "";
            if (inputMsg.Contains(':')) inputMsg = inputMsg.Split(':').Last();
            if (outputMsg.Contains(':')) outputMsg = outputMsg.Split(':').Last();

            var bindings = bindingOps.GetValueOrDefault(opName);
            var inputParams = new List<GeneratedParam>(messages.GetValueOrDefault(inputMsg, new List<GeneratedParam>()));
            var outputParams = new List<GeneratedParam>(messages.GetValueOrDefault(outputMsg, new List<GeneratedParam>()));

            var ptName = portTypeOps.GetValueOrDefault(opName, "");

            // 没有 binding 信息 → 默认生成 SOAP 1.1
            if (bindings == null || bindings.Count == 0)
            {
                bindings = new List<WsdlBindingInfo> { new WsdlBindingInfo { Type = "SOAP11" } };
            }

            int bindIdx = 0;
            foreach (var info in bindings)
            {
                var bindType = info.Type;
                var soapAction = info.SoapAction ?? "";
                var httpLocation = info.Location ?? "";

                // 获取该 binding 对应的 URL
                var opUrl = sourceUrl;
                if (bindingPortTypes.Any())
                {
                    // 找到引用此 portType 且匹配 binding type 的 binding
                    foreach (var bKv in bindingPortTypes)
                    {
                        if (bKv.Value == ptName && bindingUrls.TryGetValue(bKv.Key, out var bUrl))
                            opUrl = bUrl;
                    }
                }

                // 多 binding 时，给接口名加后缀以区分
                var suffix = bindings.Count > 1 ? $"_{bindType.ToLower()}" : "";
                var apiName = opName + suffix;

                GeneratedApiItem api;

                if (bindType == "SOAP11" || bindType == "SOAP12")
                {
                    api = new GeneratedApiItem
                    {
                        MethodName = apiName,
                        MethodType = "WEBSERVICE",
                        RequestType = "POST",
                        Url = opUrl,
                        SoapMethod = opName,
                        SoapNamespace = schemaNs,
                        SoapVersion = bindType == "SOAP12" ? "12" : "11",
                        SoapAction = soapAction,
                        InputParams = new List<GeneratedParam>(inputParams),
                        OutputParams = new List<GeneratedParam>(outputParams)
                    };

                    if (!string.IsNullOrEmpty(soapAction))
                    {
                        api.Headers.Add(new GeneratedParam
                        {
                            ParamCode = "SOAPAction", ParamName = "SOAPAction",
                            DataType = "string", DefaultValue = soapAction
                        });
                    }
                    api.Headers.Add(new GeneratedParam
                    {
                        ParamCode = "Content-Type", ParamName = "Content-Type",
                        DataType = "string",
                        DefaultValue = bindType == "SOAP12"
                            ? "application/soap+xml; charset=utf-8"
                            : "text/xml; charset=utf-8"
                    });

                    if (!string.IsNullOrEmpty(info.HeaderMessage))
                    {
                        var headerMsg = info.HeaderMessage;
                        if (headerMsg.Contains(':')) headerMsg = headerMsg.Split(':').Last();
                        if (messages.TryGetValue(headerMsg, out var hParams))
                        {
                            foreach (var hp in hParams)
                                api.Headers.Add(new GeneratedParam
                                {
                                    ParamCode = hp.ParamCode, ParamName = hp.ParamName,
                                    DataType = hp.DataType, Required = 1
                                });
                        }
                    }
                }
                else // HTTP_GET or HTTP_POST
                {
                    var verb = bindType == "HTTP_POST" ? "POST" : "GET";
                    var httpUrl = opUrl;
                    if (!string.IsNullOrEmpty(httpLocation))
                    {
                        try
                        {
                            var uri = new Uri(httpUrl);
                            httpUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}{httpLocation}";
                        }
                        catch { /* 保持原 URL */ }
                    }

                    api = new GeneratedApiItem
                    {
                        MethodName = apiName,
                        MethodType = "HTTP",
                        RequestType = verb,
                        Url = httpUrl,
                        InputParams = new List<GeneratedParam>(inputParams),
                        OutputParams = new List<GeneratedParam>(outputParams)
                    };

                    if (verb == "POST")
                    {
                        api.Headers.Add(new GeneratedParam
                        {
                            ParamCode = "Content-Type", ParamName = "Content-Type",
                            DataType = "string", DefaultValue = "application/x-www-form-urlencoded"
                        });
                    }
                }

                result.Add(api);
                bindIdx++;
            }
        }

        return result;
    }

    private class WsdlBindingInfo
    {
        public string Type { get; set; } = "SOAP11";
        public string? SoapAction { get; set; }
        public string? Location { get; set; }
        public string? HeaderMessage { get; set; }
        public string? HeaderPart { get; set; }
    }

    private string GenerateMethodName(string url, string method)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath.Trim('/');
            var parts = path.Split('/');
            return method.ToUpper() + "_" + string.Join("_", parts.Where(p => !string.IsNullOrEmpty(p))).ToLower();
        }
        catch { return method + "_api"; }
    }
}

// ========== 请求/响应模型 ==========

public class GenerateFromCurlRequest
{
    public string Curl { get; set; } = "";
}

public class GenerateFromUrlRequest
{
    public string Url { get; set; } = "";
}

public class GenerateFromJsonRequest
{
    public string Json { get; set; } = "";
}

public class GenerateFromWsdlRequest
{
    public string WsdlContent { get; set; } = "";
    public string? Url { get; set; }
}

public class GeneratedApiItem
{
    public string MethodName { get; set; } = "";
    public string MethodType { get; set; } = "HTTP";
    public string RequestType { get; set; } = "GET";
    public string Url { get; set; } = "";
    public string? SoapVersion { get; set; } = "11";
    public string? SoapMethod { get; set; }
    public string? SoapNamespace { get; set; }
    public string? SoapAction { get; set; }
    public List<GeneratedParam> InputParams { get; set; } = new();
    public List<GeneratedParam> OutputParams { get; set; } = new();
    public List<GeneratedParam> Headers { get; set; } = new();
}

public class GeneratedParam
{
    public string ParamCode { get; set; } = "";
    public string ParamName { get; set; } = "";
    public string DataType { get; set; } = "string";
    public int Required { get; set; } = 0;
    public string? DefaultValue { get; set; }
}
