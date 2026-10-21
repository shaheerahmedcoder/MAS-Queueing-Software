using System.Globalization;
using QueueingSuite.Core;
using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;

namespace QueueingSuite.App.Controls;

/// <summary>"Customers" tab: the first N customers of the run (N adjustable) and an Export CSV button for the full trace + results.</summary>
internal sealed class CustomersPanel : UserControl
{
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown countBox = new() { Minimum = 1, Maximum = 20000, Value = 50, Width = 80, Font = Theme.Font(9.5f) };
    private readonly Label info = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(14, 8, 0, 0) };
    private readonly Button exportButton = Theme.Button("Export CSV…", Theme.Accent, Color.White, 120, 30);
    private RunOutput? run;

    public CustomersPanel()
    {
        BackColor = Theme.Background;
        Theme.StyleGrid(grid);
        string[,] cols =
        {
            { "id", "Customer" }, { "arrival", "Arrival" }, { "start", "Service start" }, { "wait", "Wait" },
            { "service", "Service" }, { "end", "Service end" }, { "sys", "Time in system" }, { "server", "Server" }, { "note", "" }
        };
        for (int i = 0; i < cols.GetLength(0); i++)
        {
            grid.Columns.Add(cols[i, 0], cols[i, 1]);
            grid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
            if (i > 0 && i < 7)
            {
                grid.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                grid.Columns[i].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
        }
        grid.Columns["server"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(4, 7, 4, 0), BackColor = Theme.Background, WrapContents = false };
        bar.Controls.Add(new Label { Text = "Show first", AutoSize = true, ForeColor = Theme.Ink, Margin = new Padding(0, 8, 6, 0) });
        bar.Controls.Add(countBox);
        bar.Controls.Add(new Label { Text = "customers", AutoSize = true, ForeColor = Theme.Ink, Margin = new Padding(6, 8, 18, 0) });
        exportButton.Margin = new Padding(0, 1, 0, 0);
        bar.Controls.Add(exportButton);
        bar.Controls.Add(info);

        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(4) };
        card.Controls.Add(grid);
        Controls.Add(card);
        Controls.Add(bar);

        countBox.ValueChanged += (_, _) => Fill();
        exportButton.Click += (_, _) => Export();
        exportButton.Enabled = false;
    }

    public void Clear()
    {
        run = null;
        grid.Rows.Clear();
        info.Text = "Run a simulation to see individual customers.";
        exportButton.Enabled = false;
    }

    public void ShowRun(RunOutput output)
    {
        run = output;
        Fill();
    }

    private void Fill()
    {
        grid.Rows.Clear();
        var sim = run?.Simulation?.First;
        if (sim is null)
        {
            info.Text = run is null ? "Run a simulation to see individual customers." : "Analytical mode has no customers. Run Simulation or Compare.";
            exportButton.Enabled = run is not null;
            return;
        }
        exportButton.Enabled = true;
        int n = Math.Min((int)countBox.Value, sim.Customers.Length);
        var inv = CultureInfo.InvariantCulture;
        grid.SuspendLayout();
        var rows = new List<DataGridViewRow>(n);
        for (int i = 0; i < n; i++)
        {
            var c = sim.Customers[i];
            var row = new DataGridViewRow();
            row.CreateCells(grid, c.Id, c.Arrival.ToString("F4", inv), c.Start.ToString("F4", inv), c.Wait.ToString("F4", inv),
                c.Service.ToString("F4", inv), c.End.ToString("F4", inv), c.TimeInSystem.ToString("F4", inv), c.Server + 1, c.IsWarmup ? "warm-up" : "");
            if (c.Wait > 1e-9) row.Cells[3].Style.ForeColor = Theme.WarnInk;
            if (c.IsWarmup) row.DefaultCellStyle.ForeColor = Theme.Muted;
            rows.Add(row);
        }
        grid.Rows.AddRange(rows.ToArray());
        grid.ResumeLayout();
        info.Text = $"Showing {n:N0} of {sim.Customers.Length:N0} customers (replication 1, seed {sim.SeedUsed}). Orange wait = the customer queued.";
    }

    private void Export()
    {
        if (run is null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv", FileName = $"queuelab_{run.Inputs.Model.Label().Replace("/", "")}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Title = "Export results and customer trace"
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            using var w = new StreamWriter(dlg.FileName, false, new System.Text.UTF8Encoding(false));
            SimulationReport.WriteCsv(w, run);
            info.Text = $"Exported to {dlg.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(FindForm(), "Could not write the file:\n" + ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
