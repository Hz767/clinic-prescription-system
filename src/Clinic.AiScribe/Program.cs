using Clinic.AiScribe.Endpoints;
using Clinic.AiScribe.Services;

var builder = WebApplication.CreateBuilder(args);

// 服务注册
builder.Services.AddSingleton<IAudioRecorder, AudioRecorder>();
builder.Services.AddSingleton<IAsrService, FunAsrService>();
builder.Services.AddSingleton<ISpeakerDiarizer, SimpleDiarizer>();
builder.Services.AddSingleton<MedicalRecordExtractor>();
builder.Services.AddSingleton<ScribeSessionManager>();
builder.Services.AddHttpClient<ILlmService, LlamaCppService>();

// CORS（允许WPF前端访问）
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://127.0.0.1", "http://localhost")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

// 映射API端点
app.MapGet("/", () => Results.Ok(new
{
    name = "Clinic AI Scribe Service",
    version = "1.0.0",
    endpoints = new[]
    {
        "GET /api/scribe/health",
        "POST /api/scribe/start",
        "POST /api/scribe/stop",
        "POST /api/scribe/generate",
        "GET /api/scribe/status/{sessionId}",
        "POST /api/scribe/cancel/{sessionId}"
    }
}));

app.MapScribeEndpoints();

// 启动时清理旧会话
using (var scope = app.Services.CreateScope())
{
    var sessionManager = scope.ServiceProvider.GetRequiredService<ScribeSessionManager>();
    sessionManager.CleanupOldSessions();
}

var port = builder.Configuration["Urls"] ?? "http://127.0.0.1:5080";
app.Urls.Add(port);

app.Logger.LogInformation("AI问诊Agent服务启动，监听：{Port}", port);
app.Logger.LogInformation("ASR可用：{Asr}", app.Services.GetRequiredService<IAsrService>().IsAvailable);

app.Run();
