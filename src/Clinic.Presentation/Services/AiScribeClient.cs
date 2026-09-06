using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Clinic.Presentation.Services;

/// <summary>
/// AI问诊Agent HTTP客户端实现
/// 与Clinic.AiScribe服务通信（127.0.0.1:5080）
/// </summary>
public class AiScribeClient : IAiScribeClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    public AiScribeClient(string baseUrl = "http://127.0.0.1:5080")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/scribe/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> StartAsync(long patientId, long doctorId)
    {
        var request = new { patientId, doctorId };
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/scribe/start", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<StartResponse>(_jsonOptions);
        return result?.SessionId ?? throw new InvalidOperationException("未获取到会话ID");
    }

    public async Task<StopScribeResult> StopAsync(string sessionId)
    {
        var response = await _httpClient.PostAsync(
            $"{_baseUrl}/api/scribe/stop?sessionId={Uri.EscapeDataString(sessionId)}", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<StopScribeResult>(_jsonOptions);
        return result ?? throw new InvalidOperationException("停止问诊失败");
    }

    public async Task<MedicalRecordDraftResult> GenerateAsync(string sessionId)
    {
        var request = new { sessionId };
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/scribe/generate", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MedicalRecordDraftResult>(_jsonOptions);
        return result ?? throw new InvalidOperationException("生成病历失败");
    }

    public async Task<ScribeStatusResult> GetStatusAsync(string sessionId)
    {
        var response = await _httpClient.GetAsync(
            $"{_baseUrl}/api/scribe/status/{Uri.EscapeDataString(sessionId)}");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ScribeStatusResult>(_jsonOptions);
        return result ?? throw new InvalidOperationException("获取状态失败");
    }

    public async Task CancelAsync(string sessionId)
    {
        await _httpClient.PostAsync(
            $"{_baseUrl}/api/scribe/cancel/{Uri.EscapeDataString(sessionId)}", null);
    }

    private class StartResponse
    {
        public string SessionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
