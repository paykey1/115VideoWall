namespace VideoWall115;

internal sealed class SitePolicy
{
    private readonly Uri? _fixtureOrigin;

    public SitePolicy(Uri? fixtureOrigin = null) => _fixtureOrigin = fixtureOrigin;

    public Uri Home => _fixtureOrigin ?? new Uri("https://115.com/?cid=0&offset=0&mode=wangpan");

    public string[] CloudOrigins => _fixtureOrigin == null
        ? new[] { "https://115.com", "https://yun.115.com" }
        : new[] { _fixtureOrigin.GetLeftPart(UriPartial.Authority) };

    public string[] SignInOrigins => _fixtureOrigin == null
        ? new[] { "https://115.com", "https://w.115.com", "https://www.115.com" }
        : new[] { _fixtureOrigin.GetLeftPart(UriPartial.Authority) };

    public bool IsOfficialSignInPage(string address) =>
        TryWebAddress(address, out var uri) && SignInOrigins.Contains(uri.GetLeftPart(UriPartial.Authority));

    public static bool Allows(string address) =>
        address == "about:blank" || TryWebAddress(address, out _);

    public static bool TryWebAddress(string address, out Uri uri)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var result) && result.UserInfo.Length == 0 &&
            result.Host.Length > 0 && result.Scheme is "https" or "http")
        {
            uri = result;
            return true;
        }
        uri = null!;
        return false;
    }

    public bool Is115Page(string address) =>
        TryWebAddress(address, out var uri) && (IsFixture(uri) ||
            uri.Scheme == "https" && uri.IsDefaultPort &&
            (IsHost(uri, "115.com") || IsHost(uri, "115vod.com")));

    public bool IsPlayer(string address) =>
        TryWebAddress(address, out var uri) &&
        (IsFixture(uri) ? uri.AbsolutePath.StartsWith("/watch/", StringComparison.Ordinal) :
            uri.Scheme == "https" && uri.IsDefaultPort && IsHost(uri, "115vod.com"));

    public bool IsCloudDownloadPage(string address) =>
        TryWebAddress(address, out var uri) &&
        (IsFixture(uri) || uri.Scheme == "https" && uri.IsDefaultPort && IsHost(uri, "115.com"));

    public bool IsCloudTaskSubmission(string address) =>
        IsCloudDownloadPage(address) && Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Query.TrimStart('?').Split('&').Any(field => field is "ac=add_task_url" or "ac=add_task_urls");

    public bool IsCloudTasksPage(string address) =>
        IsCloudDownloadPage(address) && Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Query.TrimStart('?').Split('&').Contains("tab=offline");

    private bool IsFixture(Uri uri) => _fixtureOrigin != null &&
        uri.GetLeftPart(UriPartial.Authority) == _fixtureOrigin.GetLeftPart(UriPartial.Authority);

    private static bool IsHost(Uri uri, string domain) =>
        uri.IdnHost.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        uri.IdnHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
