using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace VideoWall115;

internal sealed partial class MainForm
{
    private readonly Queue<string> _cloudLinks = new();
    private string? _lastCloudLink;
    private long _lastCloudLinkAt;
    private bool _processingCloudLinks;
    private bool _cloudNeedsHome = true;
    private BrowserPane? _cloudBrowser;
    private CloudToastWindow? _cloudToast;

#if DEBUG
    internal BrowserPane? CloudBrowser => _cloudBrowser;
#endif

    private async Task HookLinkClicksAsync(BrowserPane pane)
    {
        // Chromium can discard a raw ed2k URL before native navigation events can read it.
        // The injected listener keeps its per-pane channel private and only accepts real clicks.
        var channel = Guid.NewGuid().ToString("N");
        var source = $$"""
            (() => {
              const post = chrome.webview.postMessage.bind(chrome.webview);
              const path = Function.prototype.call.bind(Event.prototype.composedPath);
              const cancel = Function.prototype.call.bind(Event.prototype.preventDefault);
              const attribute = Function.prototype.call.bind(Element.prototype.getAttribute);
              const anchorType = HTMLAnchorElement;
              const absolute = Function.prototype.call.bind(Object.getOwnPropertyDescriptor(anchorType.prototype, 'href').get);
              window.addEventListener('click', event => {
                if (!event.isTrusted || event.defaultPrevented) return;
                const nodes = path(event);
                for (let i = 0; i < nodes.length; i++) {
                  if (!(nodes[i] instanceof anchorType)) continue;
                  const link = attribute(nodes[i], 'href')?.trim();
                  if (!link) return;
                  const prefix = {{JsonSerializer.Serialize(channel)}};
                  if (/^ed2k:\/\/\|file\|/i.test(link)) {
                    cancel(event);
                    post(prefix + 'D' + link);
                    return;
                  }
                  if (!event.ctrlKey || event.button !== 0 || event.shiftKey || event.altKey ||
                      attribute(nodes[i], 'download') !== null) return;
                  const url = absolute(nodes[i]);
                  if (!/^https?:/i.test(url)) return;
                  cancel(event);
                  post(prefix + 'B' + url);
                  return;
                }
              }, true);
            })();
            """;
        var core = pane.Core;
        void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            if (message.RootElement.ValueKind == JsonValueKind.String &&
                message.RootElement.GetString() is { } payload &&
                payload.Length > channel.Length && payload.StartsWith(channel, StringComparison.Ordinal))
            {
                var address = payload[(channel.Length + 1)..];
                if (payload[channel.Length] == 'D' && DownloadLink.HasScheme(address))
                    QueueCloudDownload(address);
                else if (payload[channel.Length] == 'B' && SitePolicy.TryWebAddress(address, out var url))
                    BeginInvoke((MethodInvoker)(async () => await OpenTabAsync(url.AbsoluteUri, select: false)));
            }
        }
        try
        {
            core.Settings.IsWebMessageEnabled = true;
            core.WebMessageReceived += OnMessage;
            core.FrameCreated += (_, e) => e.Frame.WebMessageReceived += OnMessage;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(source);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (!_closing)
                _log.Write("link_click_hook_failed", new { type = ex.GetType().Name });
        }
    }

    private void QueueCloudDownload(string address, string source = "page")
    {
        if (_closing || !DownloadLink.IsValid(address))
            return;
        var now = Environment.TickCount64;
        if (address == _lastCloudLink && now - _lastCloudLinkAt < 1000)
            return;
        _lastCloudLink = address;
        _lastCloudLinkAt = now;
        var protocol = address.StartsWith("ed2k:", StringComparison.OrdinalIgnoreCase) ? "ed2k" : "magnet";
        _log.Write("cloud_download_queued", new { source, protocol });
        _cloudLinks.Enqueue(address);
        if (_processingCloudLinks)
            return;
        _processingCloudLinks = true;
        BeginInvoke((MethodInvoker)(async () => await ProcessCloudDownloadsAsync()));
    }

    private async Task ProcessCloudDownloadsAsync()
    {
        try
        {
            while (!_closing && _cloudLinks.TryDequeue(out var address))
            {
                try
                {
                    var pane = await PrepareCloudBrowserAsync();
                    if (pane == null || _closing)
                        continue;
                    var result = await CloudDownload.AddAsync(pane, _site, address, CanShowCloudToast);
                    _cloudNeedsHome = result.Outcome is "Unavailable" or "115NotReady" or "NoResponse";
                    _log.Write("cloud_download_result", new { pane.Id, outcome = result.Outcome,
                        result.HttpStatus, captured = result.Image != null });
                    if (result.Image is { } image)
                        ShowCloudToast(image);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or TimeoutException)
                {
                    _cloudNeedsHome = true;
                    if (!_closing)
                        _log.Write("cloud_download_failed", new { type = ex.GetType().Name });
                }
            }
        }
        finally
        {
            _processingCloudLinks = false;
        }
    }

    private async Task<BrowserPane?> PrepareCloudBrowserAsync()
    {
        if (_environment == null || _closing)
            return null;
        if (_cloudBrowser?.View.CoreWebView2 == null)
        {
            _cloudBrowser?.Dispose();
            var browser = new BrowserPane(++_nextId, _log) { Dock = DockStyle.None, TabStop = false };
            browser.View.TabStop = false;
            _cloudBrowser = browser;
            ResizeCloudBrowser(browser);
            Controls.Add(browser);
            await browser.InitializeAsync(_environment);
            if (_closing || browser.IsDisposed)
                return null;
            browser.Core.NewWindowRequested += (_, e) => e.Handled = true;
            browser.Core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
            browser.Core.NavigationStarting += (_, e) =>
                e.Cancel = !_site.IsCloudDownloadPage(e.Uri);
            _cloudNeedsHome = true;
        }

        var pane = _cloudBrowser;
        ResizeCloudBrowser(pane);
        if (!_cloudNeedsHome && _site.IsCloudDownloadPage(pane.Core.Source))
            return pane;

        var core = pane.Core;
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) =>
            loaded.TrySetResult(e.IsSuccess);
        core.NavigationCompleted += OnNavigationCompleted;
        try
        {
            core.Navigate(_site.Home.AbsoluteUri);
            var success = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
            _cloudNeedsHome = !success;
            return success && !_closing ? pane : null;
        }
        finally
        {
            if (!pane.IsDisposed)
                core.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    private void ResizeCloudBrowser(BrowserPane pane)
    {
        var width = Math.Max(1024, _files.PageBounds.Width);
        var height = Math.Max(640, _files.PageBounds.Height);
        // Keep WebView2 rendering the website notification outside the browser client area.
        pane.Bounds = new Rectangle(-width - 1, 0, width, height);
    }

    private bool CanShowCloudToast() =>
        !_closing && Visible && WindowState != FormWindowState.Minimized;

    private void ShowCloudToast(Bitmap image)
    {
        if (!CanShowCloudToast() || _files.PageBounds is not { Width: > 0, Height: > 0 })
        {
            image.Dispose();
            return;
        }
        _cloudToast?.Close();
        var toast = new CloudToastWindow(image, _files.RectangleToScreen(_files.PageBounds));
        _cloudToast = toast;
        toast.FormClosed += (_, _) => { if (_cloudToast == toast) _cloudToast = null; };
        toast.Show(this);
    }
}
