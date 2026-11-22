using System.Runtime.InteropServices;

namespace VideoWall115;

internal sealed class WallWindow : BorderlessWindow
{
    private readonly Panel _toolbar;
    private readonly FlowLayoutPanel _actions;
    private readonly Button _fullscreen;
    private readonly ToolbarIconButton _playAll;
    private readonly ToolbarIconButton _pauseAll;
    private readonly ToolTip _groupTips = new();
    private readonly Label _empty;
    private Rectangle _normalBounds;
    private FormWindowState _normalState;
    private long _lastShortcut;
    private readonly System.Windows.Forms.Timer _pointerTimer = new() { Interval = 60 };
    private Point _pointerOnEntry;
    private bool _pointerMoved;

    protected override bool ShowWithoutActivation => true;

    internal bool Fullscreen { get; private set; }
    internal bool ChromeVisible => _toolbar.Visible;
    internal event Action? FullscreenChanged;
    internal event Action<Point>? TitleDropped;
    internal event Action? ClosePlayersRequested;
    internal event Action<Point?, long>? PointerUpdated;
    internal bool ExitRequested { get; set; }

    internal TableLayoutPanel Grid { get; }
    internal List<BrowserPane> Players { get; } = new();

    public WallWindow(TableLayoutPanel grid, Func<string, Task> all, int number = 1)
    {
        Grid = grid;
        Text = $"115 视频墙 {number}";
        ClientSize = new Size(1200, 720);
        MinimumSize = new Size(640, 360);
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Font = new Font("Microsoft YaHei UI", 9);
        _toolbar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Palette.Panel };
        _actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 3, 0, 0),
        };
        var title = new Label
        {
            Text = Text, ForeColor = Palette.Text, AutoSize = true, Margin = new Padding(4, 9, 12, 0),
        };
        _actions.Controls.Add(title);
        _toolbar.Controls.Add(_actions);
        _toolbar.Controls.Add(CreateWindowButtons());
        MakeDraggable(_actions, () => TitleDropped?.Invoke(Cursor.Position));
        MakeDraggable(title, () => TitleDropped?.Invoke(Cursor.Position));
        _fullscreen = Add("全屏 (F11)", (_, _) => SetFullscreen(!Fullscreen));
        _playAll = AddGroupIcon(ToolbarIcon.PlayAll, "全体播放", async (_, _) => await all("play"));
        _pauseAll = AddGroupIcon(ToolbarIcon.PauseAll, "全体暂停", async (_, _) => await all("pause"));
        _empty = new Label
        {
            Text = "在 115 文件窗口中打开视频，画面会出现在这里。",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Palette.Muted, BackColor = Color.Black,
        };
        Controls.Add(grid);
        Controls.Add(_empty);
        Controls.Add(_toolbar);
        _pointerTimer.Tick += TrackPointer;
        Disposed += (_, _) =>
        {
            _pointerTimer.Dispose();
            _groupTips.Dispose();
        };
        FormClosing += (_, e) =>
        {
            if (!ExitRequested && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                ClosePlayersRequested?.Invoke();
            }
        };
    }

    private Button Add(string title, EventHandler click)
    {
        var button = Palette.Button(title, title);
        button.AutoSize = true;
        button.MinimumSize = new Size(75, 31);
        button.Click += click;
        _actions.Controls.Add(button);
        return button;
    }

    private ToolbarIconButton AddGroupIcon(ToolbarIcon icon, string description, EventHandler click)
    {
        var button = new ToolbarIconButton(icon, description, _groupTips) { Margin = new Padding(1, 5, 1, 0) };
        button.Click += click;
        _actions.Controls.Add(button);
        return button;
    }

    internal void SetGroupPlayback(bool allPlaying, bool allPaused)
    {
        _playAll.SetHighlighted(allPlaying);
        _pauseAll.SetHighlighted(allPaused);
    }

    internal void PlaceBeside(Form browser)
    {
        var primary = Screen.FromControl(browser);
        var display = Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName != primary.DeviceName);
        if (display != null)
        {
            var area = display.WorkingArea;
            Bounds = new Rectangle(area.Left + 24, area.Top + 24,
                Math.Max(MinimumSize.Width, area.Width - 48), Math.Max(MinimumSize.Height, area.Height - 48));
            return;
        }
        var working = primary.WorkingArea;
        var width = Math.Clamp(working.Width * 3 / 5, MinimumSize.Width, 1450);
        var height = Math.Clamp(working.Height - 100, MinimumSize.Height, 900);
        Bounds = new Rectangle(working.Right - width - 12, working.Top + 35, width, height);
    }

    internal override WindowPlacement CapturePlacement() => Fullscreen
        ? WindowPlacement.FromBounds(_normalBounds, _normalState == FormWindowState.Maximized, fullscreen: true)
        : base.CapturePlacement();

    internal void SetHasPlayers(bool active)
    {
        _fullscreen.Enabled = active;
        _empty.Visible = !active;
        if (!active)
            SetFullscreen(false);
    }

    internal bool CanHandleShortcut(Keys keys) =>
        keys == Keys.F11 && _fullscreen.Enabled || keys == Keys.Escape && Fullscreen;

    internal void HandleShortcut(Keys keys)
    {
        if (!CanHandleShortcut(keys))
            return;
        if (keys == Keys.F11)
        {
            var now = Environment.TickCount64;
            if (now - _lastShortcut < 350)
                return;
            _lastShortcut = now;
        }
        SetFullscreen(keys == Keys.F11 && !Fullscreen);
    }

    internal void SetFullscreen(bool enabled)
    {
        if (enabled == Fullscreen)
            return;
        SuspendLayout();
        try
        {
            Fullscreen = enabled;
            if (enabled)
            {
                var screen = Screen.FromControl(this).Bounds;
                _normalState = WindowState;
                _normalBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                WindowState = FormWindowState.Normal;
                Padding = Padding.Empty;
                Bounds = screen;
            }
            else
            {
                Padding = ResizePadding;
                Bounds = _normalBounds;
                WindowState = _normalState;
            }
            _toolbar.Visible = !enabled;
            if (enabled)
            {
                _pointerOnEntry = Cursor.Position;
                _pointerMoved = false;
                _pointerTimer.Start();
            }
            else
                _pointerTimer.Stop();
            FullscreenChanged?.Invoke();
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void TrackPointer(object? sender, EventArgs e)
    {
        var position = Cursor.Position;
        _pointerMoved |= position != _pointerOnEntry;
        var overWall = _pointerMoved && IsAt(position);
        PointerUpdated?.Invoke(overWall ? position : null, Environment.TickCount64);
    }

    internal bool ContainsDropPoint(Point point) =>
        Visible && WindowState != FormWindowState.Minimized && Bounds.Contains(point);

    internal bool IsAt(Point point) => ContainsDropPoint(point) &&
        GetAncestor(WindowFromPoint(point), 2) == Handle;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (!CanHandleShortcut(keyData))
            return base.ProcessCmdKey(ref message, keyData);
        if ((message.LParam.ToInt64() & (1L << 30)) == 0)
            HandleShortcut(keyData);
        return true;
    }
}
