using System.Drawing.Drawing2D;
using QueueingSuite.Core.Models;

namespace QueueingSuite.App.Controls;

/// <summary>
/// Custom GDI+ "queue layout": entrance → FIFO waiting line → c server boxes → exit.
/// It replays the simulation's event log (Arrival / StartService / Departure) so every picture is a real state of the run.
/// Idle servers are green, busy ones amber (red when the line is long) and show the customer number.
/// </summary>
internal sealed class QueueVisualizer : Control
{
    private sealed class Flyer
    {
        public PointF From, To;
        public long Start;
        public int Duration = 380;
        public string Label = "";
        public Color Fill;
    }

    private const int IconSize = 28;
    private const int IconStep = 34;
    private const int BoxW = 138, BoxH = 46, BoxGap = 8;

    private IReadOnlyList<SimEvent> events = Array.Empty<SimEvent>();
    private int servers = 1;
    private bool truncated;
    private int next;
    private double simTime;
    private readonly List<int> waiting = new();
    private int[] serverCustomer = { -1 };
    private readonly List<Flyer> flyers = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    private long lastTick;
    private double baseSimPerSecond = 1;
    private int served;

    /// <summary>Drawing is done in 96-DPI "logical" units and scaled by the display scaling (override for tests).</summary>
    internal float? ScaleOverride { get; set; }
    private float S => ScaleOverride ?? Math.Max(1f, DeviceDpi / 96f);

    /// <summary>Pixel-sized font (logical pixels): fixed under <c>ScaleTransform</c>, unlike point sizes.</summary>
    private static Font PxFont(float pixels, FontStyle style = FontStyle.Regular) => new(Theme.FontName, pixels, style, GraphicsUnit.Pixel);

    public bool Playing { get; private set; }
    /// <summary>Playback speed multiplier; 1 = about 4 events per second.</summary>
    public double Speed { get; set; } = 1;
    public double Time => simTime;
    public int InQueue => waiting.Count;
    public int InSystem => waiting.Count + serverCustomer.Count(c => c >= 0);
    public int Served => served;
    public bool HasData => events.Count > 0;
    public bool Finished => next >= events.Count && events.Count > 0;
    public int EventIndex => next;
    public int EventCount => events.Count;
    public event EventHandler? StateChanged;

    public QueueVisualizer()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.White;
        Font = Theme.Font(9f);
        timer.Tick += (_, _) => OnTick();
    }

    // ----------------------------------------------------------------- loading / control
    public void Load(SimulationResult? result)
    {
        Pause();
        if (result is null || result.Events.Count == 0)
        {
            events = Array.Empty<SimEvent>();
            servers = result?.Servers ?? 1;
            truncated = false;
            Reset();
            return;
        }
        events = result.Events;
        servers = result.Servers;
        truncated = result.EventsTruncated;
        double span = Math.Max(1e-9, events[^1].Time - events[0].Time);
        baseSimPerSecond = span / events.Count * 4.0;          // about 4 events per real second at speed 1
        Reset();
    }

    public void Reset()
    {
        Pause();
        next = 0;
        simTime = events.Count > 0 ? events[0].Time : 0;
        waiting.Clear();
        serverCustomer = Enumerable.Repeat(-1, Math.Max(1, servers)).ToArray();
        flyers.Clear();
        served = 0;
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Play()
    {
        if (!HasData) return;
        if (Finished) Reset();
        Playing = true;
        lastTick = Environment.TickCount64;
        timer.Start();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        Playing = false;
        timer.Stop();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies exactly one event.</summary>
    public void Step()
    {
        if (!HasData || Finished) return;
        Pause();
        simTime = events[next].Time;
        Apply(events[next++]);
        timer.Start();             // keep animating the flyer even while paused
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTick()
    {
        long now = Environment.TickCount64;
        if (Playing)
        {
            double dt = (now - lastTick) / 1000.0;
            lastTick = now;
            simTime += dt * Speed * baseSimPerSecond;
            int guard = 0;
            while (next < events.Count && events[next].Time <= simTime && guard++ < 500)
                Apply(events[next++]);
            if (next >= events.Count) { Playing = false; }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        flyers.RemoveAll(f => now - f.Start > f.Duration);
        if (!Playing && flyers.Count == 0) timer.Stop();
        Invalidate();
    }

    // ----------------------------------------------------------------- event application
    private void Apply(SimEvent e)
    {
        long now = Environment.TickCount64;
        var g = ComputeGeometry();
        switch (e.Type)
        {
            case SimEventType.Arrival:
                if (e.Server < 0)
                {
                    waiting.Add(e.CustomerId);
                    flyers.Add(new Flyer { From = g.Entrance, To = SlotPoint(g, waiting.Count - 1), Start = now, Label = e.CustomerId.ToString(), Fill = Theme.Accent });
                }
                break;
            case SimEventType.StartService:
            {
                PointF from = g.Entrance;
                int idx = waiting.IndexOf(e.CustomerId);
                if (idx >= 0) { from = SlotPoint(g, 0); waiting.RemoveAt(idx); }
                if (e.Server >= 0 && e.Server < serverCustomer.Length)
                {
                    serverCustomer[e.Server] = e.CustomerId;
                    flyers.Add(new Flyer { From = from, To = ServerCenter(g, e.Server), Start = now, Label = e.CustomerId.ToString(), Fill = Theme.Amber });
                }
                break;
            }
            case SimEventType.Departure:
                if (e.Server >= 0 && e.Server < serverCustomer.Length)
                {
                    serverCustomer[e.Server] = -1;
                    flyers.Add(new Flyer { From = ServerCenter(g, e.Server), To = g.Exit, Start = now, Label = e.CustomerId.ToString(), Fill = Color.FromArgb(120, 180, 140) });
                }
                served++;
                break;
        }
        if (!timer.Enabled) timer.Start();
    }

    // ----------------------------------------------------------------- geometry
    private readonly record struct Geo(RectangleF Track, int Cap, PointF Entrance, PointF Exit, int Rows, int Cols, float ServersLeft, float ServersTop);

    private Geo ComputeGeometry()
    {
        int w = Math.Max(300, (int)(ClientSize.Width / S)), h = Math.Max(200, (int)(ClientSize.Height / S));
        int rows = Math.Max(1, Math.Min(servers, (h - 110) / (BoxH + BoxGap)));
        int cols = (int)Math.Ceiling(servers / (double)rows);
        float serversWidth = cols * (BoxW + BoxGap);
        float serversLeft = w - serversWidth - 64;
        float gridHeight = rows * (BoxH + BoxGap) - BoxGap;
        float serversTop = 70 + Math.Max(0, (h - 70 - 34 - gridHeight) / 2);
        float trackLeft = 110, trackRight = serversLeft - 36;
        float trackY = serversTop + gridHeight / 2 - (IconSize + 14) / 2f;
        var track = new RectangleF(trackLeft, trackY, Math.Max(60, trackRight - trackLeft), IconSize + 14);
        int cap = Math.Max(2, (int)((track.Width - 16) / IconStep));
        return new Geo(track, cap, new PointF(54, track.Y + track.Height / 2), new PointF(w - 28, track.Y + track.Height / 2), rows, cols, serversLeft, serversTop);
    }

    /// <summary>Centre of waiting-line slot i (0 = front, next to the servers).</summary>
    private static PointF SlotPoint(Geo g, int index)
    {
        int slot = Math.Min(index, g.Cap - 1);
        return new PointF(g.Track.Right - 8 - IconSize / 2f - slot * IconStep, g.Track.Y + g.Track.Height / 2);
    }

    private static RectangleF ServerRect(Geo g, int k)
    {
        int col = k / g.Rows, row = k % g.Rows;
        return new RectangleF(g.ServersLeft + col * (BoxW + BoxGap), g.ServersTop + row * (BoxH + BoxGap), BoxW, BoxH);
    }

    private static PointF ServerCenter(Geo g, int k)
    {
        var r = ServerRect(g, k);
        return new PointF(r.X + 24, r.Y + r.Height / 2);
    }

    // ----------------------------------------------------------------- painting
    protected override void OnPaint(PaintEventArgs e)
    {
        var gfx = e.Graphics;
        gfx.SmoothingMode = SmoothingMode.AntiAlias;
        gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        gfx.Clear(Color.White);

        if (!HasData)
        {
            using var f = Theme.Font(11);
            TextRenderer.DrawText(gfx, "Run Simulation or Run & Compare to watch the queue.\nEach arrival, service start and departure of the run is replayed here.",
                f, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        gfx.ScaleTransform(S, S);
        var g = ComputeGeometry();
        using var title = PxFont(13.3f, FontStyle.Bold);
        using var small = PxFont(11.3f);
        using var ink = new SolidBrush(Theme.Ink);
        using var muted = new SolidBrush(Theme.Muted);

        gfx.DrawString("ENTRANCE", title, ink, 18, g.Entrance.Y - 36);
        gfx.DrawString("WAITING LINE (FIFO)  →  front", title, ink, g.Track.X, g.Track.Y - 28);
        gfx.DrawString($"SERVERS ({servers})", title, ink, g.ServersLeft, g.ServersTop - 28);
        gfx.DrawString("EXIT", title, ink, g.Exit.X - 24, g.Exit.Y - 36);

        // entrance arrow
        using (var arrow = new Pen(Theme.Muted, 2) { EndCap = LineCap.ArrowAnchor })
        {
            gfx.DrawLine(arrow, 24, g.Entrance.Y, g.Track.X - 6, g.Entrance.Y);
            gfx.DrawLine(arrow, g.Track.Right + 4, g.Entrance.Y, g.ServersLeft - 6, g.Entrance.Y);
            gfx.DrawLine(arrow, g.ServersLeft + g.Cols * (BoxW + BoxGap) - 4, g.Entrance.Y, g.Exit.X - 8, g.Exit.Y);
        }

        // track
        using (var path = Theme.RoundRect(g.Track, 10))
        using (var fill = new SolidBrush(Color.FromArgb(240, 243, 250)))
        using (var border = new Pen(Theme.Border))
        {
            gfx.FillPath(fill, path);
            gfx.DrawPath(border, path);
        }

        // waiting customers: draw the `cap` customers closest to the front; if more, show "+N more" at the back
        int n = waiting.Count;
        int drawn = n > g.Cap ? g.Cap - 1 : n;
        for (int i = 0; i < drawn; i++)
            DrawIcon(gfx, SlotPoint(g, i), waiting[i].ToString(), Theme.Accent);
        if (n > drawn)
        {
            string more = $"+{n - drawn} more";
            gfx.DrawString(more, title, ink, g.Track.X + 8, g.Track.Y + g.Track.Height / 2 - 9);
        }
        gfx.DrawString($"{n} waiting", small, muted, g.Track.X, g.Track.Bottom + 4);

        // servers
        for (int k = 0; k < servers; k++)
        {
            var r = ServerRect(g, k);
            bool busy = serverCustomer[k] >= 0;
            bool hot = busy && n > 8;
            Color fill = !busy ? Theme.GoodFill : hot ? Color.FromArgb(247, 170, 160) : Color.FromArgb(253, 224, 150);
            Color edge = !busy ? Color.FromArgb(120, 190, 150) : hot ? Theme.Red : Theme.Amber;
            using var path = Theme.RoundRect(r, 8);
            using var b = new SolidBrush(fill);
            using var p = new Pen(edge, 1.6f);
            gfx.FillPath(b, path);
            gfx.DrawPath(p, path);
            if (busy) DrawIcon(gfx, ServerCenter(g, k), serverCustomer[k].ToString(), hot ? Theme.Red : Theme.Amber);
            using var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            using var textBrush = new SolidBrush(busy ? Theme.Ink : Theme.GoodInk);
            gfx.DrawString($"SERVER {k + 1}", small, textBrush, r.X + (busy ? 44 : 12), r.Y + 5);
            gfx.DrawString(busy ? $"serving #{serverCustomer[k]}" : "idle", small, textBrush, r.X + (busy ? 44 : 12), r.Y + 23);
        }

        // moving customers
        long now = Environment.TickCount64;
        foreach (var f in flyers)
        {
            double t = Math.Clamp((now - f.Start) / (double)f.Duration, 0, 1);
            t = 1 - (1 - t) * (1 - t);        // ease-out
            var p = new PointF((float)(f.From.X + (f.To.X - f.From.X) * t), (float)(f.From.Y + (f.To.Y - f.From.Y) * t));
            DrawIcon(gfx, p, f.Label, f.Fill);
        }

        // footer
        string foot = $"t = {simTime:0.###}   ·   in system {InSystem}   ·   in queue {InQueue}   ·   served {served}   ·   event {next:N0} / {events.Count:N0}";
        gfx.DrawString(foot, small, muted, 18, ClientSize.Height / S - 24);
        if (truncated)
            gfx.DrawString("Event log capped: the animation covers the start of the run only.", small, muted, ClientSize.Width / S - 360, ClientSize.Height / S - 24);
    }

    private static void DrawIcon(Graphics g, PointF center, string label, Color fill)
    {
        var r = new RectangleF(center.X - IconSize / 2f, center.Y - IconSize / 2f, IconSize, IconSize);
        using var b = new SolidBrush(fill);
        using var p = new Pen(Color.FromArgb(90, 0, 0, 0), 1);
        g.FillEllipse(b, r);
        g.DrawEllipse(p, r);
        using var f = PxFont(label.Length > 3 ? 8.7f : 10.7f, FontStyle.Bold);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var white = new SolidBrush(fill.GetBrightness() > 0.6f ? Theme.Ink : Color.White);
        g.DrawString(label, f, white, r, sf);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
