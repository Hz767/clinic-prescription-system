using System.Globalization;
using System.Windows.Data;

namespace Clinic.Presentation.Converters;

/// <summary>
/// decimal? 与 string 之间的双向转换器。
/// 始终使用 InvariantCulture（"." 小数点），不依赖 WPF 传入的 culture 参数。
/// 仅允许数字和小数点，不支持正负号或逗号。
/// 输入过程中的临时状态（如"36."）返回 Binding.DoNothing 保持当前值。
/// </summary>
public class DecimalInputConverter : IValueConverter
{
    /// <summary>decimal? → string（显示用），始终用 "." 小数点</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
            return d.ToString(CultureInfo.InvariantCulture);
        return string.Empty;
    }

    /// <summary>string → decimal?（输入用），仅接受数字和 "." 小数点</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s)
            return Binding.DoNothing;

        s = s.Trim();

        // 空字符串 → null
        if (string.IsNullOrEmpty(s))
            return null;

        // 用 InvariantCulture 解析（"." 小数点），避免区域设置导致解析失败
        if (decimal.TryParse(s, CultureInfo.InvariantCulture, out var result))
            return result;

        // 允许临时输入状态："." 或 "36." （正在输入小数点）
        if (s == "." || s.EndsWith('.'))
            return Binding.DoNothing;

        // 其他无效输入（含字母、符号、逗号等）不更新源值
        return Binding.DoNothing;
    }
}
