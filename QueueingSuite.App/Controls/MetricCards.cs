using System.Globalization;
using QueueingSuite.Core;
using QueueingSuite.Core.Models;
using QueueingSuite.Core.Statistics;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.App.Controls;

/// <summary>
/// The six white metric cards (Utilization %, L, Lq, W, Wq, Customers Served) plus a one-line strip with ρ, P0, P(wait)
/// and an exact / approximation / unstable badge. Which numbers appear depends on the mode:
/// Analytical = formulas, Simulation = simulated (± 95% CI when replicated), Compare = simulated with the formula underneath.
/// </summary>
internal sealed class MetricCards : Panel
{
    private sealed class Card : Panel
    {
        public Label Title { get; } = new() { Location = new Point(12, 8), Size = new Size(150, 34), ForeColor = Theme.Muted, Font = Theme.Font(9f) };
        public Label Value { get; } = new() { Location = new Point(12, 40), Size = new Size(150, 36), ForeColor = Theme.MetricBlue, Font = Theme.Font(19, FontStyle.Bold), Text = "—" };
        public Label Sub { get; } = new() { Location = new Point(12, 80), Size = new Size(150, 36), ForeColor = Theme.Muted, Font = Theme.Font(8.5f) };
        public Card(string title)
        {
            BackColor = Theme.Card;
            BorderStyle = BorderStyle.FixedSingle;
            Margin = new Padding(0, 0, 8, 0);
            Dock = DockStyle.Fill;
            Title.Text = title;
            Controls.AddRange(new Control[] { Title, Value, Sub });
            Resize += (_, _) =>
            {
                int w = Math.Max(60, ClientSize.Width - 20);
                Title.Width = Value.Width = Sub.Width = w;
            };
        }
    }

    private static readonly string[] Titles =
    {
        "Utilization (%)", "Customers in System (L)", "Customers in Queue (Lq)",
        "Time in System (W)", "Queue Wait (Wq)", "Customers Served"
    };

    private readonly Card[] cards = new Card[6];
    private readonly Label badge = new() { AutoSize = false, Size = new Size(190, 24), TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Font(9f, FontStyle.Bold) };
    private readonly Label strip = new() { AutoSize = false, Height = 24, ForeColor = Theme.Ink, Font = Theme.Font(9.5f), TextAlign = ContentAlignment.MiddleLeft };

    public MetricCards()
    {
        Height = 154;
        BackColor = Theme.Background;
        var table = new TableLayoutPanel { Dock = DockStyle.Top, Height = 122, ColumnCount = 6, RowCount = 1, BackColor = Theme.Background, Margin = Padding.Empty };
        for (int i = 0; i < 6; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
            cards[i] = new Card(Titles[i]);
            table.Controls.Add(cards[i], i, 0);
        }
        var row = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(0, 6, 0, 0) };
        badge.Location = new Point(0, 8);
        strip.Location = new Point(200, 8);
        strip.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        strip.Width = 700;
        row.Controls.Add(badge);
        row.Controls.Add(strip);
        row.Resize += (_, _) => strip.Width = Math.Max(200, row.ClientSize.Width - 204);
        Controls.Add(row);
        Controls.Add(table);
        Clear("Ready. Choose a model, type the rates and press the blue button.");
    }

    public void Clear(string message)
    {
        foreach (var c in cards) { c.Value.Text = "—"; c.Value.Font = Theme.Font(19, FontStyle.Bold); c.Sub.Text = ""; }
        SetBadge("", Theme.Muted, Theme.Background);
        strip.Text = message;
    }

    public void ShowError(string message)
    {
        foreach (var c in cards) { c.Value.Text = "—"; c.Value.Font = Theme.Font(19, FontStyle.Bold); c.Sub.Text = ""; }
        SetBadge("Input error", Theme.BadInk, Theme.BadFill);
        strip.Text = message;
    }

    public void ShowRun(RunOutput run)
    {
        var a = run.Analytical;
        var sim = run.Simulation;
        var mode = run.Mode;
        var cmp = run.Comparison;

        // ---- values ----
        var vals = new (string? Analytical, string? Simulated, string? Ci)[6];
        vals[0] = (a.IsStable ? Pct(a.Rho) : null, sim is null ? null : Pct(sim.Utilization.Mean), CiText(sim?.Utilization, 100, 1));
        vals[1] = (Num(a.L, 2), sim is null ? null : sim.L.Mean.ToString("F2", CultureInfo.InvariantCulture), CiText(sim?.L, 1, 2));
        vals[2] = (Num(a.Lq, 2), sim is null ? null : sim.Lq.Mean.ToString("F2", CultureInfo.InvariantCulture), CiText(sim?.Lq, 1, 2));
        vals[3] = (Num(a.W, 3), sim is null ? null : sim.W.Mean.ToString("F3", CultureInfo.InvariantCulture), CiText(sim?.W, 1, 3));
        vals[4] = (Num(a.Wq, 3), sim is null ? null : sim.Wq.Mean.ToString("F3", CultureInfo.InvariantCulture), CiText(sim?.Wq, 1, 3));
        vals[5] = (null, sim is null ? null : sim.CustomersServed.Mean.ToString("N0", CultureInfo.CurrentCulture), null);

        string?[] errKeys = { "Utilization", "L", "Lq", "W", "Wq", null };

        for (int i = 0; i < 6; i++)
        {
            var card = cards[i];
            card.Value.Font = Theme.Font(19, FontStyle.Bold);
            switch (mode)
            {
                case RunMode.Analytical:
                    card.Value.Text = vals[i].Analytical ?? (i == 5 ? "—" : (a.IsStable ? "—" : "unstable"));
                    if (card.Value.Text == "unstable") card.Value.Font = Theme.Font(14, FontStyle.Bold);
                    card.Sub.Text = i == 5 ? "formulas: no customers simulated" : a.IsExact ? "exact formula" : "approximation";
                    break;
                case RunMode.Simulation:
                    card.Value.Text = vals[i].Simulated ?? "—";
                    card.Sub.Text = vals[i].Ci ?? (i == 5 ? "after warm-up" : "");
                    break;
                default: // Compare
                    card.Value.Text = vals[i].Simulated ?? "—";
                    if (i == 5) { card.Sub.Text = "after warm-up"; break; }
                    string an = vals[i].Analytical is { } t ? t : "unstable";
                    string err = errKeys[i] is { } key && cmp?.Row(key)?.PercentError is { } pe ? $"  ({pe:+0.0;-0.0}%)" : "";
                    card.Sub.Text = $"Formula: {an}{err}" + (vals[i].Ci is { } ci ? $"\n{ci}" : "");
                    break;
            }
        }

        // ---- badge + strip ----
        if (!a.IsStable)
            SetBadge("UNSTABLE  (ρ ≥ 1)", Theme.BadInk, Theme.BadFill);
        else if (a.IsExact)
            SetBadge(a.Method.ToUpperInvariant(), Theme.GoodInk, Theme.GoodFill);
        else
            SetBadge("APPROXIMATION (Kingman)", Theme.WarnInk, Theme.WarnFill);

        var parts = new List<string>
        {
            $"{run.Inputs.Model.Label()}" + (run.Resolved.Servers > 1 ? $" (c = {run.Resolved.Servers})" : ""),
            $"ρ = {N(run.Resolved.Rho)}"
        };
        if (a.IsStable)
        {
            parts.Add($"P0 = {F(a.P0, 4)}");
            if (a.PWait is not null) parts.Add($"P(wait) = {F(a.PWait, 4)}");
        }
        else parts.Add("formulas have no steady state");
        if (sim is not null)
        {
            if (!run.Inputs.UsesKfcData) parts.Add($"seed {sim.BaseSeed}");
            if (sim.Replications > 1) parts.Add($"{sim.Replications} replications, mean ± 95% CI");
            if (run.Inputs.UsesKfcData) parts.Add($"trace replay of {run.Inputs.Session!.Name}");
        }
        strip.Text = string.Join("   ·   ", parts);
    }

    private void SetBadge(string text, Color ink, Color fill)
    {
        badge.Text = text;
        badge.ForeColor = ink;
        badge.BackColor = fill;
        badge.Visible = text.Length > 0;
    }

    private static string? Num(double? v, int d) => v is null ? null : F(v, d);
    private static string Pct(double rho) => (rho * 100).ToString("F1", CultureInfo.InvariantCulture);

    private static string? CiText(ConfidenceInterval? ci, double scale, int d) =>
        ci is { HasInterval: true } c ? $"95% CI ± {(c.HalfWidth * scale).ToString("F" + d, CultureInfo.InvariantCulture)}" : null;
}
