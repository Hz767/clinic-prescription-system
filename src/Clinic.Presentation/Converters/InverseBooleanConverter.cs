using System.Globalization;
using System.Windows.Data;

namespace Clinic.Presentation.Converters;

/// <summary>
/// 布尔值取反转换器。
/// true → false，false → true。
/// 常用于 IsEnabled="{Binding IsBusy, Converter={...}}" 实现忙碌时禁用按钮。
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}
