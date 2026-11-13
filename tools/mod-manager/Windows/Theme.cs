using System.ComponentModel;
using System.Drawing.Drawing2D;

/// <summary>
/// Colours and the few custom-painted controls. The palette follows the Windows light/dark app
/// preference; a Windows contrast theme replaces every colour with the user's system colours.
/// </summary>
static class Theme
{
    public static bool Dark, Contrast;
    public static Color Window, Side, Surface, Border, Ink, Muted, Accent, OnAccent, Good, Warn, Bad, Hover;
    public static readonly float Dpi = SystemDpi();
    static float SystemDpi() { using var g = Graphics.FromHwnd(IntPtr.Zero); return g.DpiX / 96f; }
    /// <summary>Logical pixels to device pixels.</summary>
    public static int Px(int value) => (int)Math.Round(value * Dpi);
    public static Font Font(float size, bool bold = false) => new(bold ? "Segoe UI Semibold" : "Segoe UI", size);
    static Color Rgb(int hex) => Color.FromArgb(255, Color.FromArgb(hex));

    public static void Load()
    {
        Contrast = SystemInformation.HighContrast; Dark = Application.IsDarkModeEnabled;
        if (Contrast)
        {
            Window = Side = Surface = SystemColors.Window; Border = Ink = Muted = Good = Warn = Bad = SystemColors.WindowText;
            Accent = Hover = SystemColors.Highlight; OnAccent = SystemColors.HighlightText;
        }
        else if (Dark)
        {
            Window = Rgb(0x15171B); Side = Rgb(0x0F1114); Surface = Rgb(0x1E2126); Border = Rgb(0x30353D); Hover = Rgb(0x2A2E35);
            Ink = Rgb(0xEEF0F2); Muted = Rgb(0xA3AAB3); Accent = Rgb(0xF0B14A); OnAccent = Rgb(0x1B1300);
            Good = Rgb(0x4CC46A); Warn = Rgb(0xF0B14A); Bad = Rgb(0xFF7B72);
        }
        else
        {
            Window = Rgb(0xF4F5F7); Side = Rgb(0xE9EBEF); Surface = Rgb(0xFFFFFF); Border = Rgb(0xD5D9E0); Hover = Rgb(0xE4E7EC);
            Ink = Rgb(0x1A1E23); Muted = Rgb(0x55606D); Accent = Rgb(0x8F5200); OnAccent = Rgb(0xFFFFFF);
            Good = Rgb(0x176B2E); Warn = Rgb(0x8F5200); Bad = Rgb(0xB3261E);
        }
    }
    public static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); var path = new GraphicsPath();
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}

/// <summary>One or more lines of text that end in an ellipsis instead of pushing the layout around.</summary>
sealed class Line : Label
{
    readonly int lines;
    public Line(string text = "", float size = 10, bool bold = false, int lines = 1)
    {
        this.lines = lines; AutoSize = false; AutoEllipsis = true; UseMnemonic = false; Text = text; Font = Theme.Font(size, bold);
        Dock = DockStyle.Top; Margin = new Padding(0, Theme.Px(1), 0, Theme.Px(1)); TextAlign = lines == 1 ? ContentAlignment.MiddleLeft : ContentAlignment.TopLeft; Fit();
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }
    void Fit() => Height = Font.Height * lines + Theme.Px(4);
}

enum Kind { Primary, Secondary, Nav, Link }

/// <summary>A real Button (keyboard, focus and UI Automation unchanged) with themed painting.</summary>
sealed class PillButton : Button
{
    readonly Kind kind; bool hover, down, selected, quiet;
    /// <summary>Paints a primary button as a secondary one while its action is optional.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Quiet { get => quiet; set { if (quiet == value) return; quiet = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }
    /// <summary>Large call-to-action sizing for the one main button.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Hero { get; init; }
    public PillButton(string text, string name, Kind kind)
    {
        this.kind = kind; Text = text; AccessibleName = name;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        Margin = new Padding(0, Theme.Px(3), Theme.Px(8), Theme.Px(3));
        if (kind == Kind.Nav) { AutoSize = false; Dock = DockStyle.Top; Margin = new Padding(0, 0, 0, Theme.Px(4)); Fit(); }
        else { AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; }
    }
    void Fit() { if (kind == Kind.Nav) Height = Font.Height + Theme.Px(18); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }
    const TextFormatFlags Measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
    public override Size GetPreferredSize(Size proposed)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, Measure);
        return kind == Kind.Link ? new Size(text.Width + Theme.Px(8), text.Height + Theme.Px(8))
            : Hero ? new Size(Math.Max(Theme.Px(150), text.Width + Theme.Px(44)), text.Height + Theme.Px(24))
            : new Size(text.Width + Theme.Px(30), text.Height + Theme.Px(14));
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Parent?.PerformLayout(); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Color behind = Parent?.BackColor ?? Theme.Window; g.Clear(behind);
        var box = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
        Color fill = behind, fore = Theme.Ink; Color? edge = null;
        switch (kind == Kind.Primary && quiet ? Kind.Secondary : kind)
        {
            case Kind.Primary:
                fill = !Enabled ? Theme.Mix(behind, Theme.Border, Theme.Contrast ? 0 : 0.6f) : down ? Theme.Mix(Theme.Accent, behind, 0.25f) : hover ? Theme.Mix(Theme.Accent, Theme.Ink, 0.12f) : Theme.Accent;
                fore = Enabled ? Theme.OnAccent : Theme.Contrast ? SystemColors.GrayText : Theme.Muted; if (!Enabled && Theme.Contrast) edge = SystemColors.GrayText; break;
            case Kind.Secondary:
                fill = Enabled && (hover || down) ? Theme.Hover : behind; edge = Enabled ? Theme.Border : Theme.Mix(behind, Theme.Border, 0.5f);
                fore = !Enabled ? (Theme.Contrast ? SystemColors.GrayText : Theme.Mix(behind, Theme.Muted, 0.6f)) : Theme.Contrast && (hover || down) ? Theme.OnAccent : Theme.Ink; break;
            case Kind.Nav:
                fill = selected ? (Theme.Contrast ? Theme.Accent : Theme.Surface) : hover ? Theme.Hover : behind;
                fore = Theme.Contrast && (selected || hover) ? Theme.OnAccent : selected ? Theme.Ink : Theme.Muted; break;
            case Kind.Link:
                fore = !Enabled ? (Theme.Contrast ? SystemColors.GrayText : Theme.Muted) : Theme.Contrast ? SystemColors.HotTrack : Theme.Accent; break;
        }
        using (var path = Theme.Round(box, Theme.Px(7)))
        {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            if (edge is Color c) { using var pen = new Pen(c, Math.Max(1f, Theme.Dpi)); g.DrawPath(pen, path); }
        }
        if (kind == Kind.Nav && selected && !Theme.Contrast) { using var bar = new SolidBrush(Theme.Accent); using var mark = Theme.Round(new RectangleF(Theme.Px(2), Height / 2f - Theme.Px(8), Theme.Px(4), Theme.Px(16)), Theme.Px(2)); g.FillPath(bar, mark); }
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | (kind == Kind.Nav ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        var area = kind == Kind.Nav ? new Rectangle(Theme.Px(16), 0, Width - Theme.Px(22), Height) : ClientRectangle;
        using var font = kind == Kind.Link && hover && Enabled ? new Font(Font, FontStyle.Underline) : null;
        TextRenderer.DrawText(g, Text, font ?? Font, area, fore, flags);
        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen((kind == Kind.Primary && !quiet) || Theme.Contrast ? Theme.Ink : Theme.Accent, Math.Max(2f, Theme.Dpi * 1.5f));
            using var path = Theme.Round(new RectangleF(2, 2, Width - 4.5f, Height - 4.5f), Theme.Px(6)); g.DrawPath(ring, path);
        }
    }
}

/// <summary>A rounded surface that stacks its children. Children inherit its background.</summary>
sealed class Card : TableLayoutPanel
{
    public Card(int columns = 1)
    {
        ColumnCount = columns; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top;
        Padding = new Padding(Theme.Px(16), Theme.Px(12), Theme.Px(16), Theme.Px(12)); Margin = new Padding(0, 0, 0, Theme.Px(10));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    /// <summary>Accent-tinted surface for something that wants attention.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Highlight { get; init; }
    public void Restyle() => BackColor = Highlight && !Theme.Contrast ? Theme.Mix(Theme.Surface, Theme.Accent, 0.14f) : Theme.Surface;
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Window);
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Px(9));
        using var brush = new SolidBrush(BackColor); g.FillPath(brush, path);
        using var pen = new Pen(Highlight ? Theme.Accent : Theme.Border, Math.Max(1f, Theme.Dpi)); g.DrawPath(pen, path);
    }
}

/// <summary>A small status pill. The wording carries the meaning; colour only reinforces it.</summary>
sealed class Badge : Label
{
    Color tone = Color.Empty;
    public Badge(float size = 8.5f) { AutoSize = true; UseMnemonic = false; Font = Theme.Font(size, true); Padding = new Padding(Theme.Px(9), Theme.Px(3), Theme.Px(9), Theme.Px(3)); Margin = new Padding(Theme.Px(10), Theme.Px(4), 0, Theme.Px(4)); Anchor = AnchorStyles.Left; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    public void Set(string text, Color colour) { tone = colour; Text = text; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; Color behind = Parent?.BackColor ?? Theme.Window; g.Clear(behind);
        Color colour = tone.IsEmpty ? Theme.Muted : tone;
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f);
        using var brush = new SolidBrush(Theme.Contrast ? behind : Theme.Mix(behind, colour, 0.16f)); g.FillPath(brush, path);
        using var pen = new Pen(Theme.Contrast ? colour : Theme.Mix(behind, colour, 0.45f), Math.Max(1f, Theme.Dpi)); g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, colour, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}
