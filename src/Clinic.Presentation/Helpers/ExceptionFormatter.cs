using FluentValidation;

namespace Clinic.Presentation.Helpers;

/// <summary>
/// 异常格式化辅助工具。
/// 将 FluentValidation 的 ValidationException 转换为用户友好的错误消息。
/// </summary>
public static class ExceptionFormatter
{
    /// <summary>
    /// 从异常中提取用户友好的错误消息。
    /// 如果是 ValidationException，拼接所有验证错误；否则返回 ex.Message。
    /// </summary>
    public static string GetMessage(Exception ex)
    {
        if (ex is ValidationException validationEx)
        {
            var errors = validationEx.Errors
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrEmpty(m))
                .ToList();

            return errors.Count > 0
                ? string.Join("；", errors)
                : "输入验证失败";
        }

        return ex.Message;
    }
}
