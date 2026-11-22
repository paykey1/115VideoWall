using Microsoft.Web.WebView2.Core;

namespace VideoWall115;

internal sealed partial class MainForm : BorderlessWindow
{
    private const int TotalCapacity = 12;
    private const int BrowserCapacity = 32;
    private readonly RunLog _log;
    private readonly string _data;
    private readonly WindowLayoutStore _windowLayouts;
    private readonly BrowserDataStore _browserData;
    private bool _reopeningTab;
    private bool _libraryOpen;
    private readonly System.Windows.Forms.Timer _layoutTimer = new() { Interval = 400 };
    private bool _wallPlaced;
    private bool _restoreWallFullscreen;
    private readonly SitePolicy _site;
    private readonly List<BrowserPane> _players = new();
    private readonly List<WallWindow> _walls = new();
    private WallWindow? _preferredWall;
    private int _nextWallNumber;
    private readonly Dictionary<BrowserPane, BrowserTabPage> _tabs = new();
    private readonly BrowserTabs _files = new();
    private readonly WallWindow _wallWindow;
    private readonly Label _status = new()
    {
        Text = "Starting browser…", ForeColor = Palette.Muted, BackColor = Palette.Panel,
        Dock = DockStyle.Bottom, Height = 29, Padding = new Padding(12, 0, 0, 0),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private static readonly Color AddressBackground = Color.FromArgb(39, 50, 63);
    private readonly ToolTip _toolbarTips = new() { InitialDelay = 350, ReshowDelay = 150 };
    private readonly Button _showWall;
    private readonly Button _back;
    private readonly Button _home;
    private readonly Button _settingsButton;
    private readonly WindowsLoginStore? _loginStore;
    private CancellationTokenSource? _loginRequest;
    private bool _loginAttempted;
    private bool _editingLogin;
    private readonly ToolbarIconButton _bookmarks;
    private readonly Button _reopenTab;
    private readonly Button _forward;
    private readonly Button _reload;
    private readonly Button _newTab;
    private readonly TextBox _address = new()
    {
        BackColor = AddressBackground, ForeColor = Palette.Text, BorderStyle = BorderStyle.None,
        Name = "BrowserAddress", Dock = DockStyle.Fill, PlaceholderText = "Search or enter an address",
        AccessibleName = "Address bar",
    };
    private CoreWebView2Environment? _environment;
    private BrowserPane? _primary;
    private int _nextId;
    private bool _closing;
    private bool _syncingPlayback;
    private readonly System.Windows.Forms.Timer _playbackTimer = new() { Interval = 650 };

    internal IReadOnlyList<BrowserPane> Players => _players;
    internal BrowserPane? FileBrowser => _primary;
    internal WallWindow VideoWall => _wallWindow;
    internal int TabCount => _tabs.Count;
    internal Panel BrowserToolbar { get; }

    public MainForm(string data, RunLog log, SitePolicy? site = null, WindowsLoginStore? loginStore = null)
    {
        _data = data;
        _log = log;
        _gridFile = new LocalJsonFile<WallGridSettings>(data, "wall-grid", log);
        _shortcutFile = new LocalJsonFile<BossShortcut>(data, "shortcuts", log);
        if (_gridFile.Read() is { Valid: true } savedGrid)
            _gridSettings = savedGrid;
        _windowLayouts = new WindowLayoutStore(data, log);
        _browserData = new BrowserDataStore(data, log);
        _restoreWallFullscreen = _windowLayouts.Get("wall")?.Fullscreen == true;
        _site = site ?? new SitePolicy();
        _loginStore = loginStore;
        Text = "115 Browser";
        ClientSize = new Size(1080, 760);
        MinimumSize = new Size(760, 510);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Palette.Canvas;
        ForeColor = Palette.Text;
        Font = new Font("Microsoft YaHei UI", 9);
        _wallWindow = CreateWall();

        BrowserToolbar = new Panel
        {
            Name = "BrowserToolbar", Dock = DockStyle.Top, Height = 40, BackColor = Palette.Panel,
        };
        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(3, 6, 2, 0),
        };
        var grip = new Panel
        {
            Size = new Size(15, 28), Margin = new Padding(0, 0, 3, 0), Cursor = Cursors.SizeAll,
            AccessibleName = "Move window",
        };
        grip.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(Palette.Muted);
            for (var y = 8; y <= 18; y += 5)
            {
                e.Graphics.FillEllipse(brush, 4, y, 2, 2);
                e.Graphics.FillEllipse(brush, 9, y, 2, 2);
            }
        };
        MakeDraggable(grip);
        _toolbarTips.SetToolTip(grip, "Move window");
        navigation.Controls.Add(grip);
        _back = AddIcon(navigation, ToolbarIcon.Back, "Back", (_, _) => NavigateHistory(forward: false));
        _forward = AddIcon(navigation, ToolbarIcon.Forward, "Forward", (_, _) => NavigateHistory(forward: true));
        _reload = AddIcon(navigation, ToolbarIcon.Refresh, "Reload", (_, _) => SelectedBrowser()?.View.CoreWebView2?.Reload());
        _home = AddIcon(navigation, ToolbarIcon.Files, "115 Files", (_, _) => OpenHome());
        _settingsButton = AddIcon(navigation, ToolbarIcon.Settings, "设置", (_, _) => OpenSettings());
        _showWall = AddIcon(navigation, ToolbarIcon.Wall, "Video wall", (_, _) => ShowWall(activate: true));
        AddIcon(navigation, ToolbarIcon.Logs, "Logs", (_, _) =>
        {
            if (_log.DirectoryPath is { } directory)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory)
                {
                    UseShellExecute = true,
                });
            else
                ShowStatus("Could not open the logs folder.");
        });

        AddIcon(navigation, ToolbarIcon.History, "History (Ctrl+H)",
            async (_, _) => await OpenLibraryAsync(bookmarks: false));
        _bookmarks = AddIcon(navigation, ToolbarIcon.Star, "展开收藏栏", (_, _) => ToggleBookmarksBar());

        var tabActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(3, 6, 1, 0),
        };
        _newTab = AddIcon(tabActions, ToolbarIcon.NewTab, "New tab", async (_, _) => await OpenTabAsync("about:blank", true));
        _reopenTab = AddIcon(tabActions, ToolbarIcon.Reopen, "Reopen closed tab (Ctrl+Shift+T)",
            async (_, _) => await ReopenClosedTabAsync());
        var addressBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5, 6, 5, 6) };
        var addressSurface = new Panel
        {
            Dock = DockStyle.Fill, BackColor = AddressBackground, Padding = new Padding(9, 6, 9, 0),
        };
        addressSurface.Controls.Add(_address);
        addressBox.Controls.Add(addressSurface);
        _address.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            await SubmitAddressAsync();
        };
        BrowserToolbar.Controls.Add(addressBox);
        BrowserToolbar.Controls.Add(navigation);
        BrowserToolbar.Controls.Add(tabActions);
        BrowserToolbar.Controls.Add(CreateWindowButtons(_toolbarTips));

        Controls.Add(_files);
        Controls.Add(_status);
        Controls.Add(_bookmarksBar);
        Controls.Add(BrowserToolbar);
        _playbackTimer.Tick += async (_, _) => await SyncPlaybackStatesAsync();
        _windowLayouts.Get("browser")?.Restore(this);
        TrackPlacement(this);
        TrackPlacement(_wallWindow);
        _layoutTimer.Tick += (_, _) =>
        {
            _layoutTimer.Stop();
            SaveWindowLayout();
        };
        Disposed += (_, _) =>
        {
            _toolbarTips.Dispose();
            _browserData.Dispose();
            _playbackTimer.Dispose();
            _layoutTimer.Dispose();
        };
        _files.CloseRequested += tab => { if (tab.Tag is BrowserPane pane) CloseBrowserTab(pane); };
        _files.SelectionChanged += () =>
        {
            UpdateFileButtons();
            UpdateAddress(force: true);
        };
        UpdateFileButtons();
        Shown += async (_, _) =>
        {
            LoadBossShortcut();
            await StartAsync();
        };
    }

    private ToolbarIconButton AddIcon(FlowLayoutPanel parent, ToolbarIcon icon, string description, EventHandler click)
    {
        var button = new ToolbarIconButton(icon, description, _toolbarTips);
        button.Click += click;
        parent.Controls.Add(button);
        return button;
    }

    private async Task StartAsync()
    {
        _log.Write("app_started", new { version = Application.ProductVersion });
        BrowserPane? browser = null;
        try
        {
            var runtime = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
            var hasBundledRuntime = File.Exists(Path.Combine(runtime, "msedgewebview2.exe"));
            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: hasBundledRuntime ? runtime : null,
                userDataFolder: Path.Combine(_data, "webview-profile"));
            _log.Write("browser_runtime", new { bundled = hasBundledRuntime, version = _environment.BrowserVersionString });
            if (_closing)
                return;
            browser = AddPane(player: false);
            _primary = browser;
            _tabs[browser].Title = "115";
            _files.Strip.Invalidate();
            await InitializeAsync(browser);
            if (!_closing && !browser.IsDisposed)
            {
                browser.Core.Navigate(_site.Home.AbsoluteUri);
                ShowStatus("");
                UpdateFileButtons();
            }
        }
        catch (Exception ex)
        {
            if (_closing)
                return;
            if (browser != null)
                RemovePane(browser);
            _primary = null;
            ShowStatus("Browser failed to start. Check WebView2 Runtime and the profile folder.");
            _log.Write("webview_start_failed", new { type = ex.GetType().Name });
        }
    }

    private BrowserPane AddPane(bool player, int? tabIndex = null, bool select = true)
    {
        var pane = new BrowserPane(++_nextId, _log);
        pane.CloseRequested += RemovePane;
        pane.GroupPlaybackRequested += AllAsync;
        pane.DetachRequested += TogglePopout;
        pane.WallDropRequested += DropVideo;
        pane.ShortcutRequested += keys =>
        {
            if (!pane.IsPlayer)
                return HandleBrowserShortcut(keys);
            var wall = WallFor(pane);
            if (wall == null || !wall.CanHandleShortcut(keys))
                return false;
            wall.BeginInvoke((MethodInvoker)(() => wall.HandleShortcut(keys)));
            return true;
        };
        if (player)
            AddPlayer(pane);
        else
        {
            var tab = new BrowserTabPage("新标签页") { Tag = pane };
            tab.Controls.Add(pane);
            _tabs.Add(pane, tab);
            var index = Math.Clamp(tabIndex ?? _files.Pages.Count, _primary == null ? 0 : 1, _files.Pages.Count);
            _files.Insert(index, tab, select);
        }
        return pane;
    }

    private async Task InitializeAsync(BrowserPane pane)
    {
        await pane.InitializeAsync(_environment!);
        if (_closing || pane.IsDisposed)
            return;
        pane.Core.NewWindowRequested += (_, e) => OpenPopup(pane, e);
        pane.Core.LaunchingExternalUriScheme += (_, e) =>
        {
            e.Cancel = true;
            if (e.IsUserInitiated && DownloadLink.HasScheme(e.Uri))
                QueueCloudDownload(e.Uri);
        };
        pane.Core.SourceChanged += (_, e) =>
        {
            if (!e.IsNewDocument && !pane.IsPlayer && !_site.IsPlayer(pane.Core.Source))
                _browserData.RecordVisit(pane.Core.Source, pane.Core.DocumentTitle);
            if (SelectedBrowser() == pane)
            {
                UpdateAddress();
                UpdateBookmarkIcon();
            }
        };
        pane.Core.HistoryChanged += (_, _) => UpdateFileButtons();
        pane.Core.NavigationStarting += (_, e) =>
        {
            if (DownloadLink.HasScheme(e.Uri))
            {
                e.Cancel = true;
                if (e.IsUserInitiated)
                    QueueCloudDownload(e.Uri);
            }
            else if (!SitePolicy.Allows(e.Uri))
            {
                e.Cancel = true;
                ShowStatus("Unsupported navigation protocol.");
                _log.Write("unsupported_navigation", new { pane.Id });
            }
            else if (pane == _primary && _site.IsPlayer(e.Uri))
            {
                e.Cancel = true;
                var uri = e.Uri;
                BeginInvoke((MethodInvoker)(async () => await OpenDirectAsync(uri)));
            }
            if (!e.Cancel && pane.IsPlayer)
                pane.CancelPageFullscreen();
        };
        pane.Core.NavigationCompleted += (_, e) =>
        {
            if (_closing || pane.IsDisposed)
                return;
            _log.Write("navigation_completed", new { pane.Id, pane.IsPlayer, e.IsSuccess,
                error = e.IsSuccess ? null : e.WebErrorStatus.ToString(),
                http = e.HttpStatusCode });
            if (e.IsSuccess && pane == _primary)
                _ = TryAutoSignInAsync(pane);
            if (e.IsSuccess && !pane.IsPlayer && !_site.IsPlayer(pane.Core.Source))
                _browserData.RecordVisit(pane.Core.Source, pane.Core.DocumentTitle);
            if (e.IsSuccess && !pane.IsPlayer && pane != _primary && _site.IsPlayer(pane.Core.Source))
                BeginInvoke((MethodInvoker)(() => PromoteTab(pane)));
            else if (e.IsSuccess && pane.IsPlayer && _site.IsPlayer(pane.Core.Source))
                _ = pane.ApplyPageFullscreenAsync();
            else if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                ShowStatus("Page failed to load. Reload or go back to try again.");
            UpdateFileButtons();
        };
        pane.Core.DocumentTitleChanged += (_, _) =>
        {
            if (!pane.IsPlayer)
                _browserData.UpdateVisitTitle(pane.Core.Source, pane.Core.DocumentTitle);
            if (pane != _primary && _tabs.TryGetValue(pane, out var tab) &&
                !string.IsNullOrWhiteSpace(pane.Core.DocumentTitle))
            {
                tab.Title = pane.Core.DocumentTitle;
                _files.Strip.Invalidate();
            }
        };
        pane.Core.WindowCloseRequested += (_, _) =>
        {
            if (!_closing && pane != _primary && !pane.IsDisposed)
                BeginInvoke((MethodInvoker)(() => RemovePane(pane)));
        };
        pane.Core.FaviconChanged += async (_, _) => await RefreshFaviconAsync(pane);
        await HookLinkClicksAsync(pane);
    }

    private async void OpenPopup(BrowserPane opener, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (_closing || opener.IsDisposed)
            return;
        if (DownloadLink.HasScheme(e.Uri))
        {
            if (e.IsUserInitiated)
                QueueCloudDownload(e.Uri);
            return;
        }
        var player = _site.IsPlayer(e.Uri);
        if (!SitePolicy.Allows(e.Uri) ||
            (!e.IsUserInitiated && !_site.Is115Page(opener.Core.Source)) ||
            (player ? _players.Count >= TotalCapacity : _tabs.Count >= BrowserCapacity))
        {
            if (!_closing)
                ShowStatus("Pop-up blocked. Click the link directly or open it in a new tab.");
            return;
        }
        var deferral = e.GetDeferral();
        BrowserPane? pending = null;
        try
        {
            pending = AddPane(player, select: !e.IsUserInitiated || (ModifierKeys & Keys.Control) == 0);
            await InitializeAsync(pending);
            if (!_closing && !pending.IsDisposed && !opener.IsDisposed)
            {
                e.NewWindow = pending.Core;
                _log.Write("popup_routed", new { pending.Id, opener = opener.Id, player });
            }
            else if (!_closing && !pending.IsDisposed)
                RemovePane(pending);
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                if (pending is { IsDisposed: false })
                    RemovePane(pending);
                ShowStatus("Could not open the page. Try again.");
                _log.Write("popup_failed", new { type = ex.GetType().Name });
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    internal async Task<BrowserPane?> OpenTabAsync(string address, bool focusAddress = false, int? tabIndex = null, bool select = true)
    {
        if (_closing || _environment == null || !SitePolicy.Allows(address))
            return null;
        if (_tabs.Count >= BrowserCapacity)
        {
            ShowStatus("Tab limit reached. Close a tab to continue.");
            return null;
        }
        var pane = AddPane(player: false, tabIndex, select);
        try
        {
            await InitializeAsync(pane);
            if (_closing || pane.IsDisposed)
                return null;
            pane.Core.Navigate(address);
            UpdateFileButtons();
            if (focusAddress)
            {
                UpdateAddress();
                _address.Focus();
                _address.SelectAll();
            }
            return pane;
        }
        catch (Exception ex)
        {
            if (!_closing && !pane.IsDisposed)
            {
                RemovePane(pane);
                ShowStatus("Could not open a new tab.");
                _log.Write("tab_open_failed", new { type = ex.GetType().Name });
            }
            return null;
        }
    }

    private async Task OpenDirectAsync(string address)
    {
        if (_closing)
            return;
        if (_players.Count >= TotalCapacity)
        {
            ShowStatus("Player limit reached. Close a player to continue.");
            return;
        }
        var pane = AddPane(player: true);
        try
        {
            await InitializeAsync(pane);
            if (!_closing && !pane.IsDisposed)
                pane.Core.Navigate(address);
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                RemovePane(pane);
                ShowStatus("Could not open the 115 player. Try again.");
                _log.Write("player_open_failed", new { type = ex.GetType().Name });
            }
        }
    }

    private void PromoteTab(BrowserPane pane)
    {
        if (_closing || pane.IsDisposed || !_tabs.ContainsKey(pane))
            return;
        if (_players.Count >= TotalCapacity)
        {
            ShowStatus("Player limit reached. The page stayed in its browser tab.");
            return;
        }
        RemoveTab(pane);
        AddPlayer(pane);
        _ = pane.ApplyPageFullscreenAsync();
    }

    private void AddPlayer(BrowserPane pane)
    {
        pane.MakePlayer(muted: _bossHidden || _players.Count > 0);
        _players.Add(pane);
        if (_players.Count == 1)
            _playbackTimer.Start();
        var wall = PickWall() ?? CreateWall();
        wall.Players.Add(pane);
        LayoutWall(wall);
        ShowWall(wall, activate: false);
        UpdateFileButtons();
        ShowStatus("Video added to the wall.");
        _log.Write("player_added", new { pane.Id, players = _players.Count });
    }

    private void TogglePopout(BrowserPane pane)
    {
        if (_closing || pane.IsDisposed)
            return;
        if (pane.Popout is { } window)
        {
            var wall = PickWall() ?? CreateWall();
            RememberPopout(window);
            window.Controls.Remove(pane);
            pane.Popout = null;
            wall.Players.Add(pane);
            LayoutWall(wall);
            ShowWall(wall, activate: false);
            window.Close();
            pane.MoveButton.SetIcon(ToolbarIcon.PopOut, "移到独立窗口");
        }
        else if (WallFor(pane) is { } wall)
        {
            RemoveFromWall(wall, pane);
            OpenWindow(pane);
        }
        _log.Write("player_moved", new { pane.Id, detached = pane.Popout != null });
    }

    private void OpenWindow(BrowserPane pane)
    {
        var occupied = _players.Select(player => player.Popout?.PlacementSlot).ToHashSet();
        var slot = Enumerable.Range(1, TotalCapacity).First(candidate => !occupied.Contains(candidate));
        var window = new PlayerWindow(this, pane.Id, slot);
        _windowLayouts.Get(PopoutKey(slot))?.Restore(window);
        TrackPlacement(window);
        pane.SetWallFullscreen(false);
        pane.Popout = window;
        window.Controls.Add(pane);
        pane.MoveButton.SetIcon(ToolbarIcon.Dock, "返回视频墙");
        window.FormClosing += (_, e) =>
        {
            if (!_closing && pane.Popout == window && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                BeginInvoke((MethodInvoker)(() => RemovePane(pane)));
            }
        };
        if (!_bossHidden)
            window.Show(this);
    }

    private void LayoutWall(WallWindow wall)
    {
        var _wall = wall.Grid;
        var _docked = wall.Players;
        var columns = _gridSettings.Automatic ? (_docked.Count <= 1 ? 1 : _docked.Count <= 4 ? 2 : 3) : _gridSettings.Columns;
        var rows = _gridSettings.Automatic ? Math.Max(1, (_docked.Count + columns - 1) / columns) : _gridSettings.Rows;
        wall.MinimumSize = new Size(Math.Max(640, columns * 150 + 12), Math.Max(360, rows * 100 + 56));
        _wall.SuspendLayout();
        try
        {
            _wall.ColumnCount = columns;
            _wall.RowCount = rows;
            _wall.ColumnStyles.Clear();
            _wall.RowStyles.Clear();
            for (var i = 0; i < columns; i++)
                _wall.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (var i = 0; i < rows; i++)
                _wall.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
            for (var i = 0; i < _docked.Count; i++)
            {
                var pane = _docked[i];
                var position = new TableLayoutPanelCellPosition(i % columns, i / columns);
                if (pane.Parent != _wall)
                    _wall.Controls.Add(pane);
                _wall.SetCellPosition(pane, position);
                pane.SetWallFullscreen(wall.Fullscreen);
                pane.Margin = new Padding(!wall.Fullscreen && _docked.Count > 1 ? 1 : 0);
            }
        }
        finally
        {
            _wall.ResumeLayout();
        }
        wall.SetHasPlayers(_docked.Count != 0);
    }

    private void RemoveTab(BrowserPane pane)
    {
        if (!_tabs.Remove(pane, out var tab))
            return;
        tab.Controls.Remove(pane);
        _files.Remove(tab);
        tab.Dispose();
    }

    private void RemovePane(BrowserPane pane)
    {
        if (pane.IsDisposed)
            return;
        RemoveTab(pane);
        _players.Remove(pane);
        if (_players.Count == 0)
        {
            _playbackTimer.Stop();
            foreach (var wall in _walls)
                wall.SetGroupPlayback(false, false);
        }
        if (WallFor(pane) is { } source)
            RemoveFromWall(source, pane);
        if (pane.Popout is { } window)
        {
            RememberPopout(window);
            pane.Popout = null;
            window.Controls.Remove(pane);
            window.Close();
        }
        else
            pane.Parent?.Controls.Remove(pane);
        pane.Dispose();
        UpdateFileButtons();
        _log.Write("pane_closed", new { pane.Id, players = _players.Count });
    }

    internal BrowserPane? SelectedBrowser() =>
        _tabs.FirstOrDefault(pair => pair.Value == _files.SelectedTab).Key;

    private void OpenHome()
    {
        if (_primary?.View.CoreWebView2 == null)
            return;
        _files.SelectedTab = _tabs[_primary];
        _primary.Core.Navigate(_site.Home.AbsoluteUri);
        UpdateFileButtons();
    }

    private void OpenLoginSettings(IWin32Window owner)
    {
        if (_editingLogin || _loginStore == null)
            return;
        _editingLogin = true;
        _loginRequest?.Cancel();
        try
        {
            using var dialog = new LoginSettingsDialog(_loginStore, _loginStore.Load()?.Account);
            dialog.ShowDialog(owner);
            if (dialog.Changed)
            {
                _loginAttempted = false;
                if (_loginStore.Load() == null)
                    ShowStatus("115 automatic sign-in disabled.");
                else
                {
                    ShowStatus("115 account saved. Automatic sign-in enabled.");
                    OpenHome();
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ShowStatus("Windows Credential Manager is unavailable.");
        }
        finally
        {
            _editingLogin = false;
        }
    }

    private async Task TryAutoSignInAsync(BrowserPane pane)
    {
        if (_closing || _loginStore == null || _loginAttempted || _editingLogin || _loginRequest != null ||
            !_site.IsOfficialSignInPage(pane.Core.Source))
            return;

        LoginDetails? details;
        try
        {
            details = _loginStore.Load();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _log.Write("115_sign_in_credentials_unavailable");
            return;
        }
        if (details == null)
            return;

        using var request = new CancellationTokenSource();
        _loginRequest = request;
        try
        {
            var outcome = await LoginAutomation.TryAsync(pane, _site, details, request.Token);
            if (_closing || request.IsCancellationRequested)
                return;
            _loginAttempted = outcome != LoginOutcome.Unavailable;
            _log.Write("115_auto_sign_in", new { outcome = outcome.ToString() });
            switch (outcome)
            {
                case LoginOutcome.NeedsVerification:
                    if (_tabs.TryGetValue(pane, out var tab))
                        _files.SelectedTab = tab;
                    ShowStatus("Complete the verification on the 115 page.");
                    break;
                case LoginOutcome.Submitted:
                    ShowStatus("115 sign-in submitted. Complete any verification shown on the page.");
                    break;
                case LoginOutcome.ManualInput:
                    ShowStatus("Continue signing in on the 115 page.");
                    break;
            }
        }
        finally
        {
            _loginRequest = null;
        }
    }

    private void NavigateHistory(bool forward)
    {
        if (SelectedBrowser()?.View.CoreWebView2 is not { } core)
            return;
        if (forward && core.CanGoForward)
            core.GoForward();
        else if (!forward && core.CanGoBack)
            core.GoBack();
    }

    private void UpdateAddress(bool force = false)
    {
        if (force || !_address.Focused)
        {
            var address = SelectedBrowser()?.View.CoreWebView2?.Source;
            _address.Text = address == "about:blank" || address == null ? "" : address;
        }
    }

    private async Task SubmitAddressAsync()
    {
        if (_address.ReadOnly || SelectedBrowser() == _primary)
            return;
        var text = _address.Text.Trim();
        if (text.Length == 0)
            return;
        if (DownloadLink.HasScheme(text))
        {
            QueueCloudDownload(text, "address");
            return;
        }
        if (!SitePolicy.TryWebAddress(text, out var url))
        {
            if (text.Contains("://", StringComparison.Ordinal) ||
                text.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                ShowStatus("Enter an HTTP or HTTPS address.");
                return;
            }
            if (text.Any(char.IsWhiteSpace) ||
                !SitePolicy.TryWebAddress("https://" + text, out url) ||
                !url.Host.Contains('.'))
                url = new Uri("https://www.bing.com/search?q=" + Uri.EscapeDataString(text));
        }
        var tab = await OpenTabAsync(url.AbsoluteUri);
        if (tab is { IsDisposed: false } && SelectedBrowser() == tab)
            tab.View.Focus();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keys) =>
        HandleBrowserShortcut(keys) || base.ProcessCmdKey(ref message, keys);

    private bool HandleBrowserShortcut(Keys keys)
    {
        if (_closing)
            return false;
        switch (keys)
        {
            case Keys.Control | Keys.T:
                BeginInvoke((MethodInvoker)(async () => await OpenTabAsync("about:blank", true)));
                return true;
            case Keys.Control | Keys.L:
                if (!_address.ReadOnly)
                    BeginInvoke((MethodInvoker)(() => { _address.Focus(); _address.SelectAll(); }));
                return true;
            case Keys.Control | Keys.W:
                BeginInvoke((MethodInvoker)CloseActiveTab);
                return true;
            case Keys.Control | Keys.Shift | Keys.T:
                BeginInvoke((MethodInvoker)(async () => await ReopenClosedTabAsync()));
                return true;
            case Keys.Control | Keys.D:
                BeginInvoke((MethodInvoker)BookmarkCurrentPage);
                return true;
            case Keys.Control | Keys.H:
                BeginInvoke((MethodInvoker)(async () => await OpenLibraryAsync(bookmarks: false)));
                return true;
            case Keys.Control | Keys.Tab:
            case Keys.Control | Keys.Shift | Keys.Tab:
                BeginInvoke((MethodInvoker)(() =>
                {
                    var count = _files.Pages.Count;
                    if (count > 0)
                    {
                        var step = keys.HasFlag(Keys.Shift) ? -1 : 1;
                        _files.SelectedIndex = (_files.SelectedIndex + step + count) % count;
                    }
                }));
                return true;
            case Keys.Alt | Keys.Left:
            case Keys.Alt | Keys.Right:
                BeginInvoke((MethodInvoker)(() => NavigateHistory(keys == (Keys.Alt | Keys.Right))));
                return true;
            case Keys.F5:
            case Keys.Control | Keys.R:
                BeginInvoke((MethodInvoker)(() => SelectedBrowser()?.View.CoreWebView2?.Reload()));
                return true;
            default:
                return false;
        }
    }

    private void ShowWall(bool activate)
    {
        var preferred = _preferredWall ?? _wallWindow;
        foreach (var wall in _walls.ToArray())
        {
            if (wall != preferred && wall.Players.Count > 0)
                ShowWall(wall, activate: false);
        }
        ShowWall(preferred, activate);
    }

    private void ShowWall(WallWindow wall, bool activate)
    {
        if (_bossHidden)
            return;
        if (wall == _wallWindow && !_wallPlaced)
        {
            var placement = _windowLayouts.Get("wall");
            if (placement != null)
                placement.Restore(_wallWindow);
            else
                _wallWindow.PlaceBeside(this);
            _wallPlaced = true;
        }
        if (!wall.Visible)
            wall.Show();
        if (wall == _wallWindow && _restoreWallFullscreen && wall.Players.Count > 0)
        {
            _restoreWallFullscreen = false;
            _wallWindow.SetFullscreen(true);
        }
        if (activate)
        {
            if (wall.WindowState == FormWindowState.Minimized)
                wall.WindowState = wall.LastVisibleState;
            wall.Activate();
        }
    }

    private void UpdateWallChrome(WallWindow wall)
    {
        foreach (var pane in wall.Players)
        {
            pane.SetWallFullscreen(wall.Fullscreen);
            pane.Margin = new Padding(!wall.Fullscreen && wall.Players.Count > 1 ? 1 : 0);
        }
        QueueWindowLayoutSave();
        _log.Write("wall_fullscreen_changed", new { enabled = wall.Fullscreen });
    }

    private static string PopoutKey(int slot) => $"player-{slot}";

    private void TrackPlacement(Form window)
    {
        window.LocationChanged += (_, _) => QueueWindowLayoutSave();
        window.SizeChanged += (_, _) => QueueWindowLayoutSave();
        window.ResizeEnd += (_, _) => QueueWindowLayoutSave();
    }

    private void QueueWindowLayoutSave()
    {
        if (_closing)
            return;
        _layoutTimer.Stop();
        _layoutTimer.Start();
    }

    private void RememberPopout(PlayerWindow window)
    {
        _windowLayouts.Remember(PopoutKey(window.PlacementSlot), window.CapturePlacement());
        QueueWindowLayoutSave();
    }

    private void SaveWindowLayout()
    {
        _windowLayouts.Remember("browser", CapturePlacement());
        if (_wallPlaced)
        {
            var wall = _wallWindow.CapturePlacement();
            _windowLayouts.Remember("wall", wall with { Fullscreen = wall.Fullscreen || _restoreWallFullscreen });
        }
        foreach (var pane in _players)
        {
            if (pane.Popout is { } window)
                _windowLayouts.Remember(PopoutKey(window.PlacementSlot), window.CapturePlacement());
        }
        _windowLayouts.Save();
    }

    internal void UpdateWallPointer(Point? position, long now) => UpdateWallPointer(_wallWindow, position, now);

    private static void UpdateWallPointer(WallWindow wall, Point? position, long now)
    {
        foreach (var pane in wall.Players)
        {
            var hovered = position is { } point && pane.RectangleToScreen(pane.ClientRectangle).Contains(point);
            pane.UpdateWallHover(hovered, now);
        }
    }

    private void UpdateFileButtons()
    {
        _home.Enabled = _primary?.View.CoreWebView2 != null;
        _settingsButton.Enabled = !_closing;
        var selected = SelectedBrowser();
        var core = selected?.View.CoreWebView2;
        _address.Enabled = core != null;
        _address.ReadOnly = selected == _primary;
        _address.TabStop = !_address.ReadOnly;
        _newTab.Enabled = _environment != null;
        _back.Enabled = core?.CanGoBack == true;
        _forward.Enabled = core?.CanGoForward == true;
        _reload.Enabled = core != null;
        _reopenTab.Enabled = _environment != null && _browserData.LastClosed != null && !_reopeningTab;
        UpdateBookmarkIcon();
        _showWall.Enabled = !_closing;
    }

    private async Task SyncPlaybackStatesAsync()
    {
        if (_closing || _syncingPlayback || _players.Count == 0)
            return;
        _syncingPlayback = true;
        try
        {
            await Task.WhenAll(_players.Select(pane => pane.SyncPlaybackAsync()));
            if (_closing)
                return;
            var allPlaying = _players.Count != 0 && _players.All(pane => pane.Playing == true);
            var allPaused = _players.Count != 0 && _players.All(pane => pane.Playing == false);
            foreach (var pane in _players)
                pane.SetGroupPlayback(allPlaying, allPaused);
            foreach (var wall in _walls)
                wall.SetGroupPlayback(allPlaying, allPaused);
        }
        finally
        {
            _syncingPlayback = false;
        }
    }

    private async Task AllAsync(string command)
    {
        var panes = _players.ToArray();
        var results = await Task.WhenAll(panes.Select(pane => pane.CommandAsync(command)));
        await SyncPlaybackStatesAsync();
        if (!_closing)
            ShowStatus($"已向 {results.Count(sent => sent)} 路视频发送{(command == "play" ? "继续" : "暂停")}操作。");
    }

    private void ShowStatus(string message)
    {
        if (_closing)
            return;
        _status.Text = message;
        _status.Visible = message.Length > 0;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        if (_hotkeyId != 0)
            UnregisterHotKey(Handle, _hotkeyId);
        _cloudBrowser?.Dispose();
        _layoutTimer.Stop();
        SaveWindowLayout();
        _browserData.Flush();
        _loginRequest?.Cancel();
        _playbackTimer.Stop();
        foreach (var pane in _players.Concat(_tabs.Keys).Distinct().ToArray())
            RemovePane(pane);
        foreach (var wall in _walls.ToArray())
            CloseWall(wall);
        _log.Write("app_closed");
        base.OnFormClosing(e);
    }
}
