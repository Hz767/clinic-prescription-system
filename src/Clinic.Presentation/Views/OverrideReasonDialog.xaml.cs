using System.Windows;

namespace Clinic.Presentation.Views;

/// <summary>
/// 临床覆盖（Override）确认对话框。
/// 当处方存在阻断性问题（过敏/交互/禁忌症）而医生坚持开具时，
/// 要求填写覆盖理由（必填），理由将随保存写入审计日志。
/// </summary>
public partial class OverrideReasonDialog : Window
{
    private string? _result;

    public OverrideReasonDialog(IReadOnlyList<string> blockers)
    {
        InitializeComponent();

        BlockersText.Text = "阻断性问题：\n" + string.Join("\n", blockers.Select(b => "• " + b));
        ReasonInput.Focus();
    }

    /// <summary>弹出对话框，返回覆盖理由；用户取消则返回 null。</summary>
    public static string? Show(IReadOnlyList<string> blockers)
    {
        var dialog = new OverrideReasonDialog(blockers);
        dialog.ShowDialog();
        return dialog._result;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var reason = ReasonInput.Text?.Trim();

        if (string.IsNullOrWhiteSpace(reason))
        {
            ErrorText.Text = "覆盖理由不能为空（阻断性问题必须说明临床判断依据）";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (reason.Length > 500)
        {
            ErrorText.Text = "覆盖理由不能超过500个字符";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _result = reason;
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
