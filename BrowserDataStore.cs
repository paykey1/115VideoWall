namespace VideoWall115;

internal sealed record BrowserPage(string Address, string Title, DateTimeOffset Time);
internal sealed record ClosedPage(BrowserPage Page, int Index);

internal sealed class BrowserDataStore : IDisposable
{
    private const int VisitLimit = 750;
    private const int ClosedLimit = 25;
    private readonly LocalJsonFile<BrowserData> _file;
    private readonly BrowserData _data;
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 600 };
    private bool _dirty;

    private sealed class BrowserData
    {
        public List<BrowserPage> Bookmarks { get; set; } = new();
        public List<BrowserPage> History { get; set; } = new();
        public List<ClosedPage> RecentlyClosed { get; set; } = new();
    }

    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "access_token", "refresh_token", "id_token", "auth_token", "token", "oauth_token",
        "session", "sessionid", "session_id", "password", "passwd", "otp", "api_key",
        "client_secret", "signature", "ticket", "digest", "state", "code", "sig",
    };

    internal BrowserDataStore(string data, RunLog log)
    {
        _file = new LocalJsonFile<BrowserData>(data, "browser-data", log);
        _data = _file.Read() ?? new();
        _data.Bookmarks = Readable(_data.Bookmarks);
        _data.History = Readable(_data.History).Take(VisitLimit).ToList();
        _data.RecentlyClosed = (_data.RecentlyClosed ?? new())
            .Where(item => item?.Page != null && CanReopen(item.Page.Address))
            .Take(ClosedLimit).ToList();
        _saveTimer.Tick += (_, _) => Flush();
    }

    private static List<BrowserPage> Readable(List<BrowserPage>? entries) =>
        (entries ?? new()).Where(entry => entry != null && CanRemember(entry.Address)).ToList();

    internal IReadOnlyList<BrowserPage> Bookmarks => _data.Bookmarks;
    internal IReadOnlyList<BrowserPage> History => _data.History;
    internal ClosedPage? LastClosed => _data.RecentlyClosed.FirstOrDefault();

    internal static bool CanRemember(string? address)
    {
        if (address == null || address.Length > 4096 || !SitePolicy.TryWebAddress(address, out var uri))
            return false;
        // Redirect URLs can wrap another complete login URL. Auth and signed URLs are temporary.
        var parts = (uri.Query + "&" + uri.Fragment).Split(['&', '?', '#'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var key = Uri.UnescapeDataString(part.Split('=', 2)[0]).Replace('-', '_');
            if (SecretKeys.Contains(key) || key.Equals("goto", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static BrowserPage MakePage(string address, string? title)
    {
        title = new string((title ?? "").Where(character => !char.IsControl(character)).Take(200).ToArray()).Trim();
        return new BrowserPage(address, title.Length == 0 ? new Uri(address).Host : title, DateTimeOffset.Now);
    }

    internal bool HasBookmark(string address) => _data.Bookmarks.Any(page => page.Address == address);

    internal bool AddBookmark(string address, string? title)
    {
        if (!CanRemember(address))
            return false;
        if (!HasBookmark(address))
        {
            _data.Bookmarks.Add(MakePage(address, title));
            ScheduleSave();
        }
        return true;
    }

    internal void RemoveBookmark(string address)
    {
        if (_data.Bookmarks.RemoveAll(page => page.Address == address) != 0)
            ScheduleSave();
    }

    internal void RecordVisit(string address, string? title)
    {
        if (!CanRemember(address))
            return;
        _data.History.RemoveAll(old => old.Address == address);
        _data.History.Insert(0, MakePage(address, title));
        Trim(_data.History, VisitLimit);
        ScheduleSave();
    }

    internal void UpdateVisitTitle(string address, string? title)
    {
        var index = _data.History.FindIndex(page => page.Address == address);
        if (index < 0 || string.IsNullOrWhiteSpace(title))
            return;
        var existing = _data.History[index];
        var updated = existing with { Title = MakePage(address, title).Title };
        if (updated == existing)
            return;
        _data.History[index] = updated;
        ScheduleSave();
    }

    internal void RemoveVisit(string address)
    {
        if (_data.History.RemoveAll(page => page.Address == address) != 0)
            ScheduleSave();
    }

    internal void ClearHistory()
    {
        _data.History.Clear();
        _data.RecentlyClosed.Clear();
        ScheduleSave();
    }

    internal void RememberClosed(string address, string? title, int index)
    {
        if (!CanReopen(address))
            return;
        var page = address == "about:blank" ? new BrowserPage(address, "New tab", DateTimeOffset.Now)
            : MakePage(address, title);
        _data.RecentlyClosed.Insert(0, new ClosedPage(page, index));
        Trim(_data.RecentlyClosed, ClosedLimit);
        ScheduleSave();
    }

    private static bool CanReopen(string? address) => address == "about:blank" || CanRemember(address);

    internal void ConsumeClosed(ClosedPage page)
    {
        if (_data.RecentlyClosed.Remove(page))
            ScheduleSave();
    }

    private static void Trim<T>(List<T> list, int limit)
    {
        if (list.Count > limit)
            list.RemoveRange(limit, list.Count - limit);
    }

    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    internal void Flush()
    {
        _saveTimer.Stop();
        if (_dirty)
            _dirty = !_file.TryWrite(_data);
    }

    public void Dispose()
    {
        Flush();
        _saveTimer.Dispose();
    }
}
