using Clinic.AiScribe.Models;
using Clinic.AiScribe.Services;

namespace Clinic.AiScribe.Endpoints;

/// <summary>AI问诊HTTP API端点定义</summary>
public static class ScribeEndpoints
{
    public static WebApplication MapScribeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/scribe").WithTags("AI问诊");

        // 健康检查
        group.MapGet("/health", async (
            IAsrService asrService,
            ILlmService llmService) =>
        {
            return Results.Ok(new
            {
                asrAvailable = asrService.IsAvailable,
                llmAvailable = await llmService.IsAvailableAsync(),
                timestamp = DateTime.Now
            });
        });

        // 开始问诊
        group.MapPost("/start", async (
            StartScribeRequest request,
            ScribeSessionManager sessionManager,
            IAudioRecorder recorder,
            ILogger<Program> logger) =>
        {
            try
            {
                var session = sessionManager.Create(request.PatientId, request.DoctorId);
                await recorder.StartAsync(session.SessionId);
                sessionManager.UpdateStatus(session.SessionId, SessionStatus.Recording);

                return Results.Ok(new
                {
                    sessionId = session.SessionId,
                    status = "recording",
                    startTime = session.StartTime
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "开始问诊失败");
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 停止问诊
        group.MapPost("/stop", async (
            string sessionId,
            ScribeSessionManager sessionManager,
            IAudioRecorder recorder,
            IAsrService asrService,
            ISpeakerDiarizer diarizer,
            ILogger<Program> logger) =>
        {
            try
            {
                var session = sessionManager.Get(sessionId);
                if (session == null)
                    return Results.NotFound(new { error = "会话不存在" });

                // 停止录音
                var audioPath = await recorder.StopAsync(sessionId);
                session.AudioFilePath = audioPath;
                sessionManager.UpdateStatus(sessionId, SessionStatus.Transcribing);

                // ASR转写
                var segments = await asrService.TranscribeWithTimestampsAsync(audioPath);

                // 说话人分离
                segments = diarizer.Diarize(segments);

                session.Transcript = segments;
                session.EndTime = DateTime.Now;
                sessionManager.UpdateStatus(sessionId, SessionStatus.Completed);

                var fullText = string.Join("\n", segments.Select(s =>
                {
                    var speaker = s.Speaker switch { "doctor" => "医生", "patient" => "患者", _ => "未知" };
                    return $"{speaker}：{s.Text}";
                }));

                return Results.Ok(new StopScribeResponse
                {
                    SessionId = sessionId,
                    Duration = (session.EndTime!.Value - session.StartTime).TotalSeconds,
                    Transcript = segments,
                    FullText = fullText
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "停止问诊失败");
                sessionManager.UpdateStatus(sessionId, SessionStatus.Failed, ex.Message);
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 生成病历
        group.MapPost("/generate", async (
            GenerateRecordRequest request,
            ScribeSessionManager sessionManager,
            MedicalRecordExtractor extractor,
            ILogger<Program> logger) =>
        {
            try
            {
                var session = sessionManager.Get(request.SessionId);
                if (session == null)
                    return Results.NotFound(new { error = "会话不存在" });

                if (session.Transcript == null || session.Transcript.Count == 0)
                    return Results.BadRequest(new { error = "没有转写数据，请先停止问诊" });

                sessionManager.UpdateStatus(request.SessionId, SessionStatus.Generating);

                var draft = await extractor.ExtractAsync(session.Transcript);
                session.Draft = draft;
                sessionManager.UpdateStatus(request.SessionId, SessionStatus.Completed);

                return Results.Ok(draft);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "生成病历失败");
                sessionManager.UpdateStatus(request.SessionId, SessionStatus.Failed, ex.Message);
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 获取会话状态
        group.MapGet("/status/{sessionId}", (
            string sessionId,
            ScribeSessionManager sessionManager,
            IAudioRecorder recorder) =>
        {
            var session = sessionManager.Get(sessionId);
            if (session == null)
                return Results.NotFound(new { error = "会话不存在" });

            var duration = recorder.IsRecording(sessionId)
                ? recorder.GetDuration(sessionId)
                : (session.EndTime - session.StartTime)?.TotalSeconds;

            return Results.Ok(new SessionStatusResponse
            {
                SessionId = sessionId,
                Status = session.Status.ToString(),
                Duration = duration,
                ErrorMessage = session.ErrorMessage
            });
        });

        // 取消问诊
        group.MapPost("/cancel/{sessionId}", async (
            string sessionId,
            ScribeSessionManager sessionManager,
            IAudioRecorder recorder) =>
        {
            var session = sessionManager.Get(sessionId);
            if (session == null)
                return Results.NotFound(new { error = "会话不存在" });

            if (recorder.IsRecording(sessionId))
                await recorder.CancelAsync(sessionId);

            sessionManager.UpdateStatus(sessionId, SessionStatus.Cancelled);
            return Results.Ok(new { status = "cancelled" });
        });

        return app;
    }
}
