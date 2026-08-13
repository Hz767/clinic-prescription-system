using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Clinic.Presentation.Converters;

/// <summary>
/// true → Visible，false → Collapsed
/// 设置 ConverterParameter="Invert" 可反转逻辑
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is true;
        if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}