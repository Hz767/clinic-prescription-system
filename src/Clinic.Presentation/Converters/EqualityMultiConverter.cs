using System.Globalization;
using System.Windows.Data;

namespace Clinic.Presentation.Converters;

/// <summary>
/// 多值比较转换器：判断两个值是否相等（用于导航高亮等场景）。
/// </summary>
public class EqualityMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        var a = values[0]?.ToString();
        var b = values[1]?.ToString();
        return a == b && a is not null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
