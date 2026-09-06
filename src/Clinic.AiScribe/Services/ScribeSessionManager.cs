using System.Collections.Concurrent;
using Clinic.AiScribe.Models;

namespace Clinic.AiScribe.Services;

/// <summary>问诊会话管理服务</summary>
public class ScribeSessionManager
{
    private readonly ConcurrentDictionary<string, ScribeSession> _sessions = new();
    private readonly ILogger<ScribeSessionManager> _logger;

    public ScribeSessionManager(ILogger<ScribeSessionManager> logger)
    {
        _logger = logger;
    }

    public ScribeSession Create(long patientId, long doctorId)
    {
        var session = new ScribeSession
        {
            PatientId = patientId,
            DoctorId = doctorId,
            StartTime = DateTime.Now,
            Status = SessionStatus.Idle
        };
        _sessions[session.SessionId] = session;
        _logger.LogInformation("创建问诊会话：{SessionId}，患者：{PatientId}", session.SessionId, patientId);
        return session;
    }

    public ScribeSession? Get(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    public bool UpdateStatus(string sessionId, SessionStatus status, string? error = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return false;

        session.Status = status;
        if (error != null)
            session.ErrorMessage = error;
        return true;
    }

    public bool Remove(string sessionId)
    {
        var removed = _sessions.TryRemove(sessionId, out _);
        if (removed)
            _logger.LogInformation("移除会话：{SessionId}", sessionId);
        return removed;
    }

    /// <summary>清理超过24小时的旧会话</summary>
    public void CleanupOldSessions()
    {
        var cutoff = DateTime.Now.AddHours(-24);
        var toRemove = _sessions.Values
            .Where(s => s.EndTime.HasValue && s.EndTime.Value < cutoff)
            .Select(s => s.SessionId)
            .ToList();

        foreach (var id in toRemove)
            _sessions.TryRemove(id, out _);

        if (toRemove.Count > 0)
            _logger.LogInformation("清理了 {Count} 个旧会话", toRemove.Count);
    }
}
