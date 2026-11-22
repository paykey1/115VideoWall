namespace VideoWall115;

internal sealed partial class MainForm
{
    private async Task RefreshFaviconAsync(BrowserPane pane)
    {
        if (!_tabs.TryGetValue(pane, out var tab))
            return;
        var core = pane.Core;
        var uri = core.FaviconUri;
        try
        {
            using var stream = string.IsNullOrEmpty(uri) ? null : await core.GetFaviconAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2FaviconImageFormat.Png);
            if (tab.IsDisposed || uri != core.FaviconUri)
                return;
            Bitmap? icon = null;
            if (stream is { Length: > 0 })
            {
                using var image = Image.FromStream(stream);
                icon = new Bitmap(image, 32, 32);
            }
            tab.Icon?.Dispose();
            tab.Icon = icon;
            _files.Strip.Invalidate();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // A navigation may replace the icon while WebView2 is reading it.
        }
    }

    private readonly ToolStrip _bookmarksBar = new()
    {
        Name = "BookmarksBar", Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden,
        Visible = false, AutoSize = false, Height = 30, Padding = new Padding(6, 0, 4, 0),
        LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
        BackColor = Palette.Panel, ForeColor = Palette.Text, ShowItemToolTips = true,
        AccessibleName = "收藏栏", Renderer = new ToolStripProfessionalRenderer(new BookmarkColors()),
    };
    private ToolStripButton? _bookmarkPage;

    private void UpdateBookmarkIcon()
    {
        var address = SelectedBrowser()?.View.CoreWebView2?.Source;
        var saved = address != null && _browserData.HasBookmark(address);
        _bookmarks.SetIcon(saved ? ToolbarIcon.Starred : ToolbarIcon.Star,
            _bookmarksBar.Visible ? "隐藏收藏栏" : "展开收藏栏");
        if (_bookmarkPage != null)
        {
            _bookmarkPage.Enabled = BrowserDataStore.CanRemember(address);
            _bookmarkPage.Text = saved ? "取消收藏" : "收藏此页";
        }
    }

    internal void BookmarkCurrentPage()
    {
        var core = SelectedBrowser()?.View.CoreWebView2;
        if (core == null || !_browserData.AddBookmark(core.Source, core.DocumentTitle))
        {
            ShowStatus("此页面无法收藏。");
            return;
        }
        RefreshBookmarksBar();
        ShowStatus("已收藏。");
    }

    private void ToggleBookmarksBar()
    {
        _bookmarksBar.Visible = !_bookmarksBar.Visible;
        RefreshBookmarksBar();
    }

    private void RefreshBookmarksBar()
    {
        if (!_bookmarksBar.Visible)
        {
            UpdateBookmarkIcon();
            return;
        }

        _bookmarksBar.SuspendLayout();
        try
        {
            foreach (var item in _bookmarksBar.Items.Cast<ToolStripItem>().ToArray())
                item.Dispose();

            _bookmarkPage = AddBookmarkButton("收藏此页", (_, _) =>
            {
                var address = SelectedBrowser()?.View.CoreWebView2?.Source;
                if (address != null && _browserData.HasBookmark(address))
                {
                    _browserData.RemoveBookmark(address);
                    RefreshBookmarksBar();
                }
                else
                    BookmarkCurrentPage();
            });
            _bookmarkPage.Overflow = ToolStripItemOverflow.Never;
            var manage = AddBookmarkButton("管理收藏", async (_, _) => await OpenLibraryAsync(bookmarks: true));
            manage.Alignment = ToolStripItemAlignment.Right;
            manage.Overflow = ToolStripItemOverflow.Never;
            _bookmarksBar.Items.Add(new ToolStripSeparator());

            if (_browserData.Bookmarks.Count == 0)
                _bookmarksBar.Items.Add(new ToolStripLabel("暂无收藏") { ForeColor = Palette.Muted });
            foreach (var bookmark in _browserData.Bookmarks)
            {
                var title = bookmark.Title.Length == 0 ? new Uri(bookmark.Address).Host : bookmark.Title;
                var label = title.Length > 24 ? title[..23] + "…" : title;
                var item = AddBookmarkButton(label, async (_, _) => await OpenTabAsync(bookmark.Address));
                item.ToolTipText = bookmark.Address;
                item.AccessibleName = title;
            }
        }
        finally
        {
            _bookmarksBar.ResumeLayout();
        }
        UpdateBookmarkIcon();
    }

    private ToolStripButton AddBookmarkButton(string label, EventHandler handler)
    {
        var button = new ToolStripButton(label)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ForeColor = Palette.Text,
            Padding = new Padding(5, 0, 5, 0),
        };
        button.Click += handler;
        _bookmarksBar.Items.Add(button);
        return button;
    }

    private async Task OpenLibraryAsync(bool bookmarks)
    {
        if (_libraryOpen || _closing)
            return;
        _libraryOpen = true;
        try
        {
            using var library = new BrowserLibraryWindow(_browserData, bookmarks);
            var result = library.ShowDialog(this);
            if (!_closing)
            {
                if (bookmarks)
                    RefreshBookmarksBar();
                UpdateFileButtons();
                if (result == DialogResult.OK && library.SelectedPage is { } page)
                    await OpenTabAsync(page.Address);
            }
        }
        finally
        {
            _libraryOpen = false;
        }
    }

    private void CloseActiveTab()
    {
        if (SelectedBrowser() is { } pane && pane != _primary)
            CloseBrowserTab(pane);
    }

    internal void CloseBrowserTab(BrowserPane pane)
    {
        if (_closing || pane == _primary || !_tabs.TryGetValue(pane, out var tab))
            return;
        if (pane.View.CoreWebView2 is { } core && !_site.IsPlayer(core.Source))
            _browserData.RememberClosed(core.Source, core.DocumentTitle, _files.IndexOf(tab));
        RemovePane(pane);
    }

    internal async Task ReopenClosedTabAsync()
    {
        if (_reopeningTab || _browserData.LastClosed is not { } latest)
            return;
        _reopeningTab = true;
        UpdateFileButtons();
        try
        {
            if (await OpenTabAsync(latest.Page.Address, tabIndex: latest.Index) != null)
                _browserData.ConsumeClosed(latest);
        }
        finally
        {
            _reopeningTab = false;
            if (!_closing)
                UpdateFileButtons();
        }
    }

    private sealed class BookmarkColors : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Palette.Panel;
        public override Color ToolStripGradientMiddle => Palette.Panel;
        public override Color ToolStripGradientEnd => Palette.Panel;
        public override Color ToolStripBorder => Palette.ButtonFill;
        public override Color ToolStripDropDownBackground => Palette.Panel;
        public override Color MenuBorder => Palette.ButtonFill;
        public override Color MenuItemBorder => Palette.ButtonFill;
        public override Color MenuItemSelected => Palette.ButtonFill;
        public override Color SeparatorDark => Palette.ButtonFill;
        public override Color SeparatorLight => Palette.ButtonFill;
    }
}
