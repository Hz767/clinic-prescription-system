using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Clinic.Presentation.Controls;

/// <summary>
/// 带防抖搜索的自动完成输入框。
/// 输入文本后延迟指定毫秒数再触发搜索命令，避免频繁请求。
/// 参考 E-Clinic 的 AutoCompleteTextBox 实现。
/// </summary>
public partial class AutoCompleteTextBox : UserControl
{
    // ── 依赖属性 ──

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(AutoCompleteTextBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<object>), typeof(AutoCompleteTextBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(AutoCompleteTextBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty SearchCommandProperty =
        DependencyProperty.Register(nameof(SearchCommand), typeof(System.Windows.Input.ICommand), typeof(AutoCompleteTextBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(AutoCompleteTextBox),
            new PropertyMetadata(false));

    public static readonly DependencyProperty DebounceMsProperty =
        DependencyProperty.Register(nameof(DebounceMs), typeof(int), typeof(AutoCompleteTextBox),
            new PropertyMetadata(300));

    public static readonly DependencyProperty MinSearchLengthProperty =
        DependencyProperty.Register(nameof(MinSearchLength), typeof(int), typeof(AutoCompleteTextBox),
            new PropertyMetadata(1));

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(AutoCompleteTextBox),
            new PropertyMetadata(null, OnItemTemplateChanged));

    /// <summary>下拉列表项的数据模板</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>输入文本（双向绑定）</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>搜索结果集合</summary>
    public IEnumerable<object>? ItemsSource
    {
        get => (IEnumerable<object>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>选中项（双向绑定）</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>搜索命令（参数为当前输入文本）</summary>
    public System.Windows.Input.ICommand? SearchCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    /// <summary>下拉是否打开</summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>防抖延迟（毫秒），默认300ms</summary>
    public int DebounceMs
    {
        get => (int)GetValue(DebounceMsProperty);
        set => SetValue(DebounceMsProperty, value);
    }

    /// <summary>触发搜索的最小输入长度，默认1</summary>
    public int MinSearchLength
    {
        get => (int)GetValue(MinSearchLengthProperty);
        set => SetValue(MinSearchLengthProperty, value);
    }

    // ── 防抖定时器 ──

    private readonly DispatcherTimer _debounceTimer;

    public AutoCompleteTextBox()
    {
        InitializeComponent();
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DebounceMs)
        };
        _debounceTimer.Tick += OnDebounceTick;
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteTextBox ctrl)
        {
            ctrl.UpdateEmptyHint();
        }
    }

    private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteTextBox ctrl && e.NewValue is DataTemplate template)
        {
            ctrl.ResultList.ItemTemplate = template;
        }
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 重置防抖定时器
        _debounceTimer.Stop();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceMs);
        _debounceTimer.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();

        var keyword = Text?.Trim();
        if (string.IsNullOrEmpty(keyword) || keyword.Length < MinSearchLength)
        {
            IsDropDownOpen = false;
            return;
        }

        // 触发搜索命令
        if (SearchCommand?.CanExecute(keyword) == true)
        {
            SearchCommand.Execute(keyword);
        }

        // 延迟打开下拉（等待搜索结果加载）
        Dispatcher.BeginInvoke(() =>
        {
            UpdateEmptyHint();
            IsDropDownOpen = true;
        }, DispatcherPriority.Background);
    }

    private void InputBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // 聚焦时如果已有结果，显示下拉
        if (ItemsSource?.Cast<object>().Any() == true)
        {
            IsDropDownOpen = true;
        }
    }

    private void InputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // 延迟关闭，允许点击下拉项
        Dispatcher.BeginInvoke(() =>
        {
            if (!ResultList.IsKeyboardFocusWithin)
            {
                IsDropDownOpen = false;
            }
        }, DispatcherPriority.Background);
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedItem is not null)
        {
            IsDropDownOpen = false;
        }
    }

    private void UpdateEmptyHint()
    {
        var hasItems = ItemsSource?.Cast<object>().Any() == true;
        EmptyHint.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        ResultList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }
}
