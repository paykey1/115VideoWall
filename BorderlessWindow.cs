using System.Runtime.InteropServices;

namespace VideoWall115;

internal class BorderlessWindow : Form
{
    protected static readonly Padding ResizePadding = new(6);

    protected BorderlessWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        Padding = ResizePadding;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    }

    internal FormWindowState LastVisibleState { get; private set; } = FormWindowState.Normal;

    protected override void OnResize(EventArgs e)
    {
        if (WindowState != FormWindowState.Minimized)
            LastVisibleState = WindowState;
        base.OnResize(e);
    }

    internal virtual WindowPlacement CapturePlacement() => WindowPlacement.FromBounds(
        WindowState == FormWindowState.Normal ? Bounds : RestoreBounds,
        LastVisibleState == FormWindowState.Maximized);

    protected Control CreateWindowButtons(ToolTip? tooltip = null)
    {
        var compact = tooltip != null;
        var buttons = new FlowLayoutPanel
        {
            WrapContents = false, FlowDirection = FlowDirection.LeftToRight, AutoSize = true,
            Dock = DockStyle.Right, Padding = compact ? new Padding(4, 6, 5, 0) : new Padding(0, 4, 4, 0),
        };
        Add(ToolbarIcon.Minimize, "—", "Minimize", "最小化", () => WindowState = FormWindowState.Minimized);
        Add(ToolbarIcon.Maximize, "□", "Maximize / Restore", "最大化 / 还原", ToggleMaximize);
        Add(ToolbarIcon.CloseWindow, "×", "Close window", "关闭窗口", Close);
        return buttons;

        void Add(ToolbarIcon icon, string text, string description, string localized, Action action)
        {
            Button button;
            if (compact)
                button = new ToolbarIconButton(icon, description, tooltip);
            else
            {
                button = Palette.Button(text, localized);
                button.Size = new Size(34, 30);
                button.Margin = new Padding(1);
            }
            button.Click += (_, _) => action();
            buttons.Controls.Add(button);
        }
    }

    protected void MakeDraggable(Control area, Action? moved = null)
    {
        area.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                var before = Location;
                BeginDrag();
                if (Location != before)
                    moved?.Invoke();
            }
        };
        area.DoubleClick += (_, _) => ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
            WindowState = FormWindowState.Normal;
        else
            MaximizeOnCurrentScreen();
    }

    internal void MaximizeOnCurrentScreen()
    {
        MaximizedBounds = Screen.FromRectangle(Bounds).WorkingArea;
        WindowState = FormWindowState.Maximized;
    }

    internal void BeginDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x84 || message.Result.ToInt32() != 1 ||
            WindowState != FormWindowState.Normal || Padding == Padding.Empty)
            return;
        var xy = message.LParam.ToInt64();
        var local = PointToClient(new Point(unchecked((short)xy), unchecked((short)(xy >> 16))));
        var left = local.X < Padding.Left;
        var right = local.X >= ClientSize.Width - Padding.Right;
        var top = local.Y < Padding.Top;
        var bottom = local.Y >= ClientSize.Height - Padding.Bottom;
        var hit = (left, right, top, bottom) switch
        {
            (true, _, true, _) => 13,
            (_, true, true, _) => 14,
            (true, _, _, true) => 16,
            (_, true, _, true) => 17,
            (true, _, _, _) => 10,
            (_, true, _, _) => 11,
            (_, _, true, _) => 12,
            (_, _, _, true) => 15,
            _ => 1,
        };
        message.Result = new IntPtr(hit);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int msg, IntPtr wParam, IntPtr lParam);
}
