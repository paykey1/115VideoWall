using System.Text.RegularExpressions;

namespace VideoWall115;

internal static partial class DownloadLink
{
    internal static bool HasScheme(string address) =>
        address.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
        address.StartsWith("ed2k:", StringComparison.OrdinalIgnoreCase);

    internal static bool IsValid(string address)
    {
        if (address.Length > 8192 || address.Any(char.IsControl))
            return false;
        if (address.StartsWith("ed2k:", StringComparison.OrdinalIgnoreCase))
            return Ed2kFile().IsMatch(address);
        if (!address.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var field in address[8..].Split('&'))
        {
            if (!field.StartsWith("xt=", StringComparison.OrdinalIgnoreCase))
                continue;
            var topic = Uri.UnescapeDataString(field[3..]);
            if (TorrentTopic().IsMatch(topic))
                return true;
        }
        return false;
    }

    [GeneratedRegex(@"\Aurn:(?:btih:(?:[0-9a-f]{40}|[a-z2-7]{32})|btmh:1220[0-9a-f]{64})\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TorrentTopic();

    [GeneratedRegex(@"\Aed2k://\|file\|[^|]+\|[0-9]+\|[0-9a-f]{32}\|(?:.*\|)?/\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Ed2kFile();
}
