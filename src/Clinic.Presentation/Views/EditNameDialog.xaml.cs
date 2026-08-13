using System.Windows;

namespace Clinic.Presentation.Views;

/// <summary>
/// 简单的姓名编辑对话框。通过静态 Show 方法调用，返回新姓名或 null（取消）。
/// </summary>
public partial class EditNameDialog : Window
{
    private string? _result;

    public EditNameDialog(string currentName)
    {
        InitializeComponent();
        NameInput.Text = currentName;
        NameInput.SelectAll();
        NameInput.Focus();
    }

    /// <summary>弹出对话框，返回新姓名；用户取消则返回 null。</summary>
    public static string? Show(string currentName)
    {
        var dialog = new EditNameDialog(currentName);
        dialog.ShowDialog();
        return dialog._result;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "姓名不能为空";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (name.Length > 50)
        {
            ErrorText.Text = "姓名不能超过50个字符";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _result = name;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _result = null;
        DialogResult = false;
        Close();
    }
}
