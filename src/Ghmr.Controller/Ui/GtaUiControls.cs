using System.Drawing.Drawing2D;

namespace Ghmr.Controller.Ui;

internal enum ActionIcon
{
    Play,
    Chaos,
    Bridge,
    Stop
}

internal sealed class GtaBackdropPanel : Panel
{
    private static readonly Color TopColor = Color.FromArgb(13, 17, 25);
    private static readonly Color BottomColor = Color.FromArgb(8, 11, 17);

    public GtaBackdropPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (LinearGradientBrush background = new(
                   ClientRectangle,
                   TopColor,
                   BottomColor,
                   LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(background, ClientRectangle);
        }

        DrawSunsetGlow(graphics);
        DrawHalftone(graphics);
        DrawSkyline(graphics);
    }

    private void DrawSunsetGlow(Graphics graphics)
    {
        int diameter = Math.Max(260, Width / 3);
        Rectangle cyanGlow = new(-diameter / 2, Height / 4, diameter, diameter);
        Rectangle magentaGlow = new(
            Width - diameter / 2,
            -diameter / 3,
            diameter,
            diameter);

        using SolidBrush cyan = new(Color.FromArgb(20, 44, 198, 190));
        using SolidBrush magenta = new(Color.FromArgb(24, 224, 73, 143));
        graphics.FillEllipse(cyan, cyanGlow);
        graphics.FillEllipse(magenta, magentaGlow);
    }

    private void DrawHalftone(Graphics graphics)
    {
        using SolidBrush dots = new(Color.FromArgb(34, 255, 255, 255));
        const int spacing = 18;
        const int radius = 2;
        int startX = Math.Max(0, Width - 250);

        for (int y = 26; y < 150; y += spacing)
        {
            for (int x = startX + (y / spacing % 2) * 8; x < Width - 20; x += spacing)
            {
                graphics.FillEllipse(dots, x, y, radius, radius);
            }
        }
    }

    private void DrawSkyline(Graphics graphics)
    {
        int baseline = Height;
        int skylineTop = Math.Max(0, Height - 132);
        using SolidBrush distant = new(Color.FromArgb(68, 21, 27, 38));
        using SolidBrush near = new(Color.FromArgb(128, 10, 14, 22));
        using Pen aerial = new(Color.FromArgb(90, 47, 193, 185), 2F);

        int[] distantWidths = [72, 54, 86, 45, 68, 92, 52, 78, 64, 88, 48, 76];
        int[] distantHeights = [58, 92, 72, 116, 82, 66, 104, 76, 96, 62, 110, 80];
        int x = -12;
        for (int index = 0; x < Width; index++)
        {
            int width = distantWidths[index % distantWidths.Length];
            int height = distantHeights[index % distantHeights.Length];
            graphics.FillRectangle(distant, x, baseline - height, width, height);
            x += width - 5;
        }

        int[] nearWidths = [118, 82, 138, 96, 126, 74, 144];
        int[] nearHeights = [48, 74, 55, 88, 51, 69, 46];
        x = -30;
        for (int index = 0; x < Width; index++)
        {
            int width = nearWidths[index % nearWidths.Length];
            int height = nearHeights[index % nearHeights.Length];
            graphics.FillRectangle(near, x, baseline - height, width, height);
            x += width - 7;
        }

        int towerX = Math.Max(60, Width / 7);
        graphics.FillRectangle(distant, towerX, skylineTop + 22, 32, baseline - skylineTop);
        graphics.DrawLine(aerial, towerX + 16, skylineTop + 22, towerX + 16, skylineTop - 18);
        graphics.DrawLine(aerial, towerX + 16, skylineTop - 10, towerX + 4, skylineTop + 4);
        graphics.DrawLine(aerial, towerX + 16, skylineTop - 10, towerX + 28, skylineTop + 4);
    }
}

internal sealed class ActionButton : Button
{
    private static readonly Font TitleFont = new(
        "Segoe UI Semibold",
        10.5F,
        FontStyle.Bold,
        GraphicsUnit.Point);
    private static readonly Font SubtitleFont = new(
        "Segoe UI",
        8.5F,
        FontStyle.Regular,
        GraphicsUnit.Point);

    private readonly Color _accentColor;
    private bool _hovered;

    public ActionButton(
        string text,
        string subtitle,
        ActionIcon icon,
        Color accentColor)
    {
        Text = text;
        Subtitle = subtitle;
        Icon = icon;
        _accentColor = accentColor;
        BackColor = Color.FromArgb(29, 34, 43);
        ForeColor = Color.White;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        TabStop = true;
        UseVisualStyleBackColor = false;
        DoubleBuffered = true;
    }

    public string Subtitle { get; set; }

    public ActionIcon Icon { get; }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        Invalidate();
        base.OnGotFocus(eventArgs);
    }

    protected override void OnLostFocus(EventArgs eventArgs)
    {
        Invalidate();
        base.OnLostFocus(eventArgs);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Color background = !Enabled
            ? Color.FromArgb(24, 28, 35)
            : _hovered || Focused
                ? Color.FromArgb(42, 48, 59)
                : BackColor;
        Color foreground = Enabled
            ? ForeColor
            : Color.FromArgb(103, 112, 126);
        Color accent = Enabled
            ? _accentColor
            : Color.FromArgb(72, 78, 88);

        using SolidBrush backgroundBrush = new(background);
        using SolidBrush accentBrush = new(accent);
        using Pen borderPen = new(
            Focused && Enabled ? accent : Color.FromArgb(55, 63, 76),
            Focused && Enabled ? 2F : 1F);
        graphics.FillRectangle(backgroundBrush, ClientRectangle);
        graphics.FillRectangle(accentBrush, 0, 0, 5, Height);
        graphics.DrawRectangle(
            borderPen,
            0,
            0,
            Math.Max(0, Width - 1),
            Math.Max(0, Height - 1));

        Rectangle iconBounds = new(17, Math.Max(10, (Height - 32) / 2), 32, 32);
        DrawIcon(graphics, iconBounds, accent);

        Rectangle titleBounds = new(64, 10, Math.Max(0, Width - 76), 25);
        Rectangle subtitleBounds = new(64, 34, Math.Max(0, Width - 76), Math.Max(18, Height - 40));
        TextRenderer.DrawText(
            graphics,
            Text.ToUpperInvariant(),
            TitleFont,
            titleBounds,
            foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            graphics,
            Subtitle,
            SubtitleFont,
            subtitleBounds,
            Enabled ? Color.FromArgb(174, 184, 198) : Color.FromArgb(91, 98, 109),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    private void DrawIcon(Graphics graphics, Rectangle bounds, Color color)
    {
        using Pen pen = new(color, 2.4F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using SolidBrush brush = new(color);

        switch (Icon)
        {
            case ActionIcon.Play:
                Point[] triangle =
                [
                    new(bounds.Left + 8, bounds.Top + 5),
                    new(bounds.Right - 5, bounds.Top + bounds.Height / 2),
                    new(bounds.Left + 8, bounds.Bottom - 5)
                ];
                graphics.FillPolygon(brush, triangle);
                break;

            case ActionIcon.Chaos:
                pen.EndCap = LineCap.ArrowAnchor;
                graphics.DrawBezier(
                    pen,
                    bounds.Left + 3,
                    bounds.Top + 7,
                    bounds.Left + 14,
                    bounds.Top + 7,
                    bounds.Right - 13,
                    bounds.Bottom - 7,
                    bounds.Right - 3,
                    bounds.Bottom - 7);
                graphics.DrawBezier(
                    pen,
                    bounds.Left + 3,
                    bounds.Bottom - 7,
                    bounds.Left + 14,
                    bounds.Bottom - 7,
                    bounds.Right - 13,
                    bounds.Top + 7,
                    bounds.Right - 3,
                    bounds.Top + 7);
                break;

            case ActionIcon.Bridge:
                graphics.DrawEllipse(pen, bounds.Left + 2, bounds.Top + 11, 9, 9);
                graphics.DrawEllipse(pen, bounds.Right - 11, bounds.Top + 11, 9, 9);
                graphics.DrawLine(
                    pen,
                    bounds.Left + 11,
                    bounds.Top + 15,
                    bounds.Right - 11,
                    bounds.Top + 15);
                graphics.DrawArc(
                    pen,
                    bounds.Left + 7,
                    bounds.Top + 4,
                    bounds.Width - 14,
                    bounds.Height - 8,
                    180,
                    180);
                break;

            case ActionIcon.Stop:
                graphics.FillRectangle(
                    brush,
                    bounds.Left + 6,
                    bounds.Top + 6,
                    bounds.Width - 12,
                    bounds.Height - 12);
                break;
        }
    }
}

internal sealed class GtaMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(25, 30, 39);

    public override Color ImageMarginGradientBegin => Color.FromArgb(25, 30, 39);

    public override Color ImageMarginGradientMiddle => Color.FromArgb(25, 30, 39);

    public override Color ImageMarginGradientEnd => Color.FromArgb(25, 30, 39);

    public override Color MenuItemSelected => Color.FromArgb(43, 51, 63);

    public override Color MenuItemBorder => Color.FromArgb(55, 195, 188);

    public override Color MenuBorder => Color.FromArgb(54, 63, 77);

    public override Color SeparatorDark => Color.FromArgb(54, 63, 77);

    public override Color SeparatorLight => Color.FromArgb(54, 63, 77);
}
