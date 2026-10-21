using System.Globalization;
using QueueingSuite.Core;
using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.App.Controls;

/// <summary>
/// "Results" tab: the analytical-vs-simulated table (columns shown depend on the mode), a banner for warnings /
/// instability, and the "Formula workings" panel with the typed numbers substituted into every formula.
/// </summary>
internal sealed class ResultsPanel : UserControl
{
    private readonly Label banner = new() { Dock = DockStyle.Top, Height = 0, Padding = new Padding(10, 6, 10, 6), Font = Theme.Font(9.5f), Visible = false };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill };
    private readonly TextBox workings = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White,
        BorderStyle = BorderStyle.None, Font = Theme.Mono(10f), ForeColor = Theme.Ink
    };
    private readonly Label tableTitle = new() { Text = "RESULTS", Font = Theme.Font(11, FontStyle.Bold), ForeColor = Theme.Ink, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
    private readonly Label tableNote = new() { ForeColor = Theme.Muted, Font = Theme.Font(8.5f), Dock = DockStyle.Bottom, Height = 44 };

    public ResultsPanel()
    {
        BackColor = Theme.Background;
        Theme.StyleGrid(grid);
        grid.Columns.Add("measure", "Measure");
        grid.Columns.Add("formula", "Analytical (formula)");
        grid.Columns.Add("sim", "Simulated");
        grid.Columns.Add("err", "% error");
        grid.Columns[0].FillWeight = 26;
        grid.Columns[1].FillWeight = 24;
        grid.Columns[2].FillWeight = 32;
        grid.Columns[3].FillWeight = 18;
        grid.Columns[0].MinimumWidth = 110;
        grid.Columns[3].MinimumWidth = 84;
        foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        grid.DefaultCellStyle.Font = Theme.Font(10f);
        grid.RowTemplate.Height = 30;

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 12, 0), BackColor = Theme.Background };
        var leftCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(8) };
        leftCard.Controls.Add(grid);
        leftCard.Controls.Add(tableNote);
        var leftHead = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Theme.Background };
        leftHead.Controls.Add(tableTitle);
        left.Controls.Add(leftCard);
        left.Controls.Add(leftHead);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var rightCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10) };
        rightCard.Controls.Add(workings);
        var rightHead = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Theme.Background };
        rightHead.Controls.Add(new Label { Text = "FORMULA WORKINGS", Font = Theme.Font(11, FontStyle.Bold), ForeColor = Theme.Ink, AutoSize = true });
        right.Controls.Add(rightCard);
        right.Controls.Add(rightHead);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        split.Controls.Add(left, 0, 0);
        split.Controls.Add(right, 1, 0);

        Controls.Add(split);
        Controls.Add(banner);
        Padding = new Padding(0, 8, 0, 0);
        Clear();
    }

    public void Clear()
    {
        grid.Rows.Clear();
        workings.Text = "Formulas appear here once you press Calculate / Run.\r\n\r\nTry M/M/1 with λ = 4, μ = 6:\r\n  ρ = λ/μ = 4/6 = 0.6667\r\n  Lq = ρ²/(1−ρ) = 1.3333";
        banner.Visible = false;
        banner.Height = 0;
    }

    /// <summary>Marks the table as belonging to the previous valid inputs while the current inputs are invalid.</summary>
    public void ShowStale(string message)
    {
        ShowBanner(new List<string> { "Current inputs are invalid, so the table below still shows the PREVIOUS valid run.", message }, true);
    }

    public void ShowRun(RunOutput run)
    {
        var a = run.Analytical;
        var mode = run.Mode;
        grid.Columns["formula"]!.Visible = mode != RunMode.Simulation;
        grid.Columns["sim"]!.Visible = mode != RunMode.Analytical;
        grid.Columns["err"]!.Visible = mode == RunMode.Compare;
        grid.Columns["sim"]!.HeaderText = run.Simulation?.Replications > 1 ? "Simulated (mean ± 95% CI)" : run.Inputs.UsesKfcData ? "Trace replay (data)" : "Simulated";

        // ---- table ----
        grid.Rows.Clear();
        var rows = new List<(string Name, double? Formula, double? Sim, double? Half, double? Err, bool Percent)>();
        if (run.Comparison is { } cmp)
        {
            foreach (var r in cmp.Rows)
                rows.Add((MeasureName(r.Metric), r.Analytical, r.Simulated, r.SimHalfWidth, r.PercentError, r.Metric == "Utilization"));
        }
        else
        {
            rows.Add((MeasureName("Utilization"), a.IsStable ? a.Rho : null, null, null, null, true));
            rows.Add((MeasureName("P0"), a.P0, null, null, null, false));
            rows.Add((MeasureName("P(wait)"), a.PWait, null, null, null, false));
            rows.Add((MeasureName("L"), a.L, null, null, null, false));
            rows.Add((MeasureName("Lq"), a.Lq, null, null, null, false));
            rows.Add((MeasureName("W"), a.W, null, null, null, false));
            rows.Add((MeasureName("Wq"), a.Wq, null, null, null, false));
        }
        foreach (var r in rows)
        {
            string formula = r.Formula is { } f ? Value(f, r.Percent) : a.IsStable ? "n/a (no formula)" : "unstable";
            string simText = r.Sim is { } s ? Value(s, r.Percent) + (r.Half is { } h ? $" ± {Value(h, r.Percent)}" : "") : "—";
            string err = r.Err is { } e ? $"{e:+0.00;-0.00}%" : "—";
            int idx = grid.Rows.Add(r.Name, formula, simText, err);
            if (r.Err is { } er)
            {
                var cell = grid.Rows[idx].Cells["err"];
                double abs = Math.Abs(er);
                cell.Style.ForeColor = abs <= 3 ? Theme.GoodInk : abs <= 10 ? Theme.WarnInk : Theme.BadInk;
                cell.Style.Font = Theme.Font(10f, FontStyle.Bold);
            }
            if (!a.IsStable && r.Formula is null)
                grid.Rows[idx].Cells["formula"].Style.ForeColor = Theme.BadInk;
        }
        tableNote.Text = a.IsStable
            ? $"Formulas: {a.Method}. " + (mode == RunMode.Compare ? "% error = (simulated − formula) / formula (green ≤ 3%, amber ≤ 10%). One run of 10,000 customers is noisy by a few %: raise customers or replications to tighten it." : "")
            : "Formulas need ρ < 1. The simulation / trace is still shown (the queue keeps growing, so its averages depend on run length).";

        // ---- banner ----
        var lines = new List<string>();
        if (!a.IsStable && a.Message is not null) lines.Add(a.Message);
        lines.AddRange(run.Warnings.Where(w => a.IsStable || !w.Contains("unstable")));
        if (!a.IsExact && a.IsStable) lines.Add("G/G/1 has no exact formula: Kingman's result is an approximation, so a gap to the simulation is expected.");
        ShowBanner(lines, !a.IsStable);

        // ---- workings ----
        var text = new List<string>();
        if (mode != RunMode.Simulation)
        {
            text.Add($"MODEL {run.Inputs.Model.Label()}  –  {a.Method}");
            text.Add(new string('─', 60));
            text.AddRange(a.Workings.Select(w => w));
        }
        if (mode != RunMode.Analytical && run.Simulation is { } sim)
        {
            if (text.Count > 0) text.Add("");
            text.Add($"SIMULATION  –  {run.Resolved.Arrival.Name}  /  {run.Resolved.Service.Name}");
            text.Add(new string('─', 60));
            text.AddRange(SimulationReport.Workings(sim.First));
            if (sim.Replications > 1)
                text.Add($"Replications: {sim.Replications}; each uses its own seed derived from {sim.BaseSeed}. Shown: mean ± t(0.975, n−1)·s/√n.");
            if (run.Inputs.UsesKfcData)
                text.Add("Trace replay: recorded arrival times and service times are replayed in order (FCFS), no random numbers.");
        }
        workings.Text = string.Join("\r\n", text);
    }

    private void ShowBanner(List<string> lines, bool error)
    {
        if (lines.Count == 0) { banner.Visible = false; banner.Height = 0; return; }
        banner.Text = string.Join("\n", lines);
        banner.BackColor = error ? Theme.BadFill : Theme.WarnFill;
        banner.ForeColor = error ? Theme.BadInk : Theme.WarnInk;
        banner.Height = 12 + 20 * lines.Count + (lines.Count > 1 ? 0 : 6);
        banner.Visible = true;
    }

    private static string MeasureName(string metric) => metric switch
    {
        "Utilization" => "Utilization ρ",
        "P0" => "P0 (empty)",
        "P(wait)" => "P(wait)",
        "L" => "L (in system)",
        "Lq" => "Lq (in queue)",
        "W" => "W (in system)",
        "Wq" => "Wq (in queue)",
        _ => metric
    };

    private static string Value(double v, bool percent) =>
        percent ? (v * 100).ToString("F2", CultureInfo.InvariantCulture) + " %" : v.ToString("F4", CultureInfo.InvariantCulture);
}
