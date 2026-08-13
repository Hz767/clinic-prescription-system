using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Clinic.Infrastructure.Llm;

/// <summary>
/// 基于 Ollama REST API 的 LLM 服务实现。
/// 通过 HTTP 调用本地 Ollama 实例，将自由文本转换为结构化医疗信息。
/// 
/// 依赖项：
/// - 需要本地运行 Ollama（默认 http://localhost:11434）
/// - 需要预先拉取模型（如 ollama pull qwen2.5:7b）
/// 
/// 容错策略：
/// - 连接失败/超时 → 返回空结果，不抛异常
/// - JSON 解析失败 → 返回空结果 + 原始输出，不抛异常
/// - 模型返回非 JSON → 返回空结果 + 原始输出
/// </summary>
public sealed class OllamaLlmService : ILlmService, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<OllamaLlmService>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // 超时设置：LLM 推理可能较慢，给 120 秒
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    public OllamaLlmService(string endpoint, string model, ILogger<OllamaLlmService>? logger = null)
    {
        _model = model;
        _logger = logger;

        _http = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/')),
            Timeout = RequestTimeout
        };

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  ILlmService 实现
    // ═══════════════════════════════════════════════════════════════

    public async Task<StructuredDiagnosis> ParseDiagnosisAsync(string freeText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(freeText))
            return new StructuredDiagnosis(string.Empty, null, null, null, string.Empty);

        var prompt = LlmPromptTemplates.DiagnosisPrompt(freeText);
        var rawOutput = await CallOllamaAsync(prompt, ct);

        if (rawOutput is null)
            return new StructuredDiagnosis(freeText, null, null, null, string.Empty);

        try
        {
            var doc = JsonDocument.Parse(rawOutput);
            var root = doc.RootElement;

            return new StructuredDiagnosis(
                ChiefComplaint: GetString(root, "chiefComplaint") ?? freeText,
                Signs: GetString(root, "signs"),
                SuspectedDiagnosis: GetString(root, "suspectedDiagnosis"),
                SuggestedExams: GetString(root, "suggestedExams"),
                RawOutput: rawOutput);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "LLM 诊断结构化失败，返回原始文本");
            return new StructuredDiagnosis(freeText, null, null, null, rawOutput);
        }
    }

    public async Task<StructuredAllergy> ParseAllergyAsync(string freeText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(freeText))
            return new StructuredAllergy(Array.Empty<string>(), Array.Empty<string>(), null, string.Empty);

        var prompt = LlmPromptTemplates.AllergyPrompt(freeText);
        var rawOutput = await CallOllamaAsync(prompt, ct);

        if (rawOutput is null)
            return new StructuredAllergy(Array.Empty<string>(), Array.Empty<string>(), null, string.Empty);

        try
        {
            var doc = JsonDocument.Parse(rawOutput);
            var root = doc.RootElement;

            var drugTags = GetStringArray(root, "drugAllergyTags");
            var otherTags = GetStringArray(root, "otherAllergyTags");

            return new StructuredAllergy(
                DrugAllergyTags: drugTags,
                OtherAllergyTags: otherTags,
                ReactionDescription: GetString(root, "reactionDescription"),
                RawOutput: rawOutput);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "LLM 过敏史结构化失败，返回原始文本");
            return new StructuredAllergy(Array.Empty<string>(), Array.Empty<string>(), null, rawOutput);
        }
    }

    public async Task<PrescriptionSuggestion> ParsePrescriptionAsync(string freeText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(freeText))
            return new PrescriptionSuggestion(string.Empty, null, null, null, null, null, null, string.Empty);

        var prompt = LlmPromptTemplates.PrescriptionPrompt(freeText);
        var rawOutput = await CallOllamaAsync(prompt, ct);

        if (rawOutput is null)
            return new PrescriptionSuggestion(string.Empty, null, null, null, null, null, null, string.Empty);

        try
        {
            var doc = JsonDocument.Parse(rawOutput);
            var root = doc.RootElement;

            return new PrescriptionSuggestion(
                DrugName: GetString(root, "drugName") ?? string.Empty,
                Dose: GetDecimal(root, "dose"),
                DoseUnit: GetString(root, "doseUnit"),
                Frequency: GetString(root, "frequency"),
                Route: GetString(root, "route"),
                DurationDays: GetInt32(root, "durationDays"),
                TotalQty: GetDecimal(root, "totalQty"),
                RawOutput: rawOutput);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "LLM 处方建议结构化失败，返回原始文本");
            return new PrescriptionSuggestion(string.Empty, null, null, null, null, null, null, rawOutput);
        }
    }

    public async Task<LlmStatus> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/tags", ct);
            if (!response.IsSuccessStatusCode)
            {
                return new LlmStatus(false, _model, _http.BaseAddress?.ToString(),
                    $"Ollama 返回状态码 {(int)response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(content);

            var models = doc.RootElement.GetProperty("models");
            var modelNames = new List<string>();
            foreach (var m in models.EnumerateArray())
            {
                var name = m.GetProperty("name").GetString();
                if (name is not null) modelNames.Add(name);
            }

            var hasModel = modelNames.Any(n =>
                n.StartsWith(_model, StringComparison.OrdinalIgnoreCase));

            if (!hasModel)
            {
                return new LlmStatus(false, _model, _http.BaseAddress?.ToString(),
                    $"模型 {_model} 未找到，可用模型：{string.Join(", ", modelNames)}。请运行 ollama pull {_model}");
            }

            return new LlmStatus(true, _model, _http.BaseAddress?.ToString(),
                $"Ollama 连接正常，模型 {_model} 可用");
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "Ollama 服务不可达");
            return new LlmStatus(false, _model, _http.BaseAddress?.ToString(),
                "Ollama 服务不可达，请确认 Ollama 已启动（默认端口 11434）");
        }
        catch (TaskCanceledException)
        {
            return new LlmStatus(false, _model, _http.BaseAddress?.ToString(),
                "连接 Ollama 超时");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "获取 LLM 状态失败");
            return new LlmStatus(false, _model, _http.BaseAddress?.ToString(),
                $"状态检查异常：{ex.Message}");
        }
    }

    public void Dispose() => _http.Dispose();

    // ═══════════════════════════════════════════════════════════════
    //  私有方法
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 调用 Ollama /api/generate 端点，返回模型生成的文本。
    /// 使用 stream=false 模式，一次性获取完整响应。
    /// </summary>
    private async Task<string?> CallOllamaAsync(string prompt, CancellationToken ct)
    {
        try
        {
            var request = new
            {
                model = _model,
                prompt,
                stream = false,
                options = new { temperature = 0.1 }  // 低温度以获得更确定性的输出
            };

            var response = await _http.PostAsJsonAsync("/api/generate", request, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(content);
            var rawResponse = doc.RootElement.GetProperty("response").GetString();

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                _logger?.LogWarning("Ollama 返回空响应");
                return null;
            }

            // 提取 JSON 部分：模型可能在 JSON 前后附加 markdown 代码块标记
            return ExtractJson(rawResponse);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "Ollama HTTP 请求失败");
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger?.LogWarning("Ollama 请求超时");
            return null;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Ollama 响应解析失败");
            return null;
        }
    }

    /// <summary>
    /// 从模型输出中提取 JSON 内容。
    /// 处理模型可能附加的 markdown 代码块标记（```json ... ```）。
    /// </summary>
    private static string ExtractJson(string raw)
    {
        var trimmed = raw.Trim();

        // 尝试去掉 markdown 代码块
        if (trimmed.StartsWith("```"))
        {
            var end = trimmed.IndexOf("```", 3, StringComparison.Ordinal);
            if (end > 3)
            {
                trimmed = trimmed[3..end].Trim();
                // 去掉可能的 "json" 语言标记
                if (trimmed.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed[4..].Trim();
            }
        }

        // 找到第一个 { 和最后一个 }
        var start = trimmed.IndexOf('{');
        var last = trimmed.LastIndexOf('}');
        if (start >= 0 && last > start)
            return trimmed[start..(last + 1)];

        return trimmed;
    }

    private static string? GetString(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String)
            return element.GetString();
        return null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(s);
                }
            }
            return list;
        }
        return Array.Empty<string>();
    }

    private static decimal? GetDecimal(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDecimal();
        }
        return null;
    }

    private static int? GetInt32(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number)
        {
            return element.GetInt32();
        }
        return null;
    }
}