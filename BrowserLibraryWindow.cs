namespace VideoWall115;

internal sealed class BrowserLibraryWindow : BorderlessWindow
{
    private readonly BrowserDataStore _data;
    private readonly bool _bookmarks;
    private readonly ListView _pages = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
        BackColor = Palette.Canvas, ForeColor = Palette.Text, HideSelection = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    private readonly TextBox _search = new()
    {
        Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle,
        BackColor = Palette.ButtonFill, ForeColor = Palette.Text, PlaceholderText = "Search",
    };
    private readonly Button _open;
    private readonly Button _remove;

    internal BrowserPage? SelectedPage { get; private set; }

    internal BrowserLibraryWindow(BrowserDataStore data, bool bookmarks)
    {
        _data = data;
        _bookmarks = bookmarks;
        Text = bookmarks ? "管理收藏" : "History";
        if (bookmarks)
            _search.PlaceholderText = "搜索收藏";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(740, 480);
        MinimumSize = new Size(530, 300);
        BackColor = Palette.Panel;
        ForeColor = Palette.Text;
        Font = new Font("Segoe UI", 10);

        var header = new Panel { Dock = DockStyle.Top, Height = 42 };
        var title = new Label
        {
            Text = Text, Dock = DockStyle.Fill, Padding = new Padding(9, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold),
        };
        header.Controls.Add(title);
        header.Controls.Add(CreateWindowButtons());
        MakeDraggable(title);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(4, 6, 0, 0) };
        _open = Add(actions, bookmarks ? "打开" : "Open", OpenSelected);
        _remove = Add(actions, bookmarks ? "移除" : "Remove", RemoveSelected);
        if (!bookmarks)
            Add(actions, "Clear list", ClearHistory);
        Add(actions, bookmarks ? "关闭" : "Close", Close);
        AcceptButton = _open;

        _pages.Columns.Add(bookmarks ? "网页" : "Page", 360);
        _pages.Columns.Add(bookmarks ? "网站" : "Website", 190);
        _pages.Columns.Add(bookmarks ? "收藏时间" : "Visited", 150);
        _pages.SelectedIndexChanged += (_, _) => UpdateButtons();
        _pages.DoubleClick += (_, _) => OpenSelected();
        _pages.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelected();
                e.Handled = true;
            }
        };
        _search.TextChanged += (_, _) => Populate();
        Controls.Add(_pages);
        Controls.Add(actions);
        Controls.Add(_search);
        Controls.Add(header);
        Populate();
    }

    private static Button Add(FlowLayoutPanel parent, string label, Action action)
    {
        var button = Palette.Button(label, label);
        button.AutoSize = true;
        button.MinimumSize = new Size(82, 30);
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
        return button;
    }

    private void Populate()
    {
        var pages = _bookmarks ? _data.Bookmarks : _data.History;
        var search = _search.Text.Trim();
        _pages.BeginUpdate();
        try
        {
            _pages.Items.Clear();
            foreach (var page in pages)
            {
                var uri = new Uri(page.Address);
                var name = string.IsNullOrWhiteSpace(page.Title) ? uri.Host : page.Title;
                if (search.Length > 0 && !name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                    !page.Address.Contains(search, StringComparison.OrdinalIgnoreCase))
                    continue;
                var row = new ListViewItem(name) { Tag = page };
                row.SubItems.Add(uri.Host);
                row.SubItems.Add(page.Time.ToLocalTime().ToString("MM-dd HH:mm"));
                _pages.Items.Add(row);
            }
        }
        finally
        {
            _pages.EndUpdate();
        }
        UpdateButtons();
    }

    private BrowserPage? Current => _pages.SelectedItems.Count == 1
        ? _pages.SelectedItems[0].Tag as BrowserPage : null;

    private void UpdateButtons() => _open.Enabled = _remove.Enabled = Current != null;

    private void OpenSelected()
    {
        if (Current is not { } page)
            return;
        SelectedPage = page;
        DialogResult = DialogResult.OK;
    }

    private void RemoveSelected()
    {
        if (Current is not { } page)
            return;
        if (_bookmarks)
            _data.RemoveBookmark(page.Address);
        else
            _data.RemoveVisit(page.Address);
        Populate();
    }

    private void ClearHistory()
    {
        if (MessageBox.Show(this, "Clear this browser's history list and recently closed tabs?",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _data.ClearHistory();
        Populate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
