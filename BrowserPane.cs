using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace VideoWall115;

internal sealed class BrowserPane : UserControl
{
    private readonly List<CoreWebView2Frame> _frames = new();
    private readonly Label _caption;
    private readonly ToolbarIconButton _mute;
    private readonly ToolTip _controlTips = new() { InitialDelay = 320, ReshowDelay = 120 };
    private readonly RunLog _log;
    private readonly Panel _top;
    private bool _initialMute;
    private CancellationTokenSource? _pageFullscreen;
    private bool _immersive;
    private bool _hovered;
    private long? _hideToolbarAt;
    private Point? _dragOrigin;

    // The 115 player keeps the webpage-wide and monitor-wide buttons separate.
    private const string EnableWebFullscreen = """
        (() => {
          const button = document.querySelector('[rel="web_fullscreen"]');
          if (!button) return false;
          const enabled = () => Boolean(button.querySelector('.iop-browser-exitfull'));
          if (!enabled()) button.click();
          return enabled();
        })()
        """;

    private const string DismissPlayerPromotion = """
        (() => {
          const close = () => {
            const box = document.getElementById('js_common_mini-dialog');
            const button = box?.querySelector('a[rel="close"]');
            if (!button || !box.getClientRects().length || getComputedStyle(box).visibility === 'hidden')
              return false;
            button.click();
            return !box.isConnected || !box.getClientRects().length;
          };
          if (close()) return;
          let checks = 0;
          const watcher = setInterval(() => {
            if (close() || ++checks >= 120) clearInterval(watcher);
          }, 500);
        })()
        """;

    internal event Action<BrowserPane>? DetachRequested;
    internal event Action<BrowserPane>? CloseRequested;
    internal event Func<string, Task>? GroupPlaybackRequested;
    internal event Func<Keys, bool>? ShortcutRequested;
    internal bool IsPlayer { get; private set; }
    internal PlayerWindow? Popout { get; set; }
    internal ToolbarIconButton MoveButton { get; }
    internal ToolbarIconButton PlaybackButton { get; }
    internal ToolbarIconButton PlayAllButton { get; }
    internal ToolbarIconButton PauseAllButton { get; }
    internal bool? Playing { get; private set; }
    internal bool ToolbarVisible => _top.Visible;
    internal IntPtr ToolbarHandle => _top.Handle;
    internal WebView2 View { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    internal CoreWebView2 Core => View.CoreWebView2;
    internal int Id { get; }

    public BrowserPane(int id, RunLog log)
    {
        Id = id;
        _log = log;
        Dock = DockStyle.Fill;
        BackColor = Color.Black;
        MinimumSize = new Size(150, 100);
        _top = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Palette.Panel, Visible = false };
        _caption = new Label
        {
            Text = $"视频 {Id}", ForeColor = Palette.Text, Dock = DockStyle.Fill,
            Padding = new Padding(9, 0, 4, 0), TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        var controls = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false,
            Dock = DockStyle.Right, Padding = new Padding(0, 2, 4, 0),
        };
        PlaybackButton = Add(controls, ToolbarIcon.Play, "播放", async (_, _) => await CommandAsync("toggle"));
        Add(controls, ToolbarIcon.BackTen, "后退 10 秒", async (_, _) => await CommandAsync("back"));
        Add(controls, ToolbarIcon.ForwardTen, "前进 10 秒", async (_, _) => await CommandAsync("forward"));
        PlayAllButton = Add(controls, ToolbarIcon.PlayAll, "全体播放", async (_, _) => await RequestGroupAsync("play"));
        PauseAllButton = Add(controls, ToolbarIcon.PauseAll, "全体暂停", async (_, _) => await RequestGroupAsync("pause"));
        _mute = Add(controls, ToolbarIcon.Volume, "静音", (_, _) => ToggleMute());
        MoveButton = Add(controls, ToolbarIcon.PopOut, "移到独立窗口", (_, _) => DetachRequested?.Invoke(this));
        Add(controls, ToolbarIcon.CloseTab, "关闭视频", (_, _) => CloseRequested?.Invoke(this));
        SizeChanged += (_, _) =>
        {
            var buttonWidth = Math.Clamp((ClientSize.Width - controls.Padding.Horizontal) /
                controls.Controls.Count - 2, 15, 28);
            foreach (Control button in controls.Controls)
                button.Width = buttonWidth;
        };
        _top.Controls.Add(_caption);
        _top.Controls.Add(controls);
        Controls.Add(View);
        Controls.Add(_top);
        View.KeyDown += (_, e) =>
        {
            if (ShortcutRequested?.Invoke(e.KeyData) == true)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        Disposed += (_, _) =>
        {
            CancelPageFullscreen();
            _controlTips.Dispose();
        };
        _caption.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;
            if (Popout is PlayerWindow window)
                window.BeginDrag();
            else if (IsPlayer)
            {
                _dragOrigin = _caption.PointToScreen(e.Location);
                Capture = true;
            }
        };
        _caption.Cursor = Cursors.SizeAll;
        _controlTips.SetToolTip(_caption, "拖动标题可将视频移到另一面墙，或在墙外新建视频墙");
        MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || _dragOrigin is not { } origin)
                return;
            _dragOrigin = null;
            Capture = false;
            var size = SystemInformation.DragSize;
            var threshold = new Rectangle(origin.X - size.Width / 2, origin.Y - size.Height / 2, size.Width, size.Height);
            var point = PointToScreen(e.Location);
            if (!threshold.Contains(point))
                WallDropRequested?.Invoke(this, point);
        };
        MouseCaptureChanged += (_, _) => { if (!Capture) _dragOrigin = null; };
    }

    internal event Action<BrowserPane, Point>? WallDropRequested;
#if DEBUG
    internal Control DragTitle => _caption;
#endif

    private ToolbarIconButton Add(FlowLayoutPanel parent, ToolbarIcon icon, string label, EventHandler onClick)
    {
        var button = new ToolbarIconButton(icon, label, _controlTips);
        button.Margin = new Padding(1);
        button.Click += onClick;
        parent.Controls.Add(button);
        return button;
    }

    internal async Task InitializeAsync(CoreWebView2Environment environment)
    {
        await View.EnsureCoreWebView2Async(environment);
        if (IsDisposed)
            return;
        Core.Settings.AreDevToolsEnabled = false;
        Core.Settings.AreHostObjectsAllowed = false;
        Core.Settings.IsWebMessageEnabled = false;
        Core.FrameCreated += (_, e) =>
        {
            var frame = e.Frame;
            _frames.Add(frame);
            frame.Destroyed += (_, _) => _frames.Remove(frame);
        };
        Core.DocumentTitleChanged += (_, _) =>
        {
            if (IsPlayer && !string.IsNullOrWhiteSpace(Core.DocumentTitle))
                _caption.Text = Core.DocumentTitle;
        };
        Core.ProcessFailed += (_, e) =>
        {
            _log.Write("webview_process_failed", new { id = Id, kind = e.ProcessFailedKind.ToString() });
            _caption.Text = "播放器进程异常，请关闭后重开";
        };
        if (IsPlayer)
            SetMuted(_initialMute);
    }

    internal void MakePlayer(bool muted)
    {
        if (IsPlayer)
            return;
        IsPlayer = true;
        _initialMute = muted;
        _top.Visible = true;
        if (View.CoreWebView2 != null)
        {
            if (!string.IsNullOrWhiteSpace(Core.DocumentTitle))
                _caption.Text = Core.DocumentTitle;
            SetMuted(muted);
        }
    }

    internal void SetWallFullscreen(bool enabled)
    {
        if (enabled == _immersive)
            return;
        _immersive = enabled;
        _hovered = false;
        _hideToolbarAt = null;
        SuspendLayout();
        try
        {
            _top.Visible = false;
            if (enabled)
            {
                _top.Dock = DockStyle.None;
                _top.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                _top.Bounds = new Rectangle(0, 0, ClientSize.Width, _top.Height);
                _top.BringToFront();
            }
            else
            {
                _top.Dock = DockStyle.Top;
                _top.SendToBack();
                _top.Visible = IsPlayer;
            }
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    internal void UpdateWallHover(bool hovered, long now)
    {
        if (!_immersive)
            return;
        if (hovered)
        {
            _hovered = true;
            _hideToolbarAt = null;
            if (!_top.Visible)
            {
                _top.Visible = true;
                _top.BringToFront();
            }
        }
        else if (_hovered)
        {
            _hovered = false;
            _hideToolbarAt = now + 1000;
        }
        else if (_hideToolbarAt <= now)
        {
            _hideToolbarAt = null;
            _top.Visible = false;
        }
    }

    internal void CancelPageFullscreen()
    {
        _pageFullscreen?.Cancel();
        _pageFullscreen = null;
    }

    internal async Task ApplyPageFullscreenAsync()
    {
        CancelPageFullscreen();
        using var request = new CancellationTokenSource();
        _pageFullscreen = request;
        try
        {
            await Core.ExecuteScriptAsync(DismissPlayerPromotion);
            for (var attempt = 0; attempt < 80 && !request.IsCancellationRequested && !IsDisposed; attempt++)
            {
                if (await Core.ExecuteScriptAsync(EnableWebFullscreen) == "true")
                {
                    if (!request.IsCancellationRequested)
                        _log.Write("page_fullscreen_enabled", new { Id });
                    return;
                }
                await Task.Delay(250, request.Token);
            }
            if (!request.IsCancellationRequested && !IsDisposed)
                _log.Write("page_fullscreen_unavailable", new { Id });
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (!request.IsCancellationRequested && !IsDisposed)
                _log.Write("page_fullscreen_failed", new { Id, errorType = ex.GetType().Name });
        }
        finally
        {
            if (_pageFullscreen == request)
                _pageFullscreen = null;
        }
    }

    private void ToggleMute()
    {
        if (View.CoreWebView2 != null)
            SetMuted(!Core.IsMuted);
    }

    private void SetMuted(bool value)
    {
        Core.IsMuted = value;
        _mute.SetIcon(value ? ToolbarIcon.Muted : ToolbarIcon.Volume, value ? "取消静音" : "静音");
    }

    internal async Task SilenceAsync()
    {
        if (IsDisposed || View.CoreWebView2 == null)
            return;
        try
        {
            SetMuted(true);
            await RunInPageAsync("(() => { for (const media of document.querySelectorAll('video, audio')) media.pause(); return false; })()");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // The page may be closing concurrently.
        }
    }

    internal async Task<bool> CommandAsync(string command)
    {
        if (View.CoreWebView2 == null || IsDisposed)
            return false;
        var action = command switch
        {
            "play" => "v.play().catch(() => {}); return true;",
            "pause" => "v.pause(); return true;",
            "toggle" => "if (v.paused || v.ended) v.play().catch(() => {}); else v.pause(); return true;",
            "back" => "v.currentTime = Math.max(0, v.currentTime - 10); return true;",
            "forward" => "v.currentTime = Number.isFinite(v.duration) ? Math.min(v.duration, v.currentTime + 10) : v.currentTime + 10; return true;",
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        var script = VideoScript(action);
        try
        {
            return await RunInPageAsync(script);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _log.Write("player_control_failed", new { id = Id, command, errorType = ex.GetType().Name });
            return false;
        }
    }

    internal async Task SyncPlaybackAsync()
    {
        if (IsDisposed || View.CoreWebView2 == null)
            return;
        try
        {
            var result = await FindPageResultAsync(VideoScript("return v.paused || v.ended ? 0 : 1;"),
                value => value == "0" || value == "1");
            if (IsDisposed)
                return;
            Playing = result == null ? null : result == "1";
            PlaybackButton.SetIcon(Playing == true ? ToolbarIcon.Pause : ToolbarIcon.Play,
                Playing == true ? "暂停" : "播放");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Playing = null;
        }
    }

    internal void SetGroupPlayback(bool allPlaying, bool allPaused)
    {
        PlayAllButton.SetHighlighted(allPlaying);
        PauseAllButton.SetHighlighted(allPaused);
    }

    private Task RequestGroupAsync(string command) =>
        GroupPlaybackRequested?.Invoke(command) ?? Task.CompletedTask;

    private static string VideoScript(string action) => @"(() => {
        const videos = Array.from(document.querySelectorAll('video')).filter(v => v.currentSrc);
        const v = videos.find(item => !item.paused && item.getClientRects().length) ||
                  videos.find(item => item.getClientRects().length) || videos[0];
        if (!v) return null;
        try { " + action + @" } catch (_) { return null; }
      })()";

    internal async Task<bool> RunInPageAsync(string script) =>
        await FindPageResultAsync(script, value => value == "true") != null;

    internal async Task<string?> FindPageResultAsync(string script, Func<string, bool> accepted)
    {
        var result = await Core.ExecuteScriptAsync(script);
        if (accepted(result))
            return result;
        foreach (var frame in _frames.ToArray())
        {
            try
            {
                if (frame.IsDestroyed() != 0)
                    continue;
                result = await frame.ExecuteScriptAsync(script);
                if (accepted(result))
                    return result;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                // The frame navigated or closed while running the command.
            }
        }
        return null;
    }
}
