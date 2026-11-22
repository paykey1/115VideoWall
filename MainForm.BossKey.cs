using System.Runtime.InteropServices;

namespace VideoWall115;

internal sealed record BossShortcut(bool Enabled, Keys Gesture)
{
    internal static bool IsValid(Keys gesture)
    {
        var key = gesture & Keys.KeyCode;
        return ((gesture & Keys.Modifiers) != Keys.None || key is >= Keys.F1 and <= Keys.F24) &&
            key is not (Keys.None or Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin);
    }

    internal static string Format(Keys keys) => new KeysConverter().ConvertToInvariantString(keys) ?? "";
}

internal sealed partial class MainForm
{
    private readonly LocalJsonFile<BossShortcut> _shortcutFile;
    private BossShortcut _bossShortcut = new(false, Keys.Control | Keys.Alt | Keys.F10);
    private int _hotkeyId;
    private bool _bossHidden;
    private Form[] _bossWindows = [];

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private void LoadBossShortcut()
    {
        if (_shortcutFile.Read() is not { } saved || !BossShortcut.IsValid(saved.Gesture))
            return;
        _bossShortcut = saved;
        if (saved.Enabled && !RegisterShortcut(1, saved.Gesture, out var error))
            _log.Write("boss_shortcut_registration_failed", new { error });
        else if (saved.Enabled)
            _hotkeyId = 1;
    }

    private bool RegisterShortcut(int id, Keys gesture, out int error)
    {
        const uint noRepeat = 0x4000;
        var modifiers = noRepeat;
        if ((gesture & Keys.Alt) != 0) modifiers |= 1;
        if ((gesture & Keys.Control) != 0) modifiers |= 2;
        if ((gesture & Keys.Shift) != 0) modifiers |= 4;
        if (RegisterHotKey(Handle, id, modifiers, (uint)(gesture & Keys.KeyCode)))
        {
            error = 0;
            return true;
        }
        error = Marshal.GetLastWin32Error();
        return false;
    }

    private string? ProbeBossShortcut(Keys gesture)
    {
        if (!BossShortcut.IsValid(gesture))
            return "请按功能键或带 Ctrl、Alt、Shift 的组合键。";
        if (_hotkeyId != 0 && gesture == _bossShortcut.Gesture)
            return null;
        if (!RegisterShortcut(3, gesture, out var error))
            return RegistrationError(error);
        UnregisterHotKey(Handle, 3);
        return null;
    }

    internal string? SaveBossShortcut(BossShortcut next)
    {
        if (!BossShortcut.IsValid(next.Gesture))
            return "请设置有效快捷键。";
        var reuse = next.Enabled && _hotkeyId != 0 && _bossShortcut.Gesture == next.Gesture;
        var nextId = reuse ? _hotkeyId : _hotkeyId == 1 ? 2 : 1;
        if (next.Enabled && !reuse && !RegisterShortcut(nextId, next.Gesture, out var error))
            return RegistrationError(error);
        if (!_shortcutFile.TryWrite(next))
        {
            if (next.Enabled && !reuse)
                UnregisterHotKey(Handle, nextId);
            return "快捷键设置写入失败。";
        }
        if (_hotkeyId != 0 && !reuse)
            UnregisterHotKey(Handle, _hotkeyId);
        _hotkeyId = next.Enabled ? nextId : 0;
        _bossShortcut = next;
        return null;
    }

    private static string RegistrationError(int error) => error == 1409
        ? "这个快捷键已被占用，请换一个组合。"
        : $"无法注册这个快捷键（Windows 错误 {error}）。";

    private Panel BuildShortcutsPage(Label title)
    {
        var page = new Panel { Dock = DockStyle.Fill, Visible = false };
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24, 12, 16, 0), AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
        };
        var enable = new CheckBox { Text = "启用老板键", AutoSize = true, Checked = _bossShortcut.Enabled };
        var gesture = _bossShortcut.Gesture;
        var input = new ShortcutCaptureBox
        {
            ReadOnly = true, Width = 235, Text = BossShortcut.Format(gesture), AccessibleName = "按下自定义组合键",
        };
        var status = new Label
        {
            AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Palette.Muted,
            Margin = new Padding(3, 7, 0, 0),
            Text = _bossShortcut.Enabled && _hotkeyId == 0 ? "已保存的快捷键未注册，请检测或更换。" : "",
        };
        input.GestureCaptured += keys =>
        {
            gesture = keys;
            input.Text = BossShortcut.Format(gesture);
            status.Text = "";
        };
        var probe = Palette.Button("检测占用", "检测快捷键注册冲突");
        probe.Size = new Size(105, 32);
        probe.Click += (_, _) => status.Text = ProbeBossShortcut(gesture) ?? "此快捷键可用。";
        var save = Palette.Button("保存快捷键", "保存快捷键");
        save.Size = new Size(105, 32);
        save.Click += (_, _) => status.Text =
            SaveBossShortcut(new BossShortcut(enable.Checked, gesture)) ?? "已保存。";
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        actions.Controls.Add(probe);
        actions.Controls.Add(save);
        body.Controls.Add(enable);
        body.Controls.Add(new Label { Text = "点击输入框，然后按下组合键。", AutoSize = true, ForeColor = Palette.Muted });
        body.Controls.Add(input);
        body.Controls.Add(actions);
        body.Controls.Add(new Label
        {
            Text = "停止播放并静音，隐藏所有窗口。\n再次按下快捷键可恢复窗口，视频保持暂停。",
            AutoSize = true, ForeColor = Palette.Muted, Margin = new Padding(3, 9, 0, 0),
        });
        body.Controls.Add(status);
        page.Controls.Add(body);
        page.Controls.Add(title);
        return page;
    }

    private sealed class ShortcutCaptureBox : TextBox
    {
        internal event Action<Keys>? GestureCaptured;

        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            if ((keys & Keys.KeyCode) is Keys.Tab or Keys.Escape || !BossShortcut.IsValid(keys))
                return base.ProcessCmdKey(ref message, keys);
            GestureCaptured?.Invoke(keys);
            return true;
        }
    }

    protected override void WndProc(ref Message message)
    {
        const int hotkeyMessage = 0x0312;
        if (message.Msg == hotkeyMessage && _hotkeyId != 0 && message.WParam.ToInt32() == _hotkeyId && !_closing)
        {
            BeginInvoke((MethodInvoker)(async () => await ToggleBossModeAsync()));
            return;
        }
        base.WndProc(ref message);
    }

    internal async Task ToggleBossModeAsync()
    {
        if (_bossHidden)
        {
            _bossHidden = false;
            foreach (var window in _bossWindows)
                if (!window.IsDisposed)
                    window.Show();
            _bossWindows = [];
            Activate();
            return;
        }

        _bossHidden = true;
        var forms = Application.OpenForms.Cast<Form>().Where(window => window.Visible).ToArray();
        _bossWindows = forms.Where(window => !window.Modal && window is not CloudToastWindow).ToArray();
        var silencing = Task.WhenAll(_players.Concat(_tabs.Keys).Distinct().Select(pane => pane.SilenceAsync()));
        foreach (var window in forms.Reverse())
        {
            if (window.Modal || window is CloudToastWindow)
                window.Close();
            else
                window.Hide();
        }
        await silencing;
        await SyncPlaybackStatesAsync();
    }
}
