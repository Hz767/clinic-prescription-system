using System.Windows;
using System.Windows.Controls;
using Clinic.Presentation.ViewModels;

namespace Clinic.Presentation.Views;

/// <summary>
/// 库存管理视图。DataTemplate 在 MainWindow.xaml 中将其与 InventoryViewModel 关联。
/// 加载时自动触发数据加载。
/// </summary>
public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
    }

    private void InventoryView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is InventoryViewModel vm)
        {
            _ = vm.LoadDataCommand.ExecuteAsync(null);
        }
    }
}
