using System.Net.Http.Json;
using System.Text.Json;
using Clinic.Application.DTOs;
using Clinic.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Clinic.Infrastructure.Llm;

/// <summary>
/// 基于 llama.cpp llama-server OpenAI 兼容 API 的 LLM 服务实现。
/// 通过 HTTP 调用本地 llama-server 的 /v1/chat/completions 端点，
/// 将自由文本转换为结构化医疗信息。
/// 
/// 与 OllamaLlmService 的区别：
/// - 使用 OpenAI 兼容的 /v1/chat/completions（而非 Ollama 专有 /api/generate）
/// - 使用 chat messages 格式（而非原始 prompt 字符串）
/// - 与 llama-server 的 /v1/chat/completions 完全兼容
/// 
/// 容错策略（与 OllamaLlmService 一致）：
/// - 连接失败/超时 → 返回空结果，不抛异常
/// - JSON 解析失败 → 返回空结果 + 原始输出，不抛异常
/// </summary>
public sealed class LlamaCppLlmService : ILlmService, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<LlamaCppLlmService>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    public LlamaCppLlmService(string endpoint, string model, ILogger<LlamaCppLlmService>? logger = null)
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

    public async Task<StructuredDiagnosis> ParseDiagnosisAsync(string freeText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(freeText))
            return new StructuredDiagnosis(string.Empty, null, null, null, string.Empty);

        var prompt = LlmPromptTemplates.DiagnosisPrompt(freeText);
        var rawOutput = await CallChatCompletionAsync(prompt, ct);

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
        var rawOutput = await CallChatCompletionAsync(prompt, ct);

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
        var rawOutput = await CallChatCompletionAsync(prompt, ct);

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
            // llama-server 的 /health 端点返回 200 OK 表示服务就绪
            var response = await _http.GetAsync("/health", ct);
            if (!response.IsSuccessStatusCode)
            {
                return new LlmStatus(false, _model, _http.BaseAddress?.ToString(),
                    $"llama-server 返回状态码 {(int)response.StatusCode}");
            }

            return new LlmStatus(true, _model, _http.BaseAddress?.ToString(),
                "llama.cpp 服务正常");
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "llama-server 服务不可达");
            return new LlmStatus(false, _model, _http.BaseAddress?.ToString(),
                "llama.cpp 服务不可达，请确认 llama-server 已启动");
        }
        catch (TaskCanceledException)
        {
            return new LlmStatus(false, _model, _http.BaseAddress?.ToString(),
                "连接 llama-server 超时");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "获取 LLM 状态失败");
            return new LlmStatus(false, _model, _http.BaseAddress?.ToString(),
                $"状态检查异常：{ex.Message}");
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// 调用 llama-server 的 /v1/chat/completions 端点（OpenAI 兼容）。
    /// 使用 chat messages 格式，temperature=0.1 以获得确定性输出。
    /// </summary>
    private async Task<string?> CallChatCompletionAsync(string prompt, CancellationToken ct)
    {
        try
        {
            var request = new
            {
                model = "gpt-3.5-turbo", // llama-server 忽略此字段但要求存在
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.1
            };

            var response = await _http.PostAsJsonAsync("/v1/chat/completions", request, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(content);

            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                _logger?.LogWarning("llama-server 返回空 choices");
                return null;
            }

            var message = choices[0].GetProperty("message");
            var rawResponse = message.GetProperty("content").GetString();

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                _logger?.LogWarning("llama-server 返回空响应");
                return null;
            }

            return ExtractJson(rawResponse);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "llama-server HTTP 请求失败");
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger?.LogWarning("llama-server 请求超时");
            return null;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "llama-server 响应解析失败");
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

        if (trimmed.StartsWith("```"))
        {
            var end = trimmed.IndexOf("```", 3, StringComparison.Ordinal);
            if (end > 3)
            {
                trimmed = trimmed[3..end].Trim();
                if (trimmed.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed[4..].Trim();
            }
        }

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
            return element.GetDecimal();
        return null;
    }

    private static int? GetInt32(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number)
            return element.GetInt32();
        return null;
    }
}