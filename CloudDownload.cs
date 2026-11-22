using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace VideoWall115;

internal static class CloudDownload
{
    internal sealed record Notification(string Outcome, Bitmap? Image, int? HttpStatus);

    internal static async Task<Notification> AddAsync(
        BrowserPane pane, SitePolicy site, string magnet, Func<bool> needsImage)
    {
        var submit = OfficialScript(site, $$"""
            const field = document.querySelector('textarea#js_offline_new_add');
            let job = document.videoWallCloudJob;
            if (!job && visible(field)) return false;
            if (!job) {
              const style = document.createElement('style');
              style.textContent = '.offline-box, .offline-box + .dialog-back-mask { opacity: 0 !important; pointer-events: none !important; }';
              document.head.append(style);
              job = document.videoWallCloudJob = { style };
            }

            if (visible(field)) {
              const dialog = field.closest('.offline-box');
              const button = dialog?.querySelector('[data-btn="start"]');
              if (!visible(button) || button.textContent.trim() !== '开始下载' ||
                  button.disabled || button.getAttribute('aria-disabled') === 'true' ||
                  field.value.trim()) return false;
              job.dialog = dialog;
              job.openedAt ??= performance.now();
              // 115 opens the dialog before its offline plugin receives the authorization data.
              const plugin = window.Core?.OFFL5Plug;
              if (typeof plugin?.GetGlobals === 'function' && !plugin.GetGlobals()?.KEY_OBJ) {
                job.waitingFor115 = true;
                return false;
              }
              job.waitingFor115 = false;
              if (performance.now() - job.openedAt < 350) return false;

              const documents = [document];
              try { if (window !== window.top) documents.push(window.top.document); } catch (_) {}
              job.observer = new MutationObserver(records => {
                for (const doc of documents) {
                  const toasts = doc.querySelectorAll('.ex-popoup-hint, .popup-hint');
                  for (const toast of toasts) {
                    if (!visible(toast) || toast.matches('.exph-loader')) continue;
                    const message = toast.querySelector('[rel="con"]')?.textContent.trim();
                    if (!message || message.includes('正在添加任务')) continue;
                    if (records.some(record => toast.contains(record.target) ||
                        [...record.addedNodes].some(node => node.contains(toast)))) job.toast = toast;
                  }
                }
              });
              for (const doc of documents)
                job.observer.observe(doc.body, {
                  subtree: true, childList: true, characterData: true,
                  attributes: true, attributeFilter: ['class', 'style', 'hidden']
                });
              field.value = {{JsonSerializer.Serialize(magnet)}};
              field.dispatchEvent(new Event('input', { bubbles: true }));
              field.dispatchEvent(new Event('change', { bubbles: true }));
              button.click();
              return true;
            }

            const items = [...document.querySelectorAll('a, button, [role="menuitem"]')];
            const menu = items.find(element => element.textContent.trim() === '添加云下载') ||
                         document.querySelector('[menu="add_offline_link"]');
            if (menu && job.menu !== menu &&
                menu.getAttribute('aria-disabled') !== 'true' && !menu.disabled) {
              job.menu = menu;
              menu.click();
              return false;
            }
            const upload = items.find(element => visible(element) && element.textContent.trim() === '上传');
            if (upload && job.upload !== upload) {
              job.upload = upload;
              upload.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
              upload.dispatchEvent(new MouseEvent('mouseenter'));
            }
            return false;
            """);
        var result = OfficialScript(site, """
            const toast = document.videoWallCloudJob?.toast;
            if (!visible(toast)) return null;
            const message = toast.querySelector('[rel="con"]')?.textContent.trim() || '';
            const outcome = message === '成功添加云下载' ? 'Created' :
                message.includes('任务已存在') ? 'Duplicate' : 'Rejected';
            try {
              let rect = toast.getBoundingClientRect();
              let x = rect.left, y = rect.top, view = toast.ownerDocument.defaultView;
              while (view !== view.top) {
                const frame = view.frameElement;
                if (!frame) return { outcome };
                const bounds = frame.getBoundingClientRect();
                x += bounds.left + frame.clientLeft;
                y += bounds.top + frame.clientTop;
                view = view.parent;
              }
              const width = Math.ceil(rect.width), height = Math.ceil(rect.height);
              if (width < 10 || height < 10 || width > 550 || height > 400 ||
                  x < 0 || y < 0 || x + width > view.innerWidth || y + height > view.innerHeight)
                return { outcome };
              return { outcome, clip: { x: Math.floor(x + view.scrollX), y: Math.floor(y + view.scrollY),
                                        width, height, scale: 1 } };
            } catch (_) { return { outcome }; }
            """);
        var cleanup = OfficialScript(site, """
            const job = document.videoWallCloudJob;
            if (!job) return false;
            job.observer?.disconnect();
            const dialog = job.dialog || document.querySelector('textarea#js_offline_new_add')?.closest('.offline-box');
            if (visible(dialog)) dialog.querySelector('.dialog-handle [btn="close"]')?.click();
            job.style.remove();
            delete document.videoWallCloudJob;
            return false;
            """);
        var awaiting115 = OfficialScript(site, "return document.videoWallCloudJob?.waitingFor115 === true;");

        if (!CanContinue(pane, site))
            return new Notification("Unavailable", null, null);
        var core = pane.Core;
        int? httpStatus = null;
        void KeepResultOnPage(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // 115 may redirect immediately after showing the success notification.
            if (site.IsCloudTasksPage(e.Uri))
                e.Cancel = true;
        }
        void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (e.Request.Method == "POST" && site.IsCloudTaskSubmission(e.Request.Uri))
                httpStatus = e.Response.StatusCode;
        }
        core.WebResourceResponseReceived += OnResponse;
        core.NavigationStarting += KeepResultOnPage;
        try
        {
            for (var attempt = 0; attempt < 48 && CanContinue(pane, site); attempt++)
            {
                if (await pane.RunInPageAsync(submit))
                {
                    for (var poll = 0; poll < 120 && CanContinue(pane, site); poll++)
                    {
                        var json = await pane.FindPageResultAsync(result, value => value.StartsWith('{'));
                        if (json != null)
                        {
                            using var response = JsonDocument.Parse(json);
                            var outcome = response.RootElement.GetProperty("outcome").GetString()!;
                            Bitmap? image = null;
                            if (needsImage() && response.RootElement.TryGetProperty("clip", out var clip))
                                image = await CaptureToastAsync(pane, clip);
                            return new Notification(outcome, image, httpStatus);
                        }
                        await Task.Delay(100);
                    }
                    return new Notification(httpStatus.HasValue ? "ResponseWithoutToast" : "NoResponse", null, httpStatus);
                }
                await Task.Delay(250);
            }
            var waiting = CanContinue(pane, site) && await pane.RunInPageAsync(awaiting115);
            return new Notification(waiting ? "115NotReady" : "Unavailable", null, null);
        }
        finally
        {
            if (!pane.IsDisposed)
            {
                core.WebResourceResponseReceived -= OnResponse;
                core.NavigationStarting -= KeepResultOnPage;
            }
            if (CanContinue(pane, site))
                await pane.RunInPageAsync(cleanup);
        }
    }

    private static async Task<Bitmap?> CaptureToastAsync(BrowserPane pane, JsonElement clip)
    {
        try
        {
            var options = JsonSerializer.Serialize(new
            {
                format = "png", fromSurface = true, captureBeyondViewport = true, clip,
            });
            var screenshot = await pane.Core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", options);
            using var response = JsonDocument.Parse(screenshot);
            using var bytes = new MemoryStream(Convert.FromBase64String(response.RootElement.GetProperty("data").GetString()!));
            using var decoded = Image.FromStream(bytes);
            return new Bitmap(decoded);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException
                                  or ArgumentException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static bool CanContinue(BrowserPane pane, SitePolicy site) =>
        !pane.IsDisposed && pane.View.CoreWebView2 != null && site.IsCloudDownloadPage(pane.Core.Source);

    private static string OfficialScript(SitePolicy site, string body) => $$"""
        (() => {
          if (!{{JsonSerializer.Serialize(site.CloudOrigins)}}.includes(location.origin)) return false;
          const visible = element => Boolean(element && element.getClientRects().length &&
            getComputedStyle(element).visibility !== 'hidden');
          {{body}}
        })()
        """;
}
