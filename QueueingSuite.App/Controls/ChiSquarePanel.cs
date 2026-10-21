using System.Globalization;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using QueueingSuite.Core;
using QueueingSuite.Core.Analytical;
using QueueingSuite.Core.Data;
using QueueingSuite.Core.Models;
using QueueingSuite.Core.Statistics;

namespace QueueingSuite.App.Controls;

/// <summary>
/// "Chi-square" tab: generator validity, data fit (KFC CSV vs Exponential) and simulation vs theory P(n).
/// Shows the verdict, the observed / expected / contribution table and an observed-vs-expected bar chart.
/// </summary>
internal sealed class ChiSquarePanel : UserControl
{
    private readonly ComboBox testBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 410, Font = Theme.Font(9.5f) };
    private readonly ComboBox sessionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Font = Theme.Font(9.5f) };
    private readonly TextBox alphaBox = new() { Text = "0.05", Width = 52 };
    private readonly TextBox binsBox = new() { Text = "auto", Width = 52 };
    private readonly TextBox spacingBox = new() { Text = "auto", Width = 52 };
    private readonly Button runButton = Theme.Button("Run test", Theme.Accent, Color.White, 100, 30);
    private FlowLayoutPanel sessionPair = null!, binsPair = null!, spacingPair = null!;
    private readonly Label verdict = new() { Dock = DockStyle.Top, Height = 58, Padding = new Padding(12, 8, 12, 4), Font = Theme.Font(10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label stats = new() { Dock = DockStyle.Top, Height = 28, Padding = new Padding(12, 4, 12, 0), Font = Theme.Font(9.5f), ForeColor = Theme.Ink };
    private readonly TextBox notes = new() { Dock = DockStyle.Bottom, Height = 92, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Background, ForeColor = Theme.Muted, Font = Theme.Font(9f), ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill };
    private readonly PlotView plot = new() { Dock = DockStyle.Fill, BackColor = Color.White };

    private RunOutput? run;
    private AppData? data;

    /// <summary>A caption and its input kept together so show/hide never reorders them.</summary>
    private static FlowLayoutPanel Pair(string caption, Control input)
    {
        var p = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0), BackColor = Color.Transparent };
        p.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 8, 4, 0) });
        input.Margin = new Padding(0, 3, 0, 0);
        p.Controls.Add(input);
        return p;
    }

    public ChiSquarePanel()
    {
        BackColor = Theme.Background;
        testBox.Items.AddRange(new object[]
        {
            "1. Generator validity – inter-arrival times (simulation)",
            "2. Generator validity – service times (simulation)",
            "3. Data fit – KFC inter-arrival times ~ Exponential",
            "4. Data fit – KFC service times ~ Exponential",
            "5. Simulation vs theory – time-in-state P(n)  (M/M/1, M/M/c)"
        });
        testBox.SelectedIndex = 0;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(4, 6, 4, 0), BackColor = Theme.Background, WrapContents = false };
        runButton.Margin = new Padding(12, 0, 0, 0);
        sessionPair = Pair("Data session:", sessionBox);
        binsPair = Pair("Bins:", binsBox);
        spacingPair = Pair("Spacing:", spacingBox);
        bar.Controls.AddRange(new Control[] { Pair("Test:", testBox), sessionPair, Pair("α:", alphaBox), binsPair, spacingPair, runButton });
        runButton.Margin = new Padding(14, 1, 0, 0);

        Theme.StyleGrid(grid);
        grid.Columns.Add("bin", "Bin / category");
        grid.Columns.Add("obs", "Observed O");
        grid.Columns.Add("exp", "Expected E");
        grid.Columns.Add("contrib", "(O−E)²/E");
        foreach (DataGridViewColumn c in grid.Columns) { c.SortMode = DataGridViewColumnSortMode.NotSortable; if (c.Index > 0) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }

        var leftCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(6) };
        leftCard.Controls.Add(grid);
        var rightCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        rightCard.Controls.Add(plot);
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background, Padding = new Padding(0, 6, 0, 0) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        leftCard.Margin = new Padding(0, 0, 8, 0);
        split.Controls.Add(leftCard, 0, 0);
        split.Controls.Add(rightCard, 1, 0);

        Controls.Add(split);
        Controls.Add(notes);
        Controls.Add(stats);
        Controls.Add(verdict);
        Controls.Add(bar);

        testBox.SelectedIndexChanged += (_, _) => UpdateControlVisibility();
        runButton.Click += (_, _) => RunTest();
        UpdateControlVisibility();
        Reset("Choose a test and press “Run test”. Tests 1, 2 and 5 need a simulation run first (Simulation or Compare mode).");
    }

    public void SetData(AppData appData)
    {
        data = appData;
        appData.Changed += (_, _) => FillSessions();
        FillSessions();
    }

    /// <summary>The latest simulation run (null in Analytical mode or before the first run).</summary>
    public void SetRun(RunOutput? output) => run = output;

    /// <summary>Selects a test by 1-based number and runs it (used by the headless snapshot mode).</summary>
    public void RunTestNumber(int number)
    {
        testBox.SelectedIndex = Math.Clamp(number - 1, 0, testBox.Items.Count - 1);
        RunTest();
    }

    private void FillSessions()
    {
        int keep = Math.Max(0, sessionBox.SelectedIndex);
        sessionBox.Items.Clear();
        if (data is not null) foreach (var s in data.Sessions) sessionBox.Items.Add(s.Name);
        if (sessionBox.Items.Count > 0) sessionBox.SelectedIndex = Math.Min(keep == 0 ? sessionBox.Items.Count - 1 : keep, sessionBox.Items.Count - 1);
    }

    private void UpdateControlVisibility()
    {
        int t = testBox.SelectedIndex;
        bool dataTest = t is 2 or 3;
        sessionPair.Visible = dataTest;
        binsPair.Visible = t is 0 or 1;
        spacingPair.Visible = t == 4;
    }

    private void Reset(string message)
    {
        grid.Rows.Clear();
        plot.Model = null;
        verdict.Text = message;
        verdict.BackColor = Theme.Background;
        verdict.ForeColor = Theme.Muted;
        stats.Text = "";
        notes.Text = "";
    }

    // ================================================================= running a test
    private void RunTest()
    {
        try
        {
            if (!double.TryParse(alphaBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha) &&
                !double.TryParse(alphaBox.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out alpha) || alpha <= 0 || alpha >= 1)
            {
                Reset("Significance level α must be a number between 0 and 1 (for example 0.05).");
                return;
            }
            int? bins = null;
            if (binsPair.Visible && !string.Equals(binsBox.Text.Trim(), "auto", StringComparison.OrdinalIgnoreCase) && binsBox.Text.Trim().Length > 0)
            {
                if (!int.TryParse(binsBox.Text.Trim(), out int b) || b < 2 || b > 500) { Reset("Bins must be 'auto' or a whole number between 2 and 500."); return; }
                bins = b;
            }

            ChiSquareResult? result = null;
            switch (testBox.SelectedIndex)
            {
                case 0:
                case 1:
                {
                    if (run?.Simulation is null) { Reset("No simulation yet. Switch to Simulation or Compare mode and press Run, then come back."); return; }
                    bool arrivals = testBox.SelectedIndex == 0;
                    var dist = arrivals ? run.Resolved.Arrival : run.Resolved.Service;
                    var sim = run.Simulation.First;
                    if (run.Inputs.UsesKfcData)
                    {
                        Reset("In KFC data mode the simulation replays recorded values, so there is no random generator to validate. Use test 3 or 4 to test the data itself.");
                        return;
                    }
                    var sample = arrivals ? sim.InterArrivalTimes() : sim.ServiceTimes();
                    result = SimulationValidator.GeneratorValidity(sample, dist, arrivals ? "inter-arrival times" : "service times", bins, alpha);
                    break;
                }
                case 2:
                case 3:
                {
                    var session = data is not null && sessionBox.SelectedIndex >= 0 ? data.Sessions[sessionBox.SelectedIndex] : null;
                    if (session is null) { Reset("No CSV data loaded. Open it on the KFC Data tab."); return; }
                    bool arrivals = testBox.SelectedIndex == 2;
                    var values = arrivals ? session.InterArrivals : session.Services;
                    result = SimulationValidator.DataFitExponential(values, $"{(arrivals ? "inter-arrival times" : "service times")} of {session.Name}", alpha);
                    break;
                }
                default:
                {
                    if (run?.Simulation is null) { Reset("No simulation yet. Switch to Simulation or Compare mode and press Run, then come back."); return; }
                    double? spacing = null;
                    if (!string.Equals(spacingBox.Text.Trim(), "auto", StringComparison.OrdinalIgnoreCase) && spacingBox.Text.Trim().Length > 0)
                    {
                        if (!double.TryParse(spacingBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double sp) || sp < 1)
                        { Reset("Spacing must be 'auto' or a number ≥ 1."); return; }
                        spacing = sp;
                    }
                    result = SimulationValidator.StateDistribution(run.Simulation.First, run.Analytical, spacing, alpha);
                    break;
                }
            }
            Show(result);
        }
        catch (Exception ex)
        {
            Reset("The test could not be run: " + ex.Message);
        }
    }

    private void Show(ChiSquareResult r)
    {
        grid.Rows.Clear();
        foreach (var b in r.Bins)
            grid.Rows.Add(b.Label, b.Observed.ToString("0.##", CultureInfo.InvariantCulture), b.Expected.ToString("0.00", CultureInfo.InvariantCulture), b.Contribution.ToString("0.0000", CultureInfo.InvariantCulture));
        if (r.IsValid)
        {
            int total = grid.Rows.Add("TOTAL", r.Bins.Sum(b => b.Observed).ToString("0.##", CultureInfo.InvariantCulture), r.Bins.Sum(b => b.Expected).ToString("0.00", CultureInfo.InvariantCulture), r.Statistic.ToString("0.0000", CultureInfo.InvariantCulture));
            foreach (DataGridViewCell c in grid.Rows[total].Cells) c.Style.Font = Theme.Font(9.5f, FontStyle.Bold);
        }

        verdict.Text = r.Verdict;
        if (!r.IsValid) { verdict.BackColor = Theme.WarnFill; verdict.ForeColor = Theme.WarnInk; }
        else if (r.Reject) { verdict.BackColor = Theme.BadFill; verdict.ForeColor = Theme.BadInk; }
        else { verdict.BackColor = Theme.GoodFill; verdict.ForeColor = Theme.GoodInk; }
        verdict.Height = Math.Max(58, 18 * (1 + r.Verdict.Length / 120) + 22);

        stats.Text = r.IsValid
            ? $"χ² = {r.Statistic:0.####}     df = {r.Bins.Count} − 1 − {r.ParametersEstimated} = {r.DegreesOfFreedom}     critical (α = {r.Alpha}) = {r.Critical:0.####}     p-value = {(r.PValue < 0.0001 ? "<0.0001" : r.PValue.ToString("0.####", CultureInfo.InvariantCulture))}     n = {r.SampleSize:0.#}"
            : "";
        notes.Text = r.Hypothesis + "\r\n" + string.Join("\r\n", r.Notes.Select(n => "• " + n)) +
            "\r\n• Rule: reject H0 when χ² exceeds the critical value (equivalently p < α). “Fail to reject” does not prove the distribution is right – it only finds no evidence against it.";
        plot.Model = r.Bins.Count > 0 ? BuildPlot(r) : null;
    }

    private static PlotModel BuildPlot(ChiSquareResult r)
    {
        var m = new PlotModel
        {
            Title = r.Title, TitleFontSize = 12, DefaultFont = Theme.FontName, TextColor = OxyColor.FromRgb(30, 41, 65),
            PlotAreaBorderColor = OxyColor.FromRgb(200, 205, 220), Background = OxyColors.White,
            Subtitle = "observed vs expected counts per bin", SubtitleFontSize = 9
        };
        BarCharts.AddGroupedBars(m, r.Bins.Select(b => b.Label).ToList(), new[]
        {
            new BarCharts.BarSet("Observed", OxyColor.FromRgb(79, 102, 240), r.Bins.Select(b => b.Observed).ToList()),
            new BarCharts.BarSet("Expected", OxyColor.FromRgb(232, 140, 70), r.Bins.Select(b => b.Expected).ToList())
        }, "Count");
        m.Legends.Add(new Legend { LegendPosition = LegendPosition.TopRight });
        return m;
    }
}
