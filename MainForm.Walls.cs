namespace VideoWall115;

internal sealed partial class MainForm
{
    private WallWindow CreateWall(Point? drop = null)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Black };
        var wall = new WallWindow(grid, AllAsync, ++_nextWallNumber);
        wall.FullscreenChanged += () => UpdateWallChrome(wall);
        wall.PointerUpdated += (point, now) => UpdateWallPointer(wall, point, now);
        wall.TitleDropped += point =>
        {
            if (wall.Players.Count == 1 && FindDropWall(wall, point) is { } target)
            {
                var pane = wall.Players[0];
                BeginInvoke((MethodInvoker)(() => MoveVideoToWall(pane, target, point)));
            }
        };
        wall.Activated += (_, _) => PreferWall(wall);
        wall.ClosePlayersRequested += () => BeginInvoke((MethodInvoker)(() =>
        {
            foreach (var pane in wall.Players.ToArray())
                RemovePane(pane);
            if (wall == _wallWindow && !wall.IsDisposed)
                wall.Hide();
        }));
        wall.SetHasPlayers(false);
        _walls.Add(wall);
        if (_nextWallNumber > 1)
        {
            var cascade = 28 * Math.Max(1, _walls.Count - 1);
            var point = drop ?? new Point(_wallWindow.Left + cascade, _wallWindow.Top + cascade);
            var area = Screen.FromPoint(point).WorkingArea;
            var width = Math.Min(960, area.Width);
            var height = Math.Min(570, area.Height);
            var left = drop.HasValue ? point.X - width / 2 : point.X;
            var top = drop.HasValue ? point.Y - 24 : point.Y;
            wall.Bounds = new Rectangle(Math.Clamp(left, area.Left, area.Right - width),
                Math.Clamp(top, area.Top, area.Bottom - height), width, height);
        }
        return wall;
    }

    private void PreferWall(WallWindow wall)
    {
        if (!_walls.Remove(wall))
            return;
        _walls.Add(wall);
        _preferredWall = wall;
    }

    private WallWindow? PickWall() =>
        _walls.LastOrDefault(wall => wall.Visible && wall.WindowState != FormWindowState.Minimized &&
            wall.Players.Count < WallCapacity) ??
        _walls.LastOrDefault(wall => wall.Players.Count < WallCapacity);

    private WallWindow? WallFor(BrowserPane pane) => _walls.FirstOrDefault(wall => wall.Players.Contains(pane));

    private WallWindow? FindDropWall(WallWindow source, Point point) =>
        _walls.LastOrDefault(wall => wall != source && wall.IsAt(point)) ??
        _walls.LastOrDefault(wall => wall != source && wall.ContainsDropPoint(point));

    private void DropVideo(BrowserPane pane, Point point)
    {
        if (WallFor(pane) is not { } source)
            return;
        var target = FindDropWall(source, point);
        if (target != null || !source.ContainsDropPoint(point))
            MoveVideoToWall(pane, target, point);
    }

    internal void MoveVideoToWall(BrowserPane pane, WallWindow? target, Point point)
    {
        if (_closing || WallFor(pane) is not { } source || target == source || target is { IsDisposed: true })
            return;
        if (target?.Players.Count >= WallCapacity)
        {
            ShowStatus("目标视频墙已满。");
            return;
        }
        target ??= CreateWall(point);
        RemoveFromWall(source, pane);
        target.Players.Add(pane);
        LayoutWall(target);
        PreferWall(target);
        ShowWall(target, activate: true);
        _log.Write("video_moved_between_walls", new { pane.Id, walls = _walls.Count });
    }

    private void RemoveFromWall(WallWindow wall, BrowserPane pane)
    {
        wall.Players.Remove(pane);
        wall.Grid.Controls.Remove(pane);
        if (wall != _wallWindow && wall.Players.Count == 0)
            CloseWall(wall);
        else
            LayoutWall(wall);
    }

    private void CloseWall(WallWindow wall)
    {
        _walls.Remove(wall);
        if (_preferredWall == wall)
            _preferredWall = _walls.LastOrDefault();
        wall.ExitRequested = true;
        wall.Close();
        wall.Dispose();
    }
}
