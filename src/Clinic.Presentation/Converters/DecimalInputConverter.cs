using System.Globalization;
using System.Windows.Data;

namespace Clinic.Presentation.Converters;

/// <summary>
/// decimal? 与 string 之间的双向转换器。
/// 允许输入过程中的临时无效状态（如"36."或"36.5"正在输入），
/// 转换失败时返回 Binding.DoNothing 而非抛出异常，让用户继续输入。
/// </summary>
public class DecimalInputConverter : IValueConverter
{
    /// <summary>decimal? → string（显示用）</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
            return d.ToString(culture ?? CultureInfo.CurrentCulture);
        return string.Empty;
    }

    /// <summary>string → decimal?（输入用），转换失败返回 DoNothing</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s)
            return Binding.DoNothing;

        s = s.Trim();

        // 空字符串 → null
        if (string.IsNullOrEmpty(s))
            return null;

        // 尝试转换，成功则返回 decimal 值
        if (decimal.TryParse(s, culture ?? CultureInfo.CurrentCulture, out var result))
            return result;

        // 允许以下临时输入状态：不以失败覆盖源值
        // - "36." （正在输入小数）
        // - "+" "-" （正负号）
        // - "." （仅小数点）
        // - "36," （逗号分隔，某些区域设置）
        if (s == "." || s == "," || s == "-" || s == "+" || s.EndsWith('.') || s.EndsWith(','))
            return Binding.DoNothing;

        // 其他无效输入也不阻塞，返回 DoNothing 保持上一个值
        return Binding.DoNothing;
    }
}
