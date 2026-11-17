using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SupernovaBench.App.Controls;

/// <summary>Série do gráfico: buffer circular de valores (NaN = sem leitura).</summary>
public sealed class ChartSeries
{
    private readonly double[] _values;

    public ChartSeries(string name, Color color, int length)
    {
        Name = name;
        Color = color;
        _values = Enumerable.Repeat(double.NaN, length).ToArray();
        Stroke = new SolidColorBrush(color);
        Stroke.Freeze();
    }

    public string Name { get; }
    public Color Color { get; }
    public SolidColorBrush Stroke { get; }
    public IReadOnlyList<double> Values => _values;

    public void Push(float? value)
    {
        Array.Copy(_values, 1, _values, 0, _values.Length - 1);
        _values[^1] = value is { } v ? v : double.NaN;
    }
}

/// <summary>Gráfico de linhas leve (desenhado direto no OnRender), usado no monitor ao vivo.</summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IEnumerable<ChartSeries>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(int), typeof(LineChart),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(LineChart),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(LineChart),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<ChartSeries>? Series { get => (IEnumerable<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public int Revision { get => (int)GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        const double labelWidth = 30;
        double w = ActualWidth - labelWidth, h = ActualHeight;
        if (w <= 10 || h <= 10) return;

        var series = Series?.ToList() ?? new List<ChartSeries>();
        double min = Minimum, max = Maximum;
        foreach (var s in series)
            foreach (var v in s.Values)
                if (!double.IsNaN(v)) { min = Math.Min(min, Math.Floor(v / 10) * 10); max = Math.Max(max, Math.Ceiling(v / 10) * 10); }
        if (max - min < 1) max = min + 1;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var gridPen = new Pen(GridBrush, 1) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) };
        gridPen.Freeze();
        const int lines = 4;
        for (int i = 0; i <= lines; i++)
        {
            double y = Math.Round(h * i / lines) + 0.5;
            dc.DrawLine(gridPen, new Point(labelWidth, y), new Point(labelWidth + w, y));
            double value = max - (max - min) * i / lines;
            var text = new FormattedText(value.ToString("0", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
                                         FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, LabelBrush, dpi);
            dc.DrawText(text, new Point(0, Math.Clamp(y - text.Height / 2, 0, h - text.Height)));
        }

        foreach (var s in series)
        {
            var values = s.Values;
            int n = values.Count;
            if (n < 2) continue;
            double step = w / (n - 1);
            Point P(int i) => new(labelWidth + i * step, h - (values[i] - min) / (max - min) * h);

            // Linha (com quebras onde não houve leitura) e área preenchida sob ela.
            var line = new StreamGeometry();
            var area = new StreamGeometry();
            using (var lg = line.Open())
            using (var ag = area.Open())
            {
                int i = 0;
                while (i < n)
                {
                    while (i < n && double.IsNaN(values[i])) i++;
                    if (i >= n) break;
                    int start = i;
                    lg.BeginFigure(P(i), false, false);
                    ag.BeginFigure(new Point(P(i).X, h), true, true);
                    ag.LineTo(P(i), false, false);
                    i++;
                    while (i < n && !double.IsNaN(values[i]))
                    {
                        lg.LineTo(P(i), true, true);
                        ag.LineTo(P(i), false, false);
                        i++;
                    }
                    ag.LineTo(new Point(P(i - 1).X, h), false, false);
                    if (i - start == 1) lg.LineTo(new Point(P(start).X + 1, P(start).Y), true, false);
                }
            }
            line.Freeze();
            area.Freeze();

            var fill = new LinearGradientBrush(Color.FromArgb(90, s.Color.R, s.Color.G, s.Color.B),
                                               Color.FromArgb(0, s.Color.R, s.Color.G, s.Color.B), 90);
            fill.Freeze();
            dc.DrawGeometry(fill, null, area);
            var pen = new Pen(s.Stroke, 2) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, line);
        }
    }
}
