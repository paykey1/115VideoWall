namespace VideoWall115;

internal static class Branding
{
    internal static string Version => typeof(Branding).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    internal static Bitmap? LoadLogo()
    {
        using var stream = typeof(Branding).Assembly.GetManifestResourceStream("VideoWall115.Logo");
        if (stream == null)
            return null;
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
