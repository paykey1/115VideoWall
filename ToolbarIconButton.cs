using System.Drawing.Drawing2D;

namespace VideoWall115;

internal enum ToolbarIcon
{
    Back, Forward, Refresh, Files, Wall, Logs, NewTab, CloseTab, Minimize, Maximize, CloseWindow,
    Play, Pause, PlayAll, PauseAll, BackTen, ForwardTen, Volume, Muted, PopOut, Dock, Settings, Star, Starred, History, Reopen,
}

internal sealed class ToolbarIconButton : Button
{
    private ToolbarIcon _icon;
    private readonly ToolTip? _tooltip;
    private string _description;
    internal ToolbarIcon Icon => _icon;

    public ToolbarIconButton(ToolbarIcon icon, string description, ToolTip? tooltip = null)
    {
        _icon = icon;
        _tooltip = tooltip;
        _description = description;
        Size = new Size(28, 28);
        Margin = new Padding(1, 0, 1, 0);
        AccessibleName = description;
        UseVisualStyleBackColor = false;
        FlatStyle = FlatStyle.Flat;
        BackColor = Palette.Panel;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = icon == ToolbarIcon.CloseWindow
            ? Color.FromArgb(147, 47, 55) : Palette.ButtonFill;
        FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 72, 89);
        tooltip?.SetToolTip(this, description);
    }

    internal void SetIcon(ToolbarIcon icon, string description)
    {
        if (_icon == icon && _description == description)
            return;
        _icon = icon;
        _description = description;
        AccessibleName = description;
        _tooltip?.SetToolTip(this, description);
        Invalidate();
    }

    internal void SetHighlighted(bool highlighted)
    {
        BackColor = highlighted ? Color.FromArgb(34, 68, 100) : Palette.Panel;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Min(ClientSize.Width, ClientSize.Height) * 0.77f / 24;
        graphics.TranslateTransform((ClientSize.Width - 24 * scale) / 2,
            (ClientSize.Height - 24 * scale) / 2);
        graphics.ScaleTransform(scale, scale);
        using var pen = new Pen(Enabled ? Palette.Text : Color.FromArgb(103, 120, 139), 1.85f)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
        };
        switch (_icon)
        {
            case ToolbarIcon.Back:
                Line(13, 6, 7, 12, 13, 18);
                Line(7, 12, 19, 12);
                break;
            case ToolbarIcon.Forward:
                Line(11, 6, 17, 12, 11, 18);
                Line(17, 12, 5, 12);
                break;
            case ToolbarIcon.Refresh:
                graphics.DrawArc(pen, 5, 5, 14, 14, 22, 297);
                Line(19, 5, 19, 10, 14, 10);
                break;
            case ToolbarIcon.Files:
                Line(3, 18, 3, 6, 9, 6, 11, 9, 21, 9, 21, 18, 3, 18);
                break;
            case ToolbarIcon.Wall:
                Box(4, 4, 16, 16);
                Line(12, 4, 12, 20);
                Line(4, 12, 20, 12);
                break;
            case ToolbarIcon.Logs:
                Line(6, 3, 14, 3, 19, 8, 19, 21, 6, 21, 6, 3);
                Line(14, 3, 14, 8, 19, 8);
                Line(9, 12, 16, 12);
                Line(9, 16, 16, 16);
                break;
            case ToolbarIcon.Settings:
                graphics.DrawEllipse(pen, 5, 5, 14, 14);
                graphics.DrawEllipse(pen, 9, 9, 6, 6);
                Line(12, 2, 12, 5);
                Line(12, 19, 12, 22);
                Line(2, 12, 5, 12);
                Line(19, 12, 22, 12);
                Line(5, 5, 7, 7);
                Line(17, 17, 19, 19);
                Line(5, 19, 7, 17);
                Line(17, 7, 19, 5);
                break;
            case ToolbarIcon.Star:
            case ToolbarIcon.Starred:
                var star = new[] { new Point(12, 2), new Point(15, 8), new Point(22, 9),
                    new Point(17, 14), new Point(18, 21), new Point(12, 18), new Point(6, 21),
                    new Point(7, 14), new Point(2, 9), new Point(9, 8) };
                if (_icon == ToolbarIcon.Starred)
                {
                    using var fill = new SolidBrush(Color.FromArgb(246, 193, 86));
                    graphics.FillPolygon(fill, star);
                }
                else
                    graphics.DrawPolygon(pen, star);
                break;
            case ToolbarIcon.History:
                graphics.DrawEllipse(pen, 4, 4, 16, 16);
                Line(12, 7, 12, 12, 16, 15);
                break;
            case ToolbarIcon.Reopen:
                Line(9, 7, 4, 12, 9, 17);
                graphics.DrawArc(pen, 6, 5, 14, 14, -94, 226);
                break;
            case ToolbarIcon.NewTab:
                Line(5, 12, 19, 12);
                Line(12, 5, 12, 19);
                break;
            case ToolbarIcon.CloseTab:
            case ToolbarIcon.CloseWindow:
                Line(7, 7, 17, 17);
                Line(17, 7, 7, 17);
                break;
            case ToolbarIcon.Minimize:
                Line(6, 13, 18, 13);
                break;
            case ToolbarIcon.Maximize:
                Box(6, 6, 12, 12);
                break;
            case ToolbarIcon.Play:
                using (var fill = new SolidBrush(pen.Color))
                    graphics.FillPolygon(fill, new[] { new Point(8, 4), new Point(20, 12), new Point(8, 20) });
                break;
            case ToolbarIcon.Pause:
                Line(8, 5, 8, 19);
                Line(16, 5, 16, 19);
                break;
            case ToolbarIcon.PlayAll:
                Line(3, 5, 12, 12, 3, 19, 3, 5);
                Line(13, 6, 21, 12, 13, 18);
                break;
            case ToolbarIcon.PauseAll:
                Box(3, 3, 18, 18);
                Line(9, 7, 9, 17);
                Line(15, 7, 15, 17);
                break;
            case ToolbarIcon.BackTen:
                Line(8, 5, 4, 9, 8, 13);
                Line(4, 9, 19, 9);
                Ten();
                break;
            case ToolbarIcon.ForwardTen:
                Line(16, 5, 20, 9, 16, 13);
                Line(5, 9, 20, 9);
                Ten();
                break;
            case ToolbarIcon.Volume:
            case ToolbarIcon.Muted:
                Line(4, 9, 8, 9, 13, 5, 13, 19, 8, 15, 4, 15, 4, 9);
                if (_icon == ToolbarIcon.Muted)
                {
                    Line(17, 10, 22, 15);
                    Line(22, 10, 17, 15);
                }
                else
                    graphics.DrawArc(pen, 8, 5, 14, 14, 308, 104);
                break;
            case ToolbarIcon.PopOut:
                Line(7, 17, 19, 5);
                Line(12, 5, 19, 5, 19, 12);
                break;
            case ToolbarIcon.Dock:
                Line(18, 6, 6, 18);
                Line(6, 11, 6, 18, 13, 18);
                break;
        }
        graphics.Restore(state);

        void Line(params int[] coordinates)
        {
            var points = new Point[coordinates.Length / 2];
            for (var i = 0; i < points.Length; i++)
                points[i] = new Point(coordinates[i * 2], coordinates[i * 2 + 1]);
            graphics.DrawLines(pen, points);
        }

        void Ten()
        {
            using var font = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(pen.Color);
            using var format = new StringFormat { Alignment = StringAlignment.Center };
            graphics.DrawString("10", font, brush, new RectangleF(0, 13, 24, 10), format);
        }

        void Box(int x, int y, int width, int height) => graphics.DrawRectangle(pen, x, y, width, height);
    }
}
