using System.Drawing.Drawing2D;

namespace VideoWall115;

internal sealed class BrowserTabPage(string title) : Panel
{
    internal string Title { get; set; } = title;
    internal Bitmap? Icon { get; set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Icon?.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class BrowserTabs : UserControl
{
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Palette.Canvas };
    private readonly List<BrowserTabPage> _pages = new();
    private BrowserTabPage? _active;
    internal TabStrip Strip { get; }
    internal IReadOnlyList<BrowserTabPage> Pages => _pages;
    internal Rectangle PageBounds => _content.Bounds;
    internal event Action? SelectionChanged;
    internal event Action<BrowserTabPage>? CloseRequested;

    internal BrowserTabs()
    {
        Dock = DockStyle.Fill;
        Strip = new TabStrip(this);
        Controls.Add(_content);
        Controls.Add(Strip);
    }

    internal int SelectedIndex
    {
        get => _active == null ? -1 : _pages.IndexOf(_active);
        set => SelectedTab = _pages[value];
    }

    internal BrowserTabPage? SelectedTab
    {
        get => _active;
        set
        {
            if (value == _active)
                return;
            var previous = _active;
            _active = value;
            if (previous != null)
                previous.Visible = false;
            if (value != null)
            {
                value.BringToFront();
                value.Visible = true;
            }
            Strip.Reveal(SelectedIndex);
            SelectionChanged?.Invoke();
        }
    }

    internal void Insert(int index, BrowserTabPage page, bool select)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pages.Insert(index, page);
        _content.Controls.Add(page);
        if (select || _active == null)
            SelectedTab = page;
        Strip.Reveal(SelectedIndex);
    }

    internal void Remove(BrowserTabPage page)
    {
        var index = _pages.IndexOf(page);
        _pages.RemoveAt(index);
        if (page == _active)
            SelectedTab = _pages.Count == 0 ? null : _pages[Math.Min(index, _pages.Count - 1)];
        _content.Controls.Remove(page);
        Strip.Reveal(SelectedIndex);
    }

    internal int IndexOf(BrowserTabPage page) => _pages.IndexOf(page);

    internal sealed class TabStrip : Control
    {
        private const int PinnedWidth = 80;
        private readonly BrowserTabs _owner;
        private readonly ToolTip _tip = new() { InitialDelay = 500, ReshowDelay = 250 };
        private BrowserTabPage? _hover, _pressed;
        private bool _hoverClose, _pressClose;
        private Point _pressPoint;
        private int _offset;

        internal TabStrip(BrowserTabs owner)
        {
            _owner = owner;
            Dock = DockStyle.Top;
            Height = 35;
            BackColor = Palette.Canvas;
            AccessibleName = "浏览器标签";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private int TabWidth => Math.Clamp((ClientSize.Width - PinnedWidth) / Math.Max(1, _owner.Pages.Count - 1), 105, 218);
        private int TotalWidth => _owner.Pages.Count == 0 ? 0 : PinnedWidth + (_owner.Pages.Count - 1) * TabWidth;
        private bool Overflow => TotalWidth > ClientSize.Width;
        private int ViewWidth => Math.Max(1, ClientSize.Width - (Overflow ? 46 : 0));
        private int Limit => Math.Max(0, TotalWidth - ViewWidth);

        internal Rectangle GetTabRect(int index) =>
            new(index == 0 ? 3 : PinnedWidth + (index - 1) * TabWidth - _offset,
                4, (index == 0 ? 72 : TabWidth - 4), Height - 7);

        private static Rectangle CloseBounds(Rectangle tab) =>
            new(tab.Right - 25, tab.Top + (tab.Height - 20) / 2, 20, 20);

        internal void Reveal(int index)
        {
            _offset = Math.Clamp(_offset, 0, Limit);
            if (index == 0)
                _offset = 0;
            else if (index > 0)
            {
                var tab = GetTabRect(index);
                if (tab.Left < PinnedWidth)
                    _offset = Math.Max(0, _offset + tab.Left - PinnedWidth);
                else if (tab.Right > ViewWidth)
                    _offset = Math.Min(Limit, _offset + tab.Right - ViewWidth);
            }
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_owner != null)
                Reveal(_owner.SelectedIndex);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var edge = new Pen(Palette.ButtonFill);
            graphics.DrawLine(edge, 0, Height - 1, Width, Height - 1);
            var saved = graphics.Save();
            for (var index = 0; index < _owner.Pages.Count; index++)
            {
                var page = _owner.Pages[index];
                var tab = GetTabRect(index);
                var left = index == 0 ? 0 : PinnedWidth;
                if (tab.Right <= left || tab.Left >= ViewWidth)
                    continue;
                graphics.SetClip(new Rectangle(left, 0, ViewWidth - left, Height));
                var active = page == _owner.SelectedTab;
                var hovered = page == _hover;
                if (active || hovered)
                {
                    using var fill = new SolidBrush(active ? Color.FromArgb(45, 60, 77) : Color.FromArgb(31, 43, 56));
                    FillRound(graphics, tab, fill, 8);
                }

                var icon = new Rectangle(tab.Left + 10, tab.Top + (tab.Height - 16) / 2, 16, 16);
                if (page.Icon != null)
                    graphics.DrawImage(page.Icon, icon);
                else
                {
                    using var pen = new Pen(index == 0 ? Color.FromArgb(116, 191, 238) : Palette.Muted);
                    graphics.DrawEllipse(pen, icon.Left + 2, icon.Top + 2, 12, 12);
                    graphics.DrawEllipse(pen, icon.Left + 5, icon.Top + 2, 6, 12);
                    graphics.DrawLine(pen, icon.Left + 2, icon.Top + 8, icon.Right - 2, icon.Top + 8);
                }

                var text = new Rectangle(icon.Right + 7, tab.Top, tab.Right - icon.Right - (index == 0 ? 10 : 34), tab.Height);
                TextRenderer.DrawText(graphics, page.Title, Font, text, active ? Palette.Text : Palette.Muted,
                    TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                if (index == 0)
                    continue;
                var close = CloseBounds(tab);
                if (hovered && _hoverClose)
                {
                    using var hover = new SolidBrush(Color.FromArgb(69, 85, 102));
                    FillRound(graphics, close, hover, 9);
                }
                using var cross = new Pen(active || hovered ? Palette.Text : Palette.Muted, 1.4f);
                graphics.DrawLine(cross, close.Left + 7, close.Top + 7, close.Right - 7, close.Bottom - 7);
                graphics.DrawLine(cross, close.Right - 7, close.Top + 7, close.Left + 7, close.Bottom - 7);
            }
            graphics.Restore(saved);

            if (Overflow)
            {
                TextRenderer.DrawText(graphics, "‹", Font, new Rectangle(ViewWidth, 0, 23, Height),
                    _offset > 0 ? Palette.Text : Palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(graphics, "›", Font, new Rectangle(ViewWidth + 23, 0, 23, Height),
                    _offset < Limit ? Palette.Text : Palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private static void FillRound(Graphics graphics, Rectangle bounds, Brush brush, int radius)
        {
            var diameter = radius * 2;
            using var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            graphics.FillPath(brush, path);
        }

        private int Hit(Point point)
        {
            if (point.X < 0 || point.X >= ViewWidth)
                return -1;
            for (var i = 0; i < _owner.Pages.Count; i++)
                if ((i == 0 || point.X >= PinnedWidth) && GetTabRect(i).Contains(point))
                    return i;
            return -1;
        }

        private void ScrollTabs(int amount)
        {
            _offset = Math.Clamp(_offset + amount, 0, Limit);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollTabs(-Math.Sign(e.Delta) * TabWidth);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Overflow && e.X >= ViewWidth)
            {
                ScrollTabs(e.X < ViewWidth + 23 ? -TabWidth : TabWidth);
                return;
            }
            var index = Hit(e.Location);
            if (index < 0)
                return;
            var page = _owner.Pages[index];
            if (e.Button == MouseButtons.Middle && index > 0)
                _owner.CloseRequested?.Invoke(page);
            if (e.Button != MouseButtons.Left)
                return;
            _pressed = page;
            _pressPoint = e.Location;
            _pressClose = index > 0 && CloseBounds(GetTabRect(index)).Contains(e.Location);
            Capture = true;
            if (!_pressClose)
                _owner.SelectedTab = page;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = Hit(e.Location);
            var page = index < 0 ? null : _owner.Pages[index];
            var close = index > 0 && CloseBounds(GetTabRect(index)).Contains(e.Location);
            if (page != _hover || close != _hoverClose)
            {
                _hover = page;
                _hoverClose = close;
                _tip.SetToolTip(this, close ? "关闭标签" : page?.Title);
                Invalidate();
            }
            if (_pressed == null || _pressClose || index <= 0 ||
                Math.Abs(e.X - _pressPoint.X) < SystemInformation.DragSize.Width)
                return;
            var from = _owner.IndexOf(_pressed);
            if (from <= 0 || from == index)
                return;
            var target = GetTabRect(index);
            if (from < index ? e.X < target.Left + target.Width / 2 : e.X > target.Left + target.Width / 2)
                return;
            _owner._pages.RemoveAt(from);
            _owner._pages.Insert(index, _pressed);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = null;
            _hoverClose = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var index = Hit(e.Location);
            var pressed = _pressed;
            var close = _pressClose;
            _pressed = null;
            Capture = false;
            if (e.Button == MouseButtons.Left && close && index > 0 && _owner.Pages[index] == pressed &&
                CloseBounds(GetTabRect(index)).Contains(e.Location))
                _owner.CloseRequested?.Invoke(pressed!);
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture)
                _pressed = null;
            base.OnMouseCaptureChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
