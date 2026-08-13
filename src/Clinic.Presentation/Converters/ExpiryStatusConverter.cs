using System.Globalization;
using System.Windows.Data;

namespace Clinic.Presentation.Converters;

/// <summary>
/// 效期状态转换器。将 DaysRemaining (int) 转换为状态字符串。
/// &lt; 0  → "expired"（已过期，红色背景）
/// 0-6  → "warning"（即将过期，橙色背景）
/// &gt;= 7 → "normal"（正常）
/// 
/// 配合 DataTrigger 使用：
///   Binding="{Binding DaysRemaining, Converter={StaticResource ExpiryStatusConverter}}"
///   Value="expired" / "warning" / "normal"
/// </summary>
public class ExpiryStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int days)
        {
            if (days < 0) return "expired";
            if (days <= 6) return "warning";
            return "normal";
        }
        return "normal";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
