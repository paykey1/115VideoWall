namespace VideoWall115;

internal sealed record WallGridSettings(bool Automatic, int Columns, int Rows)
{
    internal bool Valid => Columns is >= 1 and <= 6 && Rows is >= 1 and <= 6 && Columns * Rows <= 12;
    internal int Capacity => Automatic ? 6 : Columns * Rows;
}

internal sealed partial class MainForm
{
    private readonly LocalJsonFile<WallGridSettings> _gridFile;
    private WallGridSettings _gridSettings = new(true, 3, 2);
    private int WallCapacity => _gridSettings.Capacity;

    private void OpenSettings()
    {
        using var dialog = new Form
        {
            Text = "设置", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
            MinimizeBox = false, ShowInTaskbar = false, ClientSize = new Size(652, 405),
            Font = Font, Icon = Icon, BackColor = Palette.Panel, ForeColor = Palette.Text,
        };
        var content = new Panel { Name = "SettingsContent", Dock = DockStyle.Fill, BackColor = Palette.Panel };
        var sidebar = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, Width = 158, BackColor = Palette.Canvas,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 16, 12, 0),
        };
        dialog.Controls.Add(content);
        dialog.Controls.Add(sidebar);
        var wallPage = new Panel { Dock = DockStyle.Fill };
        var accountPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        var aboutPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        var shortcutsPage = BuildShortcutsPage(Title("快捷键"));
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24, 10, 24, 10), ColumnCount = 2, RowCount = 6,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        foreach (var height in new[] { 36, 33, 34, 34, 30, 42 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        wallPage.Controls.Add(layout);
        wallPage.Controls.Add(Title("视频墙布局"));

        var login = Palette.Button("115 账号与自动登录", "115 账号与自动登录");
        login.Size = new Size(170, 36);
        login.Enabled = _loginStore != null;
        login.Click += (_, _) => OpenLoginSettings(dialog);
        var accountBody = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(24, 16, 24, 0),
        };
        accountBody.Controls.Add(new Label
        {
            Text = "账号和密码保存在 Windows 凭据管理器中。\n需要验证时，请在 115 页面手动完成。",
            AutoSize = true, Margin = new Padding(0, 0, 0, 22), ForeColor = Palette.Muted,
        });
        accountBody.Controls.Add(login);
        accountPage.Controls.Add(accountBody);
        accountPage.Controls.Add(Title("115 账号"));
        BuildAboutPage(aboutPage, Title("关于"));
        content.Controls.Add(wallPage);
        content.Controls.Add(accountPage);
        content.Controls.Add(aboutPage);
        content.Controls.Add(shortcutsPage);

        var save = Palette.Button("保存", "保存设置");
        var sections = new[] { ("视频墙", wallPage), ("账号", accountPage), ("快捷键", shortcutsPage), ("关于", aboutPage) };
        foreach (var (name, page) in sections)
        {
            var button = Palette.Button(name, name);
            button.Size = new Size(132, 40);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(10, 0, 0, 0);
            button.Tag = page;
            button.Margin = new Padding(0, 0, 0, 5);
            button.Click += (_, _) => SelectPage(page);
            sidebar.Controls.Add(button);
        }
        var automatic = new CheckBox
        {
            Text = "视频墙自动排列", Checked = _gridSettings.Automatic, Dock = DockStyle.Fill,
        };
        layout.Controls.Add(automatic, 0, 1);
        layout.SetColumnSpan(automatic, 2);
        var columns = AddNumber("每排视频数（列）", 2, _gridSettings.Columns);
        var rows = AddNumber("竖向排数（行）", 3, _gridSettings.Rows);
        var hint = new Label { Dock = DockStyle.Fill, ForeColor = Palette.Muted, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(hint, 0, 4);
        layout.SetColumnSpan(hint, 2);

        save.Dock = DockStyle.Fill;
        var cancel = Palette.Button("取消", "关闭设置");
        cancel.Dock = DockStyle.Fill;
        cancel.DialogResult = DialogResult.Cancel;
        layout.Controls.Add(save, 0, 5);
        layout.Controls.Add(cancel, 1, 5);
        dialog.CancelButton = cancel;
        automatic.CheckedChanged += (_, _) => Refresh();
        columns.ValueChanged += (_, _) => Refresh();
        rows.ValueChanged += (_, _) => Refresh();
        save.Click += (_, _) =>
        {
            if (SaveWallGrid(ReadGrid()))
                dialog.DialogResult = DialogResult.OK;
            else
                hint.Text = "设置写入失败，请重试。";
        };
        Refresh();
        SelectPage(wallPage);
        dialog.ShowDialog(this);

        void SelectPage(Panel page)
        {
            foreach (var (_, section) in sections)
                section.Visible = section == page;
            foreach (Button button in sidebar.Controls)
                button.BackColor = button.Tag == page ? Palette.ButtonFill : Palette.Canvas;
            dialog.AcceptButton = page == wallPage ? save : null;
        }

        Label Title(string title) => new()
        {
            Text = title, Dock = DockStyle.Top, Height = 65, Padding = new Padding(24, 12, 0, 0),
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
        };

        NumericUpDown AddNumber(string title, int row, int value)
        {
            var number = new NumericUpDown { Minimum = 1, Maximum = 6, Value = value, Width = 90 };
            number.AccessibleName = title;
            layout.Controls.Add(new Label
            {
                Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            }, 0, row);
            layout.Controls.Add(number, 1, row);
            return number;
        }

        WallGridSettings ReadGrid()
        {
            var grid = new WallGridSettings(automatic.Checked, (int)columns.Value, (int)rows.Value);
            return grid.Automatic && !grid.Valid ? new WallGridSettings(true, 3, 2) : grid;
        }

        void Refresh()
        {
            columns.Enabled = rows.Enabled = !automatic.Checked;
            var grid = ReadGrid();
            save.Enabled = grid.Valid;
            hint.Text = grid.Valid ? $"每面墙最多 {grid.Capacity} 路 · 总计最多 12 路" : "每面墙的行列乘积最多为 12。";
        }
    }

    private void BuildAboutPage(Panel page, Label title)
    {
        var details = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24, 4, 18, 8),
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
        };
        var logo = new PictureBox
        {
            Size = new Size(76, 76), SizeMode = PictureBoxSizeMode.Zoom,
            Image = Branding.LoadLogo(), Margin = new Padding(0, 0, 0, 8),
        };
        logo.Disposed += (_, _) => logo.Image?.Dispose();
        details.Controls.Add(logo);
        AddLine("115 视频墙", Palette.Text);
        AddLine("为 115 网盘提供多路视频浏览和独立播放窗口。", Palette.Muted);
        AddLine($"版本 {Branding.Version}    ·    2026-11-22", Palette.Text);
        AddLine("作者  社长（ME 社 / Massive Electric）", Palette.Text);
        AddLine("联系邮箱  363535896@qq.com", Palette.Text);
        AddLine("微信公众号  me_shebak", Palette.Text);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        AddButton("复制联系邮箱", () => Clipboard.SetText("363535896@qq.com"));
        AddButton("打开诊断日志", () =>
        {
            if (_log.DirectoryPath is { } path)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        });
        details.Controls.Add(actions);
        page.Controls.Add(details);
        page.Controls.Add(title);

        void AddLine(string text, Color color) => details.Controls.Add(new Label
        {
            Text = text, ForeColor = color, AutoSize = true, Margin = new Padding(0, 0, 0, 8),
        });
        void AddButton(string label, Action click)
        {
            var button = Palette.Button(label, label);
            button.AutoSize = true;
            button.Height = 29;
            button.Click += (_, _) => click();
            actions.Controls.Add(button);
        }
    }

    internal bool SaveWallGrid(WallGridSettings settings)
    {
        if (!settings.Valid || !_gridFile.TryWrite(settings))
            return false;
        if (_gridSettings == settings)
            return true;
        _gridSettings = settings;

        var overflow = new List<BrowserPane>();
        foreach (var wall in _walls.ToArray())
        {
            foreach (var pane in wall.Players.Skip(WallCapacity).ToArray())
            {
                overflow.Add(pane);
                wall.Players.Remove(pane);
                wall.Grid.Controls.Remove(pane);
            }
            LayoutWall(wall);
        }
        foreach (var pane in overflow)
        {
            var wall = PickWall() ?? CreateWall();
            wall.Players.Add(pane);
            LayoutWall(wall);
            ShowWall(wall, activate: false);
        }
        _log.Write("wall_grid_changed", new { settings.Automatic, settings.Columns, settings.Rows, walls = _walls.Count });
        return true;
    }
}
