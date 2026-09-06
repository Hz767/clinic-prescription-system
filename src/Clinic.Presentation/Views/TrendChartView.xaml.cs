using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Clinic.Presentation.Views;

/// <summary>
/// 轻量级折线图控件。用 Canvas 自绘，无需外部图表库。
/// 支持数据集合、标题、线条颜色自定义。
/// </summary>
public partial class TrendChartView : UserControl
{
    // ── 依赖属性 ──

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<object>), typeof(TrendChartView),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TrendChartView),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Brush), typeof(TrendChartView),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(13, 148, 136))));

    public static readonly DependencyProperty ValueFormatProperty =
        DependencyProperty.Register(nameof(ValueFormat), typeof(string), typeof(TrendChartView),
            new PropertyMetadata("0"));

    /// <summary>从数据项中提取数值的委托</summary>
    public Func<object, decimal>? ValueSelector { get; set; }

    /// <summary>从数据项中提取标签的委托</summary>
    public Func<object, string>? LabelSelector { get; set; }

    public IEnumerable<object>? ItemsSource
    {
        get => (IEnumerable<object>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public Brush LineColor
    {
        get => (Brush)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    public TrendChartView()
    {
        InitializeComponent();
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrendChartView chart)
        {
            chart.TitleText.Text = e.NewValue?.ToString() ?? string.Empty;
        }
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrendChartView chart)
        {
            chart.DrawChart();
        }
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawChart();
    }

    /// <summary>绘制折线图</summary>
    private void DrawChart()
    {
        ChartCanvas.Children.Clear();

        var items = ItemsSource?.Cast<object>().ToList();
        if (items is null || items.Count == 0 || ValueSelector is null) return;

        double width = ChartCanvas.ActualWidth;
        double height = ChartCanvas.ActualHeight;
        if (width < 50 || height < 50) return;

        // 边距
        double padLeft = 40, padRight = 10, padTop = 10, padBottom = 24;
        double chartW = width - padLeft - padRight;
        double chartH = height - padTop - padBottom;

        // 提取数值
        var values = items.Select(ValueSelector!).ToList();
        decimal maxVal = values.Max();
        decimal minVal = values.Min();
        if (maxVal == minVal) maxVal = minVal + 1;

        // Y轴范围（留10%余量）
        decimal range = maxVal - minVal;
        decimal yMax = maxVal + range * 0.1m;
        decimal yMin = Math.Max(0, minVal - range * 0.1m);
        decimal yRange = yMax - yMin;

        // 绘制Y轴网格线和标签
        int gridLines = 4;
        for (int i = 0; i <= gridLines; i++)
        {
            double y = padTop + chartH * (1 - (double)i / gridLines);
            decimal val = yMin + yRange * i / gridLines;

            // 网格线
            var line = new Line
            {
                X1 = padLeft,
                Y1 = y,
                X2 = padLeft + chartW,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
                StrokeThickness = 1
            };
            ChartCanvas.Children.Add(line);

            // Y轴标签
            var label = new TextBlock
            {
                Text = val.ToString(ValueFormat, CultureInfo.InvariantCulture),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(label, padLeft - 4);
            Canvas.SetTop(label, y - 7);
            ChartCanvas.Children.Add(label);
        }

        // 计算数据点坐标
        var points = new List<Point>();
        for (int i = 0; i < items.Count; i++)
        {
            double x = padLeft + (items.Count == 1 ? chartW / 2 : chartW * i / (items.Count - 1));
            double y = padTop + chartH * (1 - (double)((values[i] - yMin) / yRange));
            points.Add(new Point(x, y));
        }

        // 绘制折线
        if (points.Count >= 2)
        {
            var polyline = new Polyline
            {
                Points = new PointCollection(points),
                Stroke = LineColor,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };
            ChartCanvas.Children.Add(polyline);
        }

        // 绘制数据点和X轴标签
        for (int i = 0; i < points.Count; i++)
        {
            // 数据点
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = Brushes.White,
                Stroke = LineColor,
                StrokeThickness = 2
            };
            Canvas.SetLeft(dot, points[i].X - 3);
            Canvas.SetTop(dot, points[i].Y - 3);
            ChartCanvas.Children.Add(dot);

            // 数值标签（在点上方）
            var valLabel = new TextBlock
            {
                Text = values[i].ToString(ValueFormat, CultureInfo.InvariantCulture),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = LineColor
            };
            Canvas.SetLeft(valLabel, points[i].X - 10);
            Canvas.SetTop(valLabel, points[i].Y - 18);
            ChartCanvas.Children.Add(valLabel);

            // X轴标签
            string? labelText = LabelSelector?.Invoke(items[i]);
            if (!string.IsNullOrEmpty(labelText))
            {
                var xLabel = new TextBlock
                {
                    Text = labelText,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(xLabel, points[i].X - 14);
                Canvas.SetTop(xLabel, padTop + chartH + 4);
                ChartCanvas.Children.Add(xLabel);
            }
        }
    }
}
