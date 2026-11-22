namespace VideoWall115;

internal sealed record WindowPlacement(
    int X, int Y, int Width, int Height, bool Maximized = false, bool Fullscreen = false)
{
    internal Rectangle Rectangle => new(X, Y, Width, Height);
    internal bool IsValid => Width is >= 100 and <= 32000 && Height is >= 100 and <= 32000 &&
                             X is >= -100000 and <= 100000 && Y is >= -100000 and <= 100000;

    internal static WindowPlacement FromBounds(
        Rectangle bounds, bool maximized = false, bool fullscreen = false) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized, fullscreen);

    internal void Restore(BorderlessWindow window)
    {
        var bounds = Rectangle;
        var workAreas = Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
        var fallback = Screen.FromRectangle(bounds).WorkingArea;
        bounds = FitOnScreens(bounds, workAreas, fallback);
        window.StartPosition = FormStartPosition.Manual;
        window.Bounds = bounds;
        if (Maximized)
            window.MaximizeOnCurrentScreen();
    }

    internal static Rectangle FitOnScreens(Rectangle original, Rectangle[] workAreas, Rectangle fallback)
    {
        // Keep the original coordinates if a nontrivial piece of the drag area is still reachable.
        var titleArea = new Rectangle(original.X, original.Y, Math.Min(original.Width, 200), 30);
        if (workAreas.Any(area =>
        {
            var visible = System.Drawing.Rectangle.Intersect(titleArea, area);
            return visible.Width >= 80 && visible.Height >= 20;
        }))
            return original;

        var width = Math.Min(original.Width, fallback.Width);
        var height = Math.Min(original.Height, fallback.Height);
        return new Rectangle(
            Math.Clamp(original.X, fallback.Left, fallback.Right - width),
            Math.Clamp(original.Y, fallback.Top, fallback.Bottom - height),
            width, height);
    }
}

internal sealed class WindowLayoutStore
{
    private readonly Dictionary<string, WindowPlacement> _entries;
    private readonly LocalJsonFile<Dictionary<string, WindowPlacement>> _file;
    private bool _dirty;

    internal WindowLayoutStore(string data, RunLog log)
    {
        _file = new LocalJsonFile<Dictionary<string, WindowPlacement>>(data, "window-layout", log);
        _entries = _file.Read() ?? new();
    }

    internal WindowPlacement? Get(string key) => _entries.TryGetValue(key, out var placement) &&
        placement is { IsValid: true } ? placement : null;

    internal void Remember(string key, WindowPlacement placement)
    {
        if (!placement.IsValid || Get(key) == placement)
            return;
        _entries[key] = placement;
        _dirty = true;
    }

    internal void Save()
    {
        if (!_dirty)
            return;
        _dirty = !_file.TryWrite(_entries);
    }
}
