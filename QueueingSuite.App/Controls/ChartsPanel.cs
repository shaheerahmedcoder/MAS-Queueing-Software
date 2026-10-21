using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using QueueingSuite.Core;
using QueueingSuite.Core.Models;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.App.Controls;

/// <summary>
/// "Charts" tab (OxyPlot): (a) Lq vs ρ with the operating point, (b) cumulative average Wq vs customer index,
/// (c) number in queue over time, (d) waiting-time histogram, (e) per-server utilization.
/// </summary>
internal sealed class ChartsPanel : UserControl
{
    private static readonly OxyColor Blue = OxyColor.FromRgb(79, 102, 240);
    private static readonly OxyColor Orange = OxyColor.FromRgb(232, 89, 12);
    private static readonly OxyColor Green = OxyColor.FromRgb(24, 140, 90);
    private static readonly OxyColor Grey = OxyColor.FromRgb(120, 128, 145);

    private sealed class Host : Panel
    {
        public PlotView View { get; } = new() { Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly Label message = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, Font = Theme.Font(11), BackColor = Color.White };
        public Host()
        {
            Dock = DockStyle.Fill;
            Controls.Add(View);
            Controls.Add(message);
            message.BringToFront();
        }
        public void Show(PlotModel? model, string emptyText)
        {
            View.Model = model;
            message.Text = emptyText;
            message.Visible = model is null;
        }
    }

    private readonly Host lqHost = new(), convHost = new(), queueHost = new(), histHost = new(), utilHost = new();
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill, Font = Theme.Font(9.5f), Padding = new Point(12, 4) };

    /// <summary>Shows chart number 0..4 (used by the headless snapshot mode).</summary>
    public void SelectChart(int index) => tabs.SelectedIndex = Math.Clamp(index, 0, tabs.TabPages.Count - 1);

    public ChartsPanel()
    {
        BackColor = Theme.Background;
        void AddTab(string title, Host h)
        {
            var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(4) };
            page.Controls.Add(h);
            tabs.TabPages.Add(page);
        }
        AddTab("Lq vs ρ", lqHost);
        AddTab("Convergence of Wq", convHost);
        AddTab("Queue length over time", queueHost);
        AddTab("Waiting-time histogram", histHost);
        AddTab("Server utilization", utilHost);
        Controls.Add(tabs);
        Clear();
    }

    public void Clear()
    {
        const string none = "Press Calculate / Run to draw this chart.";
        lqHost.Show(null, none);
        convHost.Show(null, none);
        queueHost.Show(null, none);
        histHost.Show(null, none);
        utilHost.Show(null, none);
    }

    public void ShowRun(RunOutput run)
    {
        const string needSim = "This chart comes from a simulation.\nSwitch to Simulation or Compare mode and press Run.";
        lqHost.Show(BuildLqVsRho(run), "");
        var sim = run.Simulation?.First;
        convHost.Show(sim is null ? null : BuildConvergence(run, sim), needSim);
        queueHost.Show(sim is null ? null : BuildQueueLength(sim), needSim);
        histHost.Show(sim is null ? null : BuildHistogram(sim), needSim);
        utilHost.Show(sim is null ? null : BuildUtilization(run, sim), needSim);
    }

    // ----------------------------------------------------------------- model helpers
    private static PlotModel NewModel(string title, string? subtitle = null) => new()
    {
        Title = title, Subtitle = subtitle, TitleFontSize = 14, SubtitleFontSize = 10, DefaultFont = Theme.FontName,
        TextColor = OxyColor.FromRgb(30, 41, 65), PlotAreaBorderColor = OxyColor.FromRgb(200, 205, 220), Background = OxyColors.White
    };

    private static LinearAxis Axis(AxisPosition pos, string title, double? min = null, double? max = null)
    {
        var a = new LinearAxis
        {
            Position = pos, Title = title, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = OxyColor.FromRgb(236, 239, 247),
            MinorGridlineStyle = LineStyle.None
        };
        if (min is { } lo) a.Minimum = lo;
        if (max is { } hi) a.Maximum = hi;
        return a;
    }

    private static void AddLegend(PlotModel m) =>
        m.Legends.Add(new Legend { LegendPosition = LegendPosition.TopRight, LegendBackground = OxyColor.FromAColor(220, OxyColors.White), LegendBorder = OxyColor.FromRgb(210, 215, 230) });

    // ----------------------------------------------------------------- (a) Lq vs rho
    private static PlotModel BuildLqVsRho(RunOutput run)
    {
        var a = run.Analytical;
        var m = NewModel($"Lq versus ρ – {run.Inputs.Model.Label()}", a.IsExact ? "exact formula curve" : "Kingman approximation curve");
        m.Axes.Add(Axis(AxisPosition.Bottom, "Traffic intensity ρ", 0, 1));
        m.Axes.Add(Axis(AxisPosition.Left, "Lq  (mean number waiting)", 0));
        var line = new LineSeries { Title = "Formula Lq(ρ)", Color = Blue, StrokeThickness = 2.4 };
        foreach (var (rho, lq) in QueueRunner.LqVsRho(run.Inputs, run.Resolved, 90, 0.97))
            line.Points.Add(new DataPoint(rho, lq));
        m.Series.Add(line);

        double curveMax = line.Points.Count > 0 ? line.Points.Max(p => p.Y) : 10;
        // zoom the y-axis to the operating region so the marker is readable (the curve is clipped; it grows without bound near ρ = 1)
        double ymax = a.IsStable && a.Lq is { } opLq ? Math.Min(curveMax, Math.Max(4, Math.Ceiling(opLq * 3.5))) : Math.Min(curveMax, 12);
        m.Axes[1].Maximum = ymax;
        m.Subtitle = (a.IsExact ? "exact formula curve" : "Kingman approximation curve") + " – y-axis zoomed to the operating region";
        if (a.IsStable && a.Lq is { } lqOp)
        {
            m.Series.Add(new ScatterSeries
            {
                Title = $"Operating point (ρ = {N(a.Rho)}, Lq = {N(lqOp)})", MarkerType = MarkerType.Circle, MarkerSize = 8,
                MarkerFill = Orange, MarkerStroke = OxyColors.White, MarkerStrokeThickness = 1.5,
                Points = { new ScatterPoint(a.Rho, lqOp) }
            });
            m.Annotations.Add(new LineAnnotation { Type = LineAnnotationType.Vertical, X = a.Rho, Color = OxyColor.FromAColor(120, Orange), LineStyle = LineStyle.Dash, StrokeThickness = 1.2 });
        }
        else
        {
            m.Annotations.Add(new TextAnnotation
            {
                Text = $"ρ = {N(a.Rho)} ≥ 1: unstable – the operating point is off the chart (Lq grows without bound).",
                TextPosition = new DataPoint(0.5, ymax * 0.55), TextColor = OxyColor.FromRgb(160, 30, 30), FontSize = 12, StrokeThickness = 0, Background = OxyColors.Transparent
            });
        }
        if (run.Simulation is { } s && s.Utilization.Mean < 1)
        {
            m.Series.Add(new ScatterSeries
            {
                Title = $"Simulated (ρ̂ = {s.Utilization.Mean:F3}, Lq = {s.Lq.Mean:F3})", MarkerType = MarkerType.Diamond, MarkerSize = 8,
                MarkerFill = OxyColors.Transparent, MarkerStroke = Green, MarkerStrokeThickness = 2,
                Points = { new ScatterPoint(s.Utilization.Mean, s.Lq.Mean) }
            });
        }
        AddLegend(m);
        return m;
    }

    // ----------------------------------------------------------------- (b) convergence
    private static PlotModel BuildConvergence(RunOutput run, SimulationResult sim)
    {
        var avg = sim.CumulativeAverageWaitSampled(3000);
        var m = NewModel("Cumulative average wait Wq versus customer number", "the running mean settles on the long-run value as more customers are served");
        m.Axes.Add(Axis(AxisPosition.Bottom, "Customer index (after warm-up)", 1));
        m.Axes.Add(Axis(AxisPosition.Left, "Average wait in queue, Wq", 0));
        var line = new LineSeries { Title = "Simulated running mean", Color = Blue, StrokeThickness = 1.8 };
        foreach (var (index, average) in avg) line.Points.Add(new DataPoint(index, average));
        m.Series.Add(line);
        if (run.Analytical.IsStable && run.Analytical.Wq is { } wq)
        {
            m.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Horizontal, Y = wq, Color = Orange, LineStyle = LineStyle.Dash, StrokeThickness = 2,
                Text = $"Formula Wq = {wq:F4}", TextColor = Orange, TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Right
            });
        }
        else
        {
            m.Subtitle = "no formula line: the system is unstable (ρ ≥ 1), so the average keeps growing with run length";
        }
        AddLegend(m);
        return m;
    }

    // ----------------------------------------------------------------- (c) queue length over time
    private static PlotModel BuildQueueLength(SimulationResult sim)
    {
        int points = Math.Min(2500, sim.QueueTimes.Length);
        var m = NewModel("Number waiting in the queue over time", points < sim.QueueTimes.Length ? $"first {points:N0} changes of a longer run (t = 0 … {sim.QueueTimes[points - 1]:0.##})" : null);
        m.Axes.Add(Axis(AxisPosition.Bottom, "Simulated time", 0));
        m.Axes.Add(Axis(AxisPosition.Left, "Customers waiting", 0));
        var step = new StairStepSeries { Title = "Nq(t)", Color = Blue, StrokeThickness = 1.4, VerticalStrokeThickness = 1.4 };
        for (int i = 0; i < points; i++) step.Points.Add(new DataPoint(sim.QueueTimes[i], sim.QueueValues[i]));
        m.Series.Add(step);
        m.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal, Y = sim.Lq, Color = Orange, LineStyle = LineStyle.Dash, StrokeThickness = 1.5,
            Text = $"time-average Lq = {sim.Lq:F3}", TextColor = Orange, TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Right
        });
        return m;
    }

    // ----------------------------------------------------------------- (d) histogram
    private static PlotModel BuildHistogram(SimulationResult sim)
    {
        var h = sim.BuildWaitHistogram(30);
        if (h.Total == 0) return NewModel("Waiting-time histogram", "no customers");
        var m = NewModel("Waiting-time histogram (time spent in the queue)", $"{h.ZeroShare:P1} of customers did not wait at all; bars end at the 99.5th percentile ({h.Cut:0.###})");
        m.Axes.Add(Axis(AxisPosition.Bottom, "Wait in queue", 0));
        m.Axes.Add(Axis(AxisPosition.Left, "Number of customers", 0));
        if (h.Cut <= 1e-12)
        {
            m.Subtitle = "every customer was served immediately (no queue formed)";
            return m;
        }
        var hist = new HistogramSeries { FillColor = OxyColor.FromAColor(200, Blue), StrokeColor = OxyColors.White, StrokeThickness = 1 };
        double width = h.BinWidth;
        for (int i = 0; i < h.Counts.Length; i++)
            hist.Items.Add(new HistogramItem(i * width, (i + 1) * width, h.Counts[i] * width, h.Counts[i]));   // height = area/width = count
        m.Series.Add(hist);
        return m;
    }

    // ----------------------------------------------------------------- (e) per-server utilization
    private static PlotModel BuildUtilization(RunOutput run, SimulationResult sim)
    {
        var m = NewModel("Utilization of each server", sim.Servers > 1 ? "busy time / observed time per counter (the lowest-numbered idle server is used first)" : "single server");
        var labels = sim.ServerUtilization.Select((u, k) => $"S{k + 1} ({u * 100:0.0}%)").ToList();
        BarCharts.AddGroupedBars(m, labels, new[] { new BarCharts.BarSet("Simulated", Blue, sim.ServerUtilization.Select(u => u * 100).ToList()) }, "Utilization (%)", 100, 20);
        if (run.Analytical.IsStable)
        {
            m.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Horizontal, Y = run.Analytical.Rho * 100, Color = Orange, LineStyle = LineStyle.Dash, StrokeThickness = 2,
                Text = $"Formula ρ = {run.Analytical.Rho * 100:F1}%", TextColor = Orange, TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Right
            });
        }
        AddLegend(m);
        return m;
    }
}
