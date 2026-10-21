using System.Globalization;
using QueueingSuite.Core.Data;
using QueueingSuite.Core.Models;

namespace QueueingSuite.App.Controls;

/// <summary>
/// "KFC Data" tab: which CSV is loaded, the fitted λ, μ, ρ, Ca², Cs² of Session 1 / Session 2 / All,
/// and every model side by side with the trace replay for the chosen session. Unstable sessions are marked.
/// </summary>
internal sealed class KfcDataPanel : UserControl
{
    private readonly AppData data;
    private readonly Label fileLabel = new() { AutoSize = true, ForeColor = Theme.Ink, Font = Theme.Font(9.5f), Margin = new Padding(14, 8, 0, 0) };
    private readonly Button openButton = Theme.Button("Open CSV…", Theme.Accent, Color.White, 110, 30);
    private readonly DataGridView fitGrid = new() { Dock = DockStyle.Fill };
    private readonly DataGridView modelGrid = new() { Dock = DockStyle.Fill };
    private readonly ComboBox sessionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Font = Theme.Font(9.5f) };
    private readonly Label notes = new() { Dock = DockStyle.Bottom, Height = 92, ForeColor = Theme.Muted, Font = Theme.Font(9f), Padding = new Padding(4, 6, 4, 0) };
    private readonly ToolTip toolTip = new();
    private IReadOnlyList<ModelColumn> columns = Array.Empty<ModelColumn>();

    /// <summary>Raised when the user asks to use a session in the dashboard (index into <see cref="AppData.Sessions"/>).</summary>
    public event EventHandler<int>? UseSessionRequested;
    private readonly Button useButton = Theme.Button("Use in dashboard", Theme.NavyLight, Color.White, 140, 30);

    public KfcDataPanel(AppData appData)
    {
        data = appData;
        BackColor = Theme.Background;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(4, 7, 4, 0), BackColor = Theme.Background, WrapContents = false };
        openButton.Margin = new Padding(0, 1, 0, 0);
        top.Controls.Add(openButton);
        top.Controls.Add(fileLabel);

        Theme.StyleGrid(fitGrid);
        string[] fitCols = { "Session", "n", "Mean inter-arrival", "Mean service", "λ", "μ", "ρ = λ/μ", "Ca²", "Cs²", "Steady state?" };
        foreach (var c in fitCols) fitGrid.Columns.Add(c, c);
        fitGrid.Columns[0].FillWeight = 60;
        fitGrid.Columns[0].MinimumWidth = 200;
        fitGrid.Columns[1].FillWeight = 14;
        fitGrid.Columns[9].FillWeight = 46;
        fitGrid.Columns[9].MinimumWidth = 150;
        fitGrid.RowTemplate.Height = 26;
        fitGrid.ColumnHeadersHeight = 40;
        foreach (DataGridViewColumn c in fitGrid.Columns) { c.SortMode = DataGridViewColumnSortMode.NotSortable; if (c.Index is > 0 and < 9) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }

        Theme.StyleGrid(modelGrid);
        modelGrid.RowTemplate.Height = 24;
        modelGrid.ColumnHeadersHeight = 40;

        var fitCard = new Panel { Dock = DockStyle.Top, Height = 128, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(4) };
        fitCard.Controls.Add(fitGrid);
        var fitHead = new Panel { Dock = DockStyle.Top, Height = 28 };
        fitHead.Controls.Add(new Label { Text = "FITTED PARAMETERS (Queue 1, single server)", Font = Theme.Font(11, FontStyle.Bold), ForeColor = Theme.Ink, AutoSize = true, Location = new Point(0, 4) });

        var modelHead = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
        modelHead.Controls.Add(new Label { Text = "ALL MODELS SIDE BY SIDE", Font = Theme.Font(11, FontStyle.Bold), ForeColor = Theme.Ink, AutoSize = true, Margin = new Padding(0, 4, 16, 0) });
        modelHead.Controls.Add(new Label { Text = "Session:", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 4, 0) });
        modelHead.Controls.Add(sessionBox);
        useButton.Margin = new Padding(14, 1, 0, 0);
        modelHead.Controls.Add(useButton);

        var modelCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(4) };
        modelCard.Controls.Add(modelGrid);

        var spacer = new Panel { Dock = DockStyle.Top, Height = 6 };
        Controls.Add(modelCard);
        Controls.Add(notes);
        Controls.Add(modelHead);
        Controls.Add(spacer);
        Controls.Add(fitCard);
        Controls.Add(fitHead);
        Controls.Add(top);

        openButton.Click += (_, _) => OpenCsv();
        sessionBox.SelectedIndexChanged += (_, _) => FillModels();
        useButton.Click += (_, _) => { if (sessionBox.SelectedIndex >= 0) UseSessionRequested?.Invoke(this, sessionBox.SelectedIndex); };
        data.Changed += (_, _) => Refresh_();
        Refresh_();
    }

    public void SelectSession(int index)
    {
        if (index >= 0 && index < sessionBox.Items.Count) sessionBox.SelectedIndex = index;
    }

    private void OpenCsv()
    {
        using var dlg = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", Title = "Open KFC data (tandem or dashboard format)" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (!data.Load(dlg.FileName))
            MessageBox.Show(FindForm(), data.Error, "Could not load the CSV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void Refresh_()
    {
        fitGrid.Rows.Clear();
        int keep = Math.Max(0, sessionBox.SelectedIndex);
        sessionBox.Items.Clear();
        if (data.Loaded is null)
        {
            fileLabel.Text = data.Error ?? "No data loaded.";
            fileLabel.ForeColor = Theme.BadInk;
            columns = Array.Empty<ModelColumn>();
            modelGrid.Columns.Clear();
            modelGrid.Rows.Clear();
            notes.Text = "";
            return;
        }
        fileLabel.ForeColor = Theme.Ink;
        string fmt = data.Loaded.Format == CsvFormat.Tandem ? "tandem format (source of truth)" : "dashboard format (ArrivalMinutes, ObservedWaitMinutes, ServiceMinutes, Queue2Minutes)";
        fileLabel.Text = $"{System.IO.Path.GetFileName(data.Path)}  –  {data.Loaded.Customers.Count} customers, {fmt}" + (data.Error is null ? "" : $"   ⚠ {data.Error}");
        toolTip.SetToolTip(fileLabel, data.Path ?? "");
        var inv = CultureInfo.InvariantCulture;
        foreach (var s in data.Sessions)
        {
            sessionBox.Items.Add(s.Name);
            string status = !s.IsUsable ? "too small" : s.IsUnstable ? "UNSTABLE (ρ ≥ 1)" : "stable (ρ < 1)";
            int idx = fitGrid.Rows.Add(s.Name, s.Count, s.MeanInterArrival.ToString("F4", inv), s.MeanService.ToString("F4", inv),
                s.Lambda.ToString("F4", inv), s.Mu.ToString("F4", inv), s.Rho.ToString("F4", inv), s.Ca2.ToString("F4", inv), s.Cs2.ToString("F4", inv), status);
            var cell = fitGrid.Rows[idx].Cells[9];
            cell.Style.ForeColor = s.IsUnstable || !s.IsUsable ? Theme.BadInk : Theme.GoodInk;
            cell.Style.Font = Theme.Font(9.5f, FontStyle.Bold);
        }
        if (sessionBox.Items.Count > 0) sessionBox.SelectedIndex = Math.Min(keep, sessionBox.Items.Count - 1);
        FillModels();
    }

    private void FillModels()
    {
        modelGrid.Rows.Clear();
        modelGrid.Columns.Clear();
        if (sessionBox.SelectedIndex < 0 || sessionBox.SelectedIndex >= data.Sessions.Count) return;
        var session = data.Sessions[sessionBox.SelectedIndex];
        columns = KfcComparison.Build(session);

        modelGrid.Columns.Add("measure", "Measure");
        modelGrid.Columns[0].FillWeight = 30;
        modelGrid.Columns[0].MinimumWidth = 130;
        foreach (var c in columns)
        {
            int i = modelGrid.Columns.Add(c.Name, c.Kind == "Data" ? c.Name : $"{c.Name}  [{c.Kind}]");
            modelGrid.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        foreach (DataGridViewColumn col in modelGrid.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;

        (string Name, Func<ModelColumn, double?> Get)[] rows =
        {
            ("ρ (utilization)", c => c.Rho), ("P0", c => c.P0), ("L", c => c.L), ("Lq", c => c.Lq), ("W (min)", c => c.W), ("Wq (min)", c => c.Wq)
        };
        foreach (var (name, get) in rows)
        {
            var values = new object[columns.Count + 1];
            values[0] = name;
            for (int i = 0; i < columns.Count; i++)
                values[i + 1] = get(columns[i]) is { } v ? v.ToString("F4", CultureInfo.InvariantCulture) : columns[i].IsStable ? "—" : "unstable";
            int idx = modelGrid.Rows.Add(values);
            for (int i = 0; i < columns.Count; i++)
                if (!columns[i].IsStable) modelGrid.Rows[idx].Cells[i + 1].Style.ForeColor = Theme.BadInk;
        }

        var lines = new List<string>();
        foreach (var c in columns.Where(c => c.Note is not null && (!c.IsStable || c.Kind == "Data")))
            lines.Add($"• {c.Name}: {c.Note}");
        if (session.IsUnstable)
            lines.Insert(0, $"⚠ {session.Name} has ρ = {session.Rho:F4} ≥ 1: arrivals outpace service, so no steady state exists. Only the trace result is meaningful; the formulas are not applicable.");
        notes.Text = string.Join("\n", lines);
        notes.ForeColor = session.IsUnstable ? Theme.BadInk : Theme.Muted;
    }
}
