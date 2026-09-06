using System.Text;
using System.Text.Json;

namespace Clinic.AiScribe.Services;

/// <summary>LLM服务接口（复用llama.cpp）</summary>
public interface ILlmService
{
    /// <summary>是否可用（llama-server运行中）</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>发送聊天请求，返回响应文本</summary>
    Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}

/// <summary>
/// 基于llama.cpp OpenAI兼容API的LLM服务
/// 复用诊所系统已有的llama-server实例
/// </summary>
public class LlamaCppService : ILlmService
{
    private readonly ILogger<LlamaCppService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly double _temperature;
    private readonly int _timeoutSeconds;

    public LlamaCppService(ILogger<LlamaCppService> logger, IConfiguration config, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _endpoint = config["Llm:Endpoint"] ?? "http://127.0.0.1:8080/v1/chat/completions";
        _model = config["Llm:Model"] ?? "qwen2.5-7b-instruct";
        _maxTokens = int.TryParse(config["Llm:MaxTokens"], out var mt) ? mt : 2048;
        _temperature = double.TryParse(config["Llm:Temperature"], out var temp) ? temp : 0.3;
        _timeoutSeconds = int.TryParse(config["Llm:TimeoutSeconds"], out var ts) ? ts : 60;
        _httpClient.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            // llama-server的健康检查端点
            var baseUrl = _endpoint.Replace("/v1/chat/completions", "/health");
            var response = await _httpClient.GetAsync(baseUrl, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            max_tokens = _maxTokens,
            temperature = _temperature,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("发送LLM请求，输入长度：{Length}", userMessage.Length);

        var response = await _httpClient.PostAsync(_endpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        _logger.LogInformation("LLM响应完成，输出长度：{Length}", text.Length);
        return text;
    }
}
