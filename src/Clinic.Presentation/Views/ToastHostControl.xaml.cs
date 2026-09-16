using System.Windows.Controls;

namespace Clinic.Presentation.Views;

/// <summary>
/// 全局 Toast 宿主控件，展示 <see cref="Clinic.Presentation.Services.ToastService.Toasts"/>
/// 集合中的消息，右下角悬浮显示、3 秒自动消失。挂载在主窗口内容区顶层。
/// </summary>
public partial class ToastHostControl : UserControl
{
    public ToastHostControl()
    {
        InitializeComponent();
        // DataContext 通过 x:Static 静态绑定到 ToastService.Instance，无需代码设置
    }
}