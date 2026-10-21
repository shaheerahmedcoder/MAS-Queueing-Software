using System.Globalization;
using QueueingSuite.Core;
using QueueingSuite.Core.Data;
using QueueingSuite.Core.Models;
using QueueingSuite.Core.Simulation;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.App.Controls;

internal enum StatusKind { Info, Good, Warning, Error }

/// <summary>
/// The dark navy sidebar: mode selector, model, model-specific inputs, run controls and status.
/// It only reads / writes text boxes; all interpretation and math is done by <c>QueueingSuite.Core</c>.
/// </summary>
internal sealed class SidebarPanel : Panel
{
    private sealed record KindItem(DistributionKind Kind, string Text) { public override string ToString() => Text; }
    private sealed class Group : Panel
    {
        public Label Caption { get; }
        public Control Input { get; }
        public Group(string caption, Control input, int inputHeight = 26)
        {
            Caption = new Label { Text = caption, ForeColor = Theme.SidebarText, Font = Theme.Font(8.5f, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) };
            Input = input;
            input.Location = new Point(0, 18);
            input.Size = new Size(240, inputHeight);
            Size = new Size(244, 19 + inputHeight + 6);
            Margin = new Padding(0, 0, 0, 2);
            Controls.Add(Caption);
            Controls.Add(input);
        }
    }

    private static readonly KindItem[] Kinds =
    {
        new(DistributionKind.Exponential, "Exponential"),
        new(DistributionKind.Deterministic, "Deterministic (constant)"),
        new(DistributionKind.Uniform, "Uniform"),
        new(DistributionKind.Normal, "Normal (truncated at 0)"),
        new(DistributionKind.Gamma, "Gamma"),
        new(DistributionKind.Erlang, "Erlang-k"),
        new(DistributionKind.Empirical, "Empirical (KFC data)")
    };

    private readonly Button[] modeButtons = new Button[3];
    private readonly ComboBox modelBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox lambdaBox = new() { Text = "4" };
    private readonly TextBox muBox = new() { Text = "6" };
    private readonly NumericUpDown serversBox = new() { Minimum = 1, Maximum = 50, Value = 2 };
    private readonly ComboBox arrKindBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox arrCvBox = new() { Text = "1" };
    private readonly ComboBox svcKindBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox svcCvBox = new() { Text = "1" };
    private readonly TextBox customersBox = new() { Text = "10000" };
    private readonly TextBox warmupBox = new() { Text = "0" };
    private readonly TextBox seedBox = new() { Text = "" };
    private readonly TextBox repsBox = new() { Text = "1" };
    private readonly CheckBox useKfcBox = new() { Text = "Use KFC data (fit λ, μ; replay trace)", ForeColor = Theme.SidebarText, AutoSize = false };
    private readonly ComboBox sessionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label previewLabel = new() { ForeColor = Theme.SidebarText, Font = Theme.Font(8.5f), AutoSize = false };
    private readonly Label statusLabel = new() { ForeColor = Color.Gainsboro, Font = Theme.Font(9f), AutoSize = false };
    private readonly ProgressBar progressBar = new() { Minimum = 0, Maximum = 1000, Style = ProgressBarStyle.Continuous };
    private readonly Button runButton = Theme.Button("Run Simulation", Theme.Accent, Color.White, 150, 40);
    private readonly Button cancelButton = Theme.Button("Cancel", Theme.NavyLight, Color.White, 80, 40);

    private Group gLambda = null!, gMu = null!, gServers = null!, gArrKind = null!, gArrCv = null!, gSvcKind = null!, gSvcCv = null!,
        gSession = null!;
    private AppData? data;
    private bool updating;

    public event EventHandler? RunClicked;
    public event EventHandler? CancelClicked;
    public event EventHandler? InputsChanged;
    public event EventHandler? ModeChanged;

    public RunMode Mode { get; private set; } = RunMode.Compare;

    public SidebarPanel()
    {
        Dock = DockStyle.Left;
        Width = 296;
        BackColor = Theme.Navy;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Navy, Margin = Padding.Empty, Padding = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        // ---- header ----
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Navy };
        header.Controls.Add(new Label { Text = "QUEUE LAB", ForeColor = Color.White, Font = Theme.Font(19, FontStyle.Bold), Location = new Point(20, 14), AutoSize = true });
        header.Controls.Add(new Label { Text = "Queueing model simulator", ForeColor = Theme.SidebarText, Font = Theme.Font(8.5f), Location = new Point(22, 49), AutoSize = true });
        layout.Controls.Add(header, 0, 0);

        // ---- scrolling inputs ----
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Padding = new Padding(20, 4, 4, 8), BackColor = Theme.Navy
        };
        layout.Controls.Add(flow, 0, 1);

        // mode selector (segmented buttons)
        var modeCaption = new Label { Text = "MODE", ForeColor = Theme.SidebarText, Font = Theme.Font(8.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        flow.Controls.Add(modeCaption);
        var modeRow = new Panel { Size = new Size(244, 34), Margin = new Padding(0, 0, 0, 8) };
        string[] modeNames = { "Analytical", "Simulation", "Compare" };
        for (int k = 0; k < 3; k++)
        {
            int index = k;
            var b = Theme.Button(modeNames[k], Theme.NavyLight, Color.White, 80, 32);
            b.Location = new Point(k * 82, 0);
            b.Font = Theme.Font(8.5f, FontStyle.Bold);
            b.Click += (_, _) => SetMode((RunMode)index);
            modeButtons[k] = b;
            modeRow.Controls.Add(b);
        }
        flow.Controls.Add(modeRow);

        modelBox.Items.AddRange(new object[] { "M/M/1", "M/M/c", "M/G/1", "G/G/1" });
        modelBox.SelectedIndex = 0;
        flow.Controls.Add(new Group("QUEUEING MODEL", modelBox));

        gArrKind = Add(flow, "ARRIVAL DISTRIBUTION", arrKindBox);
        gArrCv = Add(flow, "ARRIVAL CV  (σ / mean)", arrCvBox);
        gLambda = Add(flow, "ARRIVAL RATE (λ)", lambdaBox);
        gSvcKind = Add(flow, "SERVICE DISTRIBUTION", svcKindBox);
        gSvcCv = Add(flow, "SERVICE CV  (σ / mean)", svcCvBox);
        gMu = Add(flow, "SERVICE RATE (μ)", muBox);
        gServers = Add(flow, "NUMBER OF SERVERS (c)", serversBox);

        foreach (var combo in new[] { arrKindBox, svcKindBox })
        {
            combo.Items.AddRange(Kinds);
            combo.SelectedIndex = 0;
        }

        previewLabel.Size = new Size(240, 54);
        previewLabel.Margin = new Padding(0, 0, 0, 6);
        flow.Controls.Add(previewLabel);

        Add(flow, "CUSTOMERS TO SIMULATE", customersBox);
        Add(flow, "WARM-UP CUSTOMERS (discarded)", warmupBox);
        Add(flow, "RANDOM SEED  (blank = random)", seedBox);
        Add(flow, "REPLICATIONS  (>1 gives 95% CI)", repsBox);

        useKfcBox.Size = new Size(244, 24);
        useKfcBox.Margin = new Padding(0, 6, 0, 4);
        flow.Controls.Add(useKfcBox);
        gSession = Add(flow, "DATA SESSION", sessionBox);

        // ---- bottom: buttons, progress, status ----
        var bottom = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Navy, Padding = new Padding(20, 6, 12, 6) };
        runButton.Location = new Point(20, 8);
        cancelButton.Location = new Point(176, 8);
        cancelButton.Enabled = false;
        progressBar.SetBounds(20, 56, 240, 8);
        statusLabel.SetBounds(20, 70, 250, 86);
        bottom.Controls.AddRange(new Control[] { runButton, cancelButton, progressBar, statusLabel });
        layout.Controls.Add(bottom, 0, 2);

        // ---- wiring ----
        modelBox.SelectedIndexChanged += (_, _) => { ApplyVisibility(); Changed(); };
        arrKindBox.SelectedIndexChanged += (_, _) => { ApplyVisibility(); Changed(); };
        svcKindBox.SelectedIndexChanged += (_, _) => { ApplyVisibility(); Changed(); };
        useKfcBox.CheckedChanged += (_, _) => { ApplyVisibility(); Changed(); };
        sessionBox.SelectedIndexChanged += (_, _) => { ApplyVisibility(); Changed(); };
        serversBox.ValueChanged += (_, _) => Changed();
        foreach (var t in new[] { lambdaBox, muBox, arrCvBox, svcCvBox, customersBox, warmupBox, seedBox, repsBox })
        {
            t.TextChanged += (_, _) => Changed();
            t.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunClicked?.Invoke(this, EventArgs.Empty); } };
        }
        runButton.Click += (_, _) => RunClicked?.Invoke(this, EventArgs.Empty);
        cancelButton.Click += (_, _) => CancelClicked?.Invoke(this, EventArgs.Empty);

        SetMode(RunMode.Compare, raise: false);
        ApplyVisibility();
    }

    private Group Add(FlowLayoutPanel flow, string caption, Control input)
    {
        var g = new Group(caption, input);
        flow.Controls.Add(g);
        return g;
    }

    // ================================================================= public API
    public void SetData(AppData appData)
    {
        data = appData;
        appData.Changed += (_, _) => RefreshSessions();
        RefreshSessions();
    }

    private void RefreshSessions()
    {
        updating = true;
        int keep = Math.Max(0, sessionBox.SelectedIndex);
        sessionBox.Items.Clear();
        if (data is not null)
            foreach (var s in data.Sessions) sessionBox.Items.Add(s.Name);
        if (sessionBox.Items.Count > 0) sessionBox.SelectedIndex = Math.Min(keep, sessionBox.Items.Count - 1);
        updating = false;
        useKfcBox.Enabled = sessionBox.Items.Count > 0;
        ApplyVisibility();
        Changed();
    }

    public SessionFit? SelectedSession =>
        data is not null && sessionBox.SelectedIndex >= 0 && sessionBox.SelectedIndex < data.Sessions.Count
            ? data.Sessions[sessionBox.SelectedIndex] : null;

    public void SetStatus(string text, StatusKind kind)
    {
        statusLabel.Text = text;
        statusLabel.ForeColor = kind switch
        {
            StatusKind.Good => Color.FromArgb(150, 235, 190),
            StatusKind.Warning => Color.FromArgb(255, 214, 130),
            StatusKind.Error => Color.FromArgb(255, 150, 150),
            _ => Color.Gainsboro
        };
    }

    public string StatusText => statusLabel.Text;

    public void SetProgress(double fraction) =>
        progressBar.Value = (int)Math.Clamp(fraction * progressBar.Maximum, 0, progressBar.Maximum);

    public void SetRunning(bool running)
    {
        runButton.Enabled = !running;
        cancelButton.Enabled = running;
        if (!running) progressBar.Value = 0;
    }

    public void SetMode(RunMode mode, bool raise = true)
    {
        Mode = mode;
        for (int k = 0; k < 3; k++)
        {
            bool on = (int)mode == k;
            modeButtons[k].BackColor = on ? Theme.Accent : Theme.NavyLight;
            modeButtons[k].ForeColor = on ? Color.White : Theme.SidebarText;
        }
        runButton.Text = mode switch { RunMode.Analytical => "Calculate", RunMode.Simulation => "Run Simulation", _ => "Run & Compare" };
        if (raise) ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects model/inputs programmatically (used by the headless snapshot mode and tests).</summary>
    public void Configure(ModelKind model, string lambda, string mu, int servers, string customers)
    {
        modelBox.SelectedIndex = (int)model;
        lambdaBox.Text = lambda;
        muBox.Text = mu;
        serversBox.Value = servers;
        customersBox.Text = customers;
    }

    public void ConfigureDistributions(DistributionKind arrival, string arrivalCv, DistributionKind service, string serviceCv)
    {
        arrKindBox.SelectedIndex = Array.FindIndex(Kinds, k => k.Kind == arrival);
        svcKindBox.SelectedIndex = Array.FindIndex(Kinds, k => k.Kind == service);
        arrCvBox.Text = arrivalCv;
        svcCvBox.Text = serviceCv;
    }

    public void SetUseKfc(bool on) => useKfcBox.Checked = on;

    public void SelectSession(int index)
    {
        if (index >= 0 && index < sessionBox.Items.Count) sessionBox.SelectedIndex = index;
    }

    /// <summary>Sets a text box by field name (headless snapshot mode / tests).</summary>
    public void SetLambdaText(string text) => lambdaBox.Text = text;
    public void SetReplicationsText(string text) => repsBox.Text = text;

    // ================================================================= reading inputs
    /// <summary>Parses the boxes. Returns false with a specific message when a box does not contain a valid number.</summary>
    public bool TryReadInputs(out QueueInputs inputs, out string? error)
    {
        inputs = new QueueInputs();
        error = null;
        var model = (ModelKind)modelBox.SelectedIndex;
        bool kfc = useKfcBox.Checked;
        var session = SelectedSession;
        var all = data?.Sessions.LastOrDefault();

        double lambda = 1, mu = 1, arrCv = 1, svcCv = 1;
        int customers, warmup, reps;
        int? seed = null;

        if (kfc)
        {
            if (session is null) { error = "No data loaded. Open the KFC CSV on the KFC Data tab first."; return false; }
            lambda = session.Lambda; mu = session.Mu;
        }
        else
        {
            // (a box disabled because its value is fitted from data holds that fitted number, so it parses the same way)
            if (!ParseDouble(lambdaBox.Text, "Arrival rate λ", out lambda, ref error)) return false;
            if (!ParseDouble(muBox.Text, "Service rate μ", out mu, ref error)) return false;
            if (gArrCv.Visible && !ParseDouble(arrCvBox.Text, "Arrival CV", out arrCv, ref error)) return false;
            if (gSvcCv.Visible && !ParseDouble(svcCvBox.Text, "Service CV", out svcCv, ref error)) return false;
        }
        if (!ParseInt(customersBox.Text, "Customers", out customers, ref error)) return false;
        if (!ParseInt(warmupBox.Text, "Warm-up customers", out warmup, ref error)) return false;
        if (!ParseInt(repsBox.Text, "Replications", out reps, ref error)) return false;
        if (!string.IsNullOrWhiteSpace(seedBox.Text))
        {
            if (!int.TryParse(seedBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sd) || sd < 0)
            {
                error = $"Random seed must be a whole number ≥ 0 or blank (got '{seedBox.Text.Trim()}').";
                return false;
            }
            seed = sd;
        }

        var arrKind = ((KindItem)arrKindBox.SelectedItem!).Kind;
        var svcKind = ((KindItem)svcKindBox.SelectedItem!).Kind;
        var source = session ?? all;
        inputs = new QueueInputs
        {
            Model = model, Lambda = lambda, Mu = mu, Servers = (int)serversBox.Value,
            ArrivalKind = model == ModelKind.GG1 ? arrKind : DistributionKind.Exponential, ArrivalCv = arrCv,
            ServiceKind = model is ModelKind.MG1 or ModelKind.GG1 ? svcKind : DistributionKind.Exponential, ServiceCv = svcCv,
            Customers = customers, Warmup = warmup, Seed = seed, Replications = reps,
            Session = kfc ? session : null,
            EmpiricalInterArrivals = source?.InterArrivals,
            EmpiricalServices = source?.Services
        };
        return true;

    }

    private static bool ParseDouble(string text, string name, out double value, ref string? error)
    {
        text = text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;
        error = text.Length == 0 ? $"{name} is empty. Type a number, e.g. 4 or 4.5." : $"{name}: '{text}' is not a number. Type a number, e.g. 4 or 4.5.";
        value = double.NaN;
        return false;
    }

    private static bool ParseInt(string text, string name, out int value, ref string? error)
    {
        text = text.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        error = text.Length == 0 ? $"{name} is empty. Type a whole number." : $"{name}: '{text}' is not a whole number.";
        return false;
    }

    // ================================================================= visibility & preview
    private void Changed()
    {
        if (updating) return;
        UpdatePreview();
        InputsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyVisibility()
    {
        if (updating) return;
        updating = true;
        var model = (ModelKind)modelBox.SelectedIndex;
        bool kfc = useKfcBox.Checked && useKfcBox.Enabled;
        var arrKind = ((KindItem)arrKindBox.SelectedItem!).Kind;
        var svcKind = ((KindItem)svcKindBox.SelectedItem!).Kind;

        gServers.Visible = model == ModelKind.MMc;
        gArrKind.Visible = model == ModelKind.GG1 && !kfc;
        gArrCv.Visible = gArrKind.Visible && DistributionFactory.UsesCv(arrKind);
        gSvcKind.Visible = model is ModelKind.MG1 or ModelKind.GG1 && !kfc;
        gSvcCv.Visible = gSvcKind.Visible && DistributionFactory.UsesCv(svcKind);
        gSession.Visible = true;

        // Rates that come from data are shown but not editable.
        bool lambdaFromData = kfc || (model == ModelKind.GG1 && arrKind == DistributionKind.Empirical);
        bool muFromData = kfc || (model is ModelKind.MG1 or ModelKind.GG1 && svcKind == DistributionKind.Empirical);
        gLambda.Input.Enabled = !lambdaFromData;
        gMu.Input.Enabled = !muFromData;
        gLambda.Caption.Text = lambdaFromData ? "ARRIVAL RATE (λ) – fitted from data" : "ARRIVAL RATE (λ)";
        gMu.Caption.Text = muFromData ? "SERVICE RATE (μ) – fitted from data" : "SERVICE RATE (μ)";
        var source = SelectedSession;
        if (source is { IsUsable: true })
        {
            if (lambdaFromData) lambdaBox.Text = source.Lambda.ToString("0.####", CultureInfo.InvariantCulture);
            if (muFromData) muBox.Text = source.Mu.ToString("0.####", CultureInfo.InvariantCulture);
        }
        sessionBox.Enabled = sessionBox.Items.Count > 0;
        updating = false;
    }

    private void UpdatePreview()
    {
        if (!TryReadInputs(out var inputs, out var err))
        {
            previewLabel.ForeColor = Color.FromArgb(255, 190, 150);
            previewLabel.Text = err ?? "";
            return;
        }
        try
        {
            var r = QueueRunner.Resolve(inputs);
            var lines = new List<string>
            {
                $"E[S] = {N(r.MeanService)}   Var(S) = {N(r.VarService)}   Cs² = {N(r.Cs2)}",
                inputs.Model == ModelKind.GG1 || inputs.UsesKfcData ? $"λ = {N(r.Lambda)}   Ca² = {N(r.Ca2)}" : $"λ = {N(r.Lambda)}",
                $"ρ = {N(r.Rho)}" + (r.Rho >= 1 ? "   ⚠ unstable (ρ ≥ 1)" : "")
            };
            previewLabel.ForeColor = r.Rho >= 1 ? Color.FromArgb(255, 190, 120) : Theme.SidebarText;
            previewLabel.Text = string.Join("\n", lines);
        }
        catch (ArgumentException ex)
        {
            previewLabel.ForeColor = Color.FromArgb(255, 190, 150);
            previewLabel.Text = ex.Message;
        }
    }
}
