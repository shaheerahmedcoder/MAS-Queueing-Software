using System.Drawing.Drawing2D;

namespace QueueingSuite.App;

/// <summary>Colours and fonts of the QUEUE LAB dashboard (dark navy sidebar, white cards, accent blue #4F66F0).</summary>
internal static class Theme
{
    public static readonly Color Navy = Color.FromArgb(25, 35, 60);
    public static readonly Color NavyLight = Color.FromArgb(40, 53, 88);
    public static readonly Color SidebarText = Color.FromArgb(205, 215, 235);
    public static readonly Color Accent = Color.FromArgb(79, 102, 240);       // #4F66F0
    public static readonly Color Background = Color.FromArgb(245, 247, 251);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(222, 227, 238);
    public static readonly Color Ink = Color.FromArgb(30, 41, 65);
    public static readonly Color Muted = Color.FromArgb(105, 112, 130);
    public static readonly Color MetricBlue = Color.FromArgb(43, 66, 135);

    public static readonly Color GoodFill = Color.FromArgb(222, 247, 234);
    public static readonly Color GoodInk = Color.FromArgb(24, 105, 70);
    public static readonly Color WarnFill = Color.FromArgb(255, 243, 214);
    public static readonly Color WarnInk = Color.FromArgb(146, 84, 0);
    public static readonly Color BadFill = Color.FromArgb(253, 226, 226);
    public static readonly Color BadInk = Color.FromArgb(160, 30, 30);
    public static readonly Color Amber = Color.FromArgb(245, 158, 11);
    public static readonly Color Red = Color.FromArgb(220, 60, 60);
    public static readonly Color Orange = Color.FromArgb(232, 89, 12);        // analytical series in charts

    public const string FontName = "Segoe UI";
    public static Font Font(float size, FontStyle style = FontStyle.Regular) => new(FontName, size, style);
    public static Font Mono(float size) => new("Consolas", size);

    /// <summary>Flat accent button as used by "Run".</summary>
    public static Button Button(string text, Color back, Color fore, int width = 100, int height = 34)
    {
        var b = new Button
        {
            Text = text, BackColor = back, ForeColor = fore, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Size = new Size(width, height), Font = Font(9.5f, FontStyle.Bold), UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Styles a read-only DataGridView consistently.</summary>
    public static void StyleGrid(DataGridView g)
    {
        g.ReadOnly = true;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.AllowUserToResizeRows = false;
        g.RowHeadersVisible = false;
        g.BackgroundColor = Color.White;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.GridColor = Border;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 241, 250);
        g.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        g.ColumnHeadersDefaultCellStyle.Font = Font(9f, FontStyle.Bold);
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = g.ColumnHeadersDefaultCellStyle.BackColor;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
        g.ColumnHeadersHeight = 30;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.DefaultCellStyle.Font = Font(9.5f);
        g.DefaultCellStyle.ForeColor = Ink;
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 231, 253);
        g.DefaultCellStyle.SelectionForeColor = Ink;
        g.RowTemplate.Height = 26;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    }
}
