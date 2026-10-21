using QueueingSuite.Core.Models;

namespace QueueingSuite.App.Controls;

/// <summary>Play / Pause / Step / Restart, speed slider and time label around the <see cref="QueueVisualizer"/>.</summary>
internal sealed class AnimationPanel : UserControl
{
    private readonly QueueVisualizer visualizer = new() { Dock = DockStyle.Fill };
    private readonly Button playButton = Theme.Button("▶  Play", Theme.Accent, Color.White, 96, 32);
    private readonly Button stepButton = Theme.Button("Step ▸", Theme.NavyLight, Color.White, 80, 32);
    private readonly Button restartButton = Theme.Button("⟲ Restart", Theme.NavyLight, Color.White, 92, 32);
    private readonly TrackBar speedBar = new() { Minimum = 1, Maximum = 40, Value = 10, TickStyle = TickStyle.None, Width = 180, AutoSize = false, Height = 28 };
    private readonly Label speedLabel = new() { AutoSize = true, ForeColor = Theme.Ink, Font = Theme.Font(9f) };
    private readonly Label timeLabel = new() { AutoSize = true, ForeColor = Theme.Ink, Font = Theme.Font(10f, FontStyle.Bold) };

    public QueueVisualizer Visualizer => visualizer;

    public AnimationPanel()
    {
        BackColor = Theme.Background;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 7, 8, 4), BackColor = Theme.Background, WrapContents = false };
        playButton.Margin = new Padding(0, 0, 6, 0);
        stepButton.Margin = new Padding(0, 0, 6, 0);
        restartButton.Margin = new Padding(0, 0, 18, 0);
        var speedCaption = new Label { Text = "Speed", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 4, 0) };
        speedBar.Margin = new Padding(0, 3, 6, 0);
        speedLabel.Margin = new Padding(0, 8, 24, 0);
        timeLabel.Margin = new Padding(0, 7, 0, 0);
        bar.Controls.AddRange(new Control[] { playButton, stepButton, restartButton, speedCaption, speedBar, speedLabel, timeLabel });

        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
        frame.Controls.Add(visualizer);
        Controls.Add(frame);
        Controls.Add(bar);

        playButton.Click += (_, _) => { if (visualizer.Playing) visualizer.Pause(); else visualizer.Play(); };
        stepButton.Click += (_, _) => visualizer.Step();
        restartButton.Click += (_, _) => visualizer.Reset();
        speedBar.ValueChanged += (_, _) => ApplySpeed();
        visualizer.StateChanged += (_, _) => RefreshLabels();
        ApplySpeed();
        RefreshLabels();
    }

    public void LoadRun(SimulationResult? result)
    {
        visualizer.Load(result);
        RefreshLabels();
    }

    /// <summary>Starts playback (used by the headless snapshot mode).</summary>
    public void PlayFor(int events)
    {
        for (int i = 0; i < events; i++) visualizer.Step();
    }

    private void ApplySpeed()
    {
        // slider 1..40 -> 0.1x .. 20x on a log scale (10 = 1x)
        visualizer.Speed = Math.Pow(10, (speedBar.Value - 10) / 15.0);
        speedLabel.Text = $"{visualizer.Speed:0.#}×";
    }

    private void RefreshLabels()
    {
        playButton.Text = visualizer.Playing ? "❚❚  Pause" : "▶  Play";
        playButton.Enabled = stepButton.Enabled = restartButton.Enabled = visualizer.HasData;
        timeLabel.Text = visualizer.HasData
            ? $"t = {visualizer.Time:0.###}    in system {visualizer.InSystem}    in queue {visualizer.InQueue}    served {visualizer.Served}"
            : "No simulation yet";
    }
}
