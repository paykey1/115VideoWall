namespace VideoWall115;

internal sealed class CloudToastWindow : Form
{
    private readonly Bitmap _image;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    internal CloudToastWindow(Bitmap image, Rectangle browserBounds)
    {
        _image = image;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(Math.Min(image.Width, browserBounds.Width), Math.Min(image.Height, browserBounds.Height));
        Location = new Point(browserBounds.Left + (browserBounds.Width - Width) / 2,
                             browserBounds.Top + Math.Max(0, (browserBounds.Height - Height) / 5));
        BackgroundImage = image;
        BackgroundImageLayout = ImageLayout.Stretch;
        _timer.Tick += (_, _) => Close();
        Shown += (_, _) => _timer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int noActivate = 0x08000000;
            const int toolWindow = 0x00000080;
            var parameters = base.CreateParams;
            parameters.ExStyle |= noActivate | toolWindow;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0084) // WM_NCHITTEST
        {
            message.Result = new IntPtr(-1); // HTTRANSPARENT
            return;
        }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            BackgroundImage = null;
            _image.Dispose();
        }
        base.Dispose(disposing);
    }
}
