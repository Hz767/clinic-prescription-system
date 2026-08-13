using System.Globalization;
using System.Windows.Data;

namespace Clinic.Presentation.Converters;

/// <summary>
/// 将处方类型/状态的 int 值转换为中文显示文本。
/// ConverterParameter="Type" 转换处方类型，"Status" 转换处方状态。
/// </summary>
public class PrescriptionTypeStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int intValue)
            return value?.ToString() ?? string.Empty;

        var mode = parameter as string;
        return mode switch
        {
            "Type" => intValue switch
            {
                0 => "普通",
                1 => "急诊",
                _ => intValue.ToString()
            },
            "Status" => intValue switch
            {
                0 => "草稿",
                1 => "已保存",
                2 => "已收费",
                3 => "已作废",
                _ => intValue.ToString()
            },
            _ => intValue.ToString()
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
