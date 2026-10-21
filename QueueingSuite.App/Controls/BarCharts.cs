using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace QueueingSuite.App.Controls;

/// <summary>
/// Vertical grouped bars with text category labels, built from <see cref="RectangleBarSeries"/> (OxyPlot 2.1's BarSeries
/// only draws horizontal bars). Category i is centred on x = i; series are drawn side by side inside the category.
/// </summary>
internal static class BarCharts
{
    public sealed record BarSet(string Title, OxyColor Fill, IReadOnlyList<double> Values);

    public static void AddGroupedBars(PlotModel model, IReadOnlyList<string> labels, IReadOnlyList<BarSet> sets, string yTitle, double? yMax = null, int maxTicks = 14)
    {
        int n = labels.Count;
        int step = Math.Max(1, (int)Math.Ceiling(n / (double)maxTicks));
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom, Minimum = -0.6, Maximum = n - 0.4, MajorStep = step, MinorStep = step,
            MajorGridlineStyle = LineStyle.None, MinorGridlineStyle = LineStyle.None, FontSize = 9, Angle = n > 14 ? 45 : 0,
            LabelFormatter = v =>
            {
                int i = (int)Math.Round(v);
                return Math.Abs(v - i) < 1e-6 && i >= 0 && i < n && i % step == 0 ? labels[i] : "";
            }
        });
        var y = new LinearAxis
        {
            Position = AxisPosition.Left, Title = yTitle, Minimum = 0, MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromRgb(236, 239, 247), MinorGridlineStyle = LineStyle.None
        };
        if (yMax is { } m) y.Maximum = m;
        model.Axes.Add(y);

        double groupWidth = n <= 4 ? 0.45 : 0.8, w = groupWidth / sets.Count;
        for (int s = 0; s < sets.Count; s++)
        {
            var series = new RectangleBarSeries { Title = sets[s].Title, FillColor = sets[s].Fill, StrokeColor = OxyColors.White, StrokeThickness = 0.8 };
            for (int i = 0; i < n; i++)
            {
                double x0 = i - groupWidth / 2 + s * w;
                series.Items.Add(new RectangleBarItem(x0, 0, x0 + w, sets[s].Values[i]));
            }
            model.Series.Add(series);
        }
    }
}
