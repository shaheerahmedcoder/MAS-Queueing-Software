using System.Diagnostics;
using QueueingSuite.App.Controls;
using QueueingSuite.Core;
using QueueingSuite.Core.Models;
using static QueueingSuite.Core.Models.Fmt;

namespace QueueingSuite.App;

/// <summary>
/// QUEUE LAB main window. It only wires the controls together and runs <see cref="QueueRunner"/> off the UI thread;
/// there is no queueing math in this file.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly AppData data = new();
    private readonly SidebarPanel sidebar = new();
    private readonly MetricCards cards = new();
    private readonly ResultsPanel results = new();
    private readonly AnimationPanel animation = new();
    private readonly ChartsPanel charts = new();
    private readonly ChiSquarePanel chiSquare = new();
    private readonly CustomersPanel customers = new();
    private readonly KfcDataPanel kfcPanel;
    private readonly TabControl tabs = new();
    private readonly Label subtitle = new();
    private readonly System.Windows.Forms.Timer liveTimer = new() { Interval = 300 };

    private CancellationTokenSource? cts;
    private bool running;
    private RunOutput? lastRun;

    public MainForm()
    {
        Text = "Queueing Model Simulator – QUEUE LAB";
        // Layout is designed at 96 DPI; Dpi autoscale makes every fixed size follow the Windows display scaling (125 %, 150 % ...).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1360, 860);
        MinimumSize = new Size(1100, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        Font = Theme.Font(10);

        data.LoadDefault();
        kfcPanel = new KfcDataPanel(data);
        sidebar.SetData(data);
        chiSquare.SetData(data);

        // ---- main area ----
        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 12), BackColor = Theme.Background };
        var header = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = Theme.Background };
        header.Controls.Add(new Label
        {
            Text = "Queue Performance Dashboard", Font = Theme.Font(21, FontStyle.Bold), ForeColor = Theme.Ink,
            Location = new Point(0, 0), AutoSize = true
        });
        subtitle.ForeColor = Theme.Muted;
        subtitle.Location = new Point(3, 44);
        subtitle.AutoSize = true;
        header.Controls.Add(subtitle);

        cards.Dock = DockStyle.Top;

        tabs.Dock = DockStyle.Fill;
        tabs.Font = Theme.Font(10);
        tabs.Padding = new Point(14, 5);
        AddTab("Results & formulas", results);
        AddTab("Queue animation", animation);
        AddTab("Charts", charts);
        AddTab("Chi-square tests", chiSquare);
        AddTab("Customers", customers);
        AddTab("KFC data", kfcPanel);

        var tabHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Background };
        tabHost.Controls.Add(tabs);

        main.Controls.Add(tabHost);
        main.Controls.Add(cards);
        main.Controls.Add(header);
        Controls.Add(main);
        Controls.Add(sidebar);

        // ---- wiring ----
        sidebar.RunClicked += async (_, _) => await RunAsync();
        sidebar.CancelClicked += (_, _) => cts?.Cancel();
        sidebar.ModeChanged += (_, _) => OnModeChanged();
        sidebar.InputsChanged += (_, _) => { liveTimer.Stop(); liveTimer.Start(); };
        liveTimer.Tick += (_, _) => { liveTimer.Stop(); if (sidebar.Mode == RunMode.Analytical && !running) _ = RunAsync(); };
        kfcPanel.UseSessionRequested += (_, index) =>
        {
            sidebar.SetUseKfc(true);
            sidebar.SelectSession(index);
            tabs.SelectedIndex = 0;
            sidebar.SetStatus($"Using {data.Sessions[index].Name}: λ and μ are now fitted from the data. Press Run.", StatusKind.Info);
        };
        FormClosing += (_, _) => cts?.Cancel();
        UpdateSubtitle();
        Shown += async (_, _) => { if (!SnapshotMode) await RunAsync(); };
    }

    /// <summary>Never open larger than the screen's usable area (a 1360x860 window does not fit a 1080p laptop at 150 % scaling).</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
    }

    /// <summary>Set by the headless snapshot runner so the form does not start its own first run.</summary>
    internal bool SnapshotMode { get; set; }

    private void AddTab(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = Theme.Background, Padding = new Padding(8) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        tabs.TabPages.Add(page);
    }

    internal void SelectTab(int index) => tabs.SelectedIndex = index;
    internal SidebarPanel Sidebar => sidebar;
    internal ChartsPanel Charts => charts;
    internal ChiSquarePanel ChiSquare => chiSquare;
    internal AnimationPanel Animation => animation;
    internal KfcDataPanel Kfc => kfcPanel;

    private void UpdateSubtitle() =>
        subtitle.Text = sidebar.Mode switch
        {
            RunMode.Analytical => "Analytical mode – steady-state formulas with your numbers substituted",
            RunMode.Simulation => "Simulation mode – discrete-event simulation results",
            _ => "Compare mode – formulas versus discrete-event simulation"
        };

    private void OnModeChanged()
    {
        UpdateSubtitle();
        if (sidebar.Mode == RunMode.Analytical) _ = RunAsync();
        else sidebar.SetStatus($"{sidebar.Mode} mode selected. Press the blue button to run.", StatusKind.Info);
    }

    // ================================================================= running
    /// <summary>Validates the inputs and runs the selected mode. Formulas run inline; simulations run on a worker thread.</summary>
    internal async Task RunAsync()
    {
        if (running) return;
        try
        {
            if (!sidebar.TryReadInputs(out var inputs, out var parseError))
            {
                Fail(parseError!);
                return;
            }
            var validation = InputValidator.Validate(inputs);
            if (!validation.IsValid)
            {
                Fail(string.Join("\n", validation.Errors));
                return;
            }

            var mode = sidebar.Mode;
            var sw = Stopwatch.StartNew();
            RunOutput output;
            if (mode == RunMode.Analytical)
            {
                output = QueueRunner.Run(inputs, mode);
            }
            else
            {
                running = true;
                cts = new CancellationTokenSource();
                sidebar.SetRunning(true);
                sidebar.SetStatus($"Simulating {inputs.Customers:N0} customers" + (inputs.Replications > 1 ? $" × {inputs.Replications} replications…" : "…"), StatusKind.Info);
                var progress = new Progress<double>(sidebar.SetProgress);
                var token = cts.Token;
                output = await Task.Run(() => QueueRunner.Run(inputs, mode, progress, token), token);
            }
            sw.Stop();
            Show(output, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            sidebar.SetStatus("Run cancelled.", StatusKind.Warning);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            if (running)
            {
                running = false;
                sidebar.SetRunning(false);
                cts?.Dispose();
                cts = null;
            }
        }
    }

    private void Fail(string message)
    {
        sidebar.SetStatus(message, StatusKind.Error);
        cards.ShowError(message.Replace("\n", "  "));
    }

    private void Show(RunOutput run, TimeSpan elapsed)
    {
        lastRun = run;
        cards.ShowRun(run);
        results.ShowRun(run);
        charts.ShowRun(run);
        animation.LoadRun(run.Simulation?.First);
        customers.ShowRun(run);
        chiSquare.SetRun(run.Simulation is null ? null : run);

        var a = run.Analytical;
        string what = run.Mode switch
        {
            RunMode.Analytical => $"{run.Inputs.Model.Label()}: formulas evaluated ({a.Method}).",
            _ when run.Inputs.UsesKfcData => $"{run.Inputs.Model.Label()}: replayed {run.Simulation!.First.CustomersServed} recorded customers of {run.Inputs.Session!.Name} in {elapsed.TotalSeconds:0.00} s.",
            _ => $"{run.Inputs.Model.Label()}: simulated {run.Simulation!.First.TotalCustomers:N0} customers" +
                 (run.Simulation.Replications > 1 ? $" × {run.Simulation.Replications}" : "") + $" in {elapsed.TotalSeconds:0.00} s (seed {run.Simulation.BaseSeed})."
        };
        if (!a.IsStable)
            sidebar.SetStatus(what + $"\nUNSTABLE: ρ = {N(run.Resolved.Rho)} ≥ 1 – no steady-state formulas.", StatusKind.Warning);
        else if (run.Warnings.Count > 0)
            sidebar.SetStatus(what + "\n" + run.Warnings[0], StatusKind.Warning);
        else
            sidebar.SetStatus(what, StatusKind.Good);
    }

    internal RunOutput? LastRun => lastRun;

    /// <summary>Cancels the running simulation (same as pressing Cancel).</summary>
    internal void CancelRun() => cts?.Cancel();
}
