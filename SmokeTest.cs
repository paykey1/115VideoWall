#if DEBUG
using System.Text.Json;
using System.Runtime.InteropServices;

namespace VideoWall115;

internal static class SmokeTest
{
    internal static int Run(Uri fixture, string evidence)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "115VideoWall-tests", Guid.NewGuid().ToString("N"));
        var exitCode = 1;
        using var log = new RunLog(scratch);
        using var main = new MainForm(scratch, log, new SitePolicy(fixture));
        main.Shown += async (_, _) =>
        {
            try
            {
                await WaitAsync(async () => main.FileBrowser?.View.CoreWebView2 != null &&
                    await main.FileBrowser.Core.ExecuteScriptAsync("Boolean(document.getElementById('one'))") == "true");

                var toolbar = main.BrowserToolbar;
                var icons = toolbar.Controls.OfType<FlowLayoutPanel>()
                    .SelectMany(group => group.Controls.OfType<ToolbarIconButton>()).ToArray();
                Check(icons.Length == 14 && icons.All(button => button.Text.Length == 0 &&
                      button.Width <= 32 && button.Height <= 32 &&
                      button.PointToScreen(Point.Empty).Y == icons[0].PointToScreen(Point.Empty).Y),
                      "Browser toolbar has 14 small icon-only buttons on one line.");
                var toolbarPreview = Path.ChangeExtension(evidence, ".toolbar.png");
                using (var preview = new Bitmap(toolbar.Width, toolbar.Height))
                {
                    toolbar.DrawToBitmap(preview, new Rectangle(Point.Empty, toolbar.Size));
                    preview.Save(toolbarPreview);
                }

                var file = main.FileBrowser!;
                await file.Core.ExecuteScriptAsync("document.getElementById('one').click()");
                await WaitAsync(() => Task.FromResult(main.Players.Count == 1));
                await file.Core.ExecuteScriptAsync("document.getElementById('two').click()");
                await WaitAsync(() => Task.FromResult(main.Players.Count == 2));
                var first = main.Players[0];
                var second = main.Players[1];
                await WaitAsync(async () => await TimeAsync(first) > 0.25 && await TimeAsync(second) > 0.25);
                await WaitAsync(async () =>
                    await first.Core.ExecuteScriptAsync("document.body.dataset.webFullscreen") == "\"true\"" &&
                    await second.Core.ExecuteScriptAsync("document.body.dataset.webFullscreen") == "\"true\"");
                Check(await first.Core.ExecuteScriptAsync("document.body.dataset.fullscreenClicks") == "\"1\"" &&
                      await second.Core.ExecuteScriptAsync("document.body.dataset.fullscreenClicks") == "\"0\"",
                      "Web fullscreen is opened once and leaves an already enabled page alone.");
                await WaitAsync(async () =>
                    await first.Core.ExecuteScriptAsync("document.body.dataset.promoClosed") == "\"1\"" &&
                    await second.Core.ExecuteScriptAsync("document.body.dataset.promoClosed") == "\"1\"");
                Check(await first.Core.ExecuteScriptAsync("document.body.dataset.otherClosed") == "\"0\"",
                    "The promotion watcher does not close unrelated controls.");
                await WaitAsync(() => Task.FromResult(first.PlaybackButton.Icon == ToolbarIcon.Pause &&
                    second.PlaybackButton.Icon == ToolbarIcon.Pause));
                second.PauseAllButton.PerformClick();
                await WaitAsync(async () => await PausedAsync(first) && await PausedAsync(second) &&
                    first.PlaybackButton.Icon == ToolbarIcon.Play && second.PauseAllButton.BackColor != Palette.Panel);
                first.PlayAllButton.PerformClick();
                await WaitAsync(async () => !await PausedAsync(first) && !await PausedAsync(second) &&
                    first.PlayAllButton.BackColor != Palette.Panel);
                var playerToolbar = Control.FromHandle(first.ToolbarHandle)!;
                var playerToolbarPreview = Path.ChangeExtension(evidence, ".player-toolbar.png");
                using (var preview = new Bitmap(playerToolbar.Width, playerToolbar.Height))
                {
                    playerToolbar.DrawToBitmap(preview, new Rectangle(Point.Empty, playerToolbar.Size));
                    preview.Save(playerToolbarPreview);
                }
                var window = main.VideoWall;
                Check(window.Visible && main.Visible && window.Owner == null,
                      "The file browser and wall are independent top-level windows.");
                var wallBounds = window.Bounds;
                var fileBounds = main.Bounds;
                var wallDocumentId = await first.Core.ExecuteScriptAsync("document.body.dataset.documentId");
                window.HandleShortcut(Keys.F11);
                await WaitAsync(() => Task.FromResult(window.Fullscreen && first.Parent!.ClientSize == window.ClientSize));
                Check(window.Bounds == Screen.FromControl(window).Bounds &&
                      !window.ChromeVisible && !first.ToolbarVisible && !second.ToolbarVisible &&
                      main.Visible && main.Bounds == fileBounds, "Immersive wall fills its monitor without changing the file window.");
                var firstPoint = first.PointToScreen(new Point(20, 100));
                var secondPoint = second.PointToScreen(new Point(20, 100));
                var videoBounds = first.View.Bounds;
                var startHover = Environment.TickCount64;
                main.UpdateWallPointer(firstPoint, startHover);
                Check(first.ToolbarVisible && !second.ToolbarVisible &&
                      first.View.Bounds == videoBounds, "Hover reveals only the targeted overlay without resizing its video.");
                Check(ChildWindowFromPointEx(first.Handle, new Point(28, 14), 1) == first.ToolbarHandle,
                      "The native toolbar sits above WebView2 and receives clicks.");
                main.UpdateWallPointer(secondPoint, startHover + 50);
                Check(first.ToolbarVisible && second.ToolbarVisible, "Switching video starts the first toolbar's hide delay.");
                main.UpdateWallPointer(secondPoint, startHover + 1049);
                Check(first.ToolbarVisible, "The toolbar remains visible just before the one-second deadline.");
                main.UpdateWallPointer(secondPoint, startHover + 1050);
                Check(!first.ToolbarVisible && second.ToolbarVisible &&
                      first.View.Bounds == videoBounds, "The departed toolbar hides after one second.");
                main.UpdateWallPointer(null, startHover + 1200);
                main.UpdateWallPointer(null, startHover + 2200);
                Check(!first.ToolbarVisible && !second.ToolbarVisible, "Leaving the wall hides all its tile controls.");
                window.HandleShortcut(Keys.Escape);
                Check(!window.Fullscreen && window.Bounds == wallBounds &&
                      window.ChromeVisible && first.ToolbarVisible && second.ToolbarVisible &&
                      wallDocumentId == await first.Core.ExecuteScriptAsync("document.body.dataset.documentId"),
                      "Leaving fullscreen restores the size, controls and live playback document.");
                Check(await first.Core.ExecuteScriptAsync("document.body.dataset.sharedProfile") == "\"yes\"" &&
                      await second.Core.ExecuteScriptAsync("document.body.dataset.sharedProfile") == "\"yes\"",
                      "Both player windows see the file browser's profile cookie.");
                first.PlaybackButton.PerformClick();
                await WaitAsync(async () => await PausedAsync(first) && !await PausedAsync(second) &&
                    first.PlaybackButton.Icon == ToolbarIcon.Play && second.PlaybackButton.Icon == ToolbarIcon.Pause);
                var pausedAt = await TimeAsync(first);
                var secondBefore = await TimeAsync(second);
                await Task.Delay(500);
                Check(Math.Abs(await TimeAsync(first) - pausedAt) < 0.05 &&
                      await TimeAsync(second) - secondBefore > 0.15,
                      "Pausing one video leaves the other playing.");
                Check(await first.CommandAsync("forward"), "First pane receives a seek.");
                await WaitAsync(async () => await TimeAsync(first) > pausedAt + 8);
                var id = await first.Core.ExecuteScriptAsync("document.body.dataset.documentId");

                first.MoveButton.PerformClick();
                await WaitAsync(() => Task.FromResult(first.Popout?.FormBorderStyle == FormBorderStyle.None));
                Check(id == await first.Core.ExecuteScriptAsync("document.body.dataset.documentId"),
                      "Moving to a borderless window preserves the loaded document.");
                first.MoveButton.PerformClick();
                await WaitAsync(() => Task.FromResult(first.Popout == null));
                Check(id == await first.Core.ExecuteScriptAsync("document.body.dataset.documentId") &&
                      await PausedAsync(first), "Docking preserves the paused video.");

                for (var i = 3; i <= 6; i++)
                {
                    var expected = i;
                    await file.Core.ExecuteScriptAsync($"document.getElementById('v{expected}').click()");
                    await WaitAsync(() => Task.FromResult(main.Players.Count == expected));
                }
                Check(await first.CommandAsync("play"), "First pane receives resume.");
                await WaitAsync(async () =>
                {
                    foreach (var pane in main.Players)
                        if (await PausedAsync(pane) || await TimeAsync(pane) <= 0.1)
                            return false;
                    return true;
                });
                var times = await Task.WhenAll(main.Players.Select(TimeAsync));
                await Task.Delay(450);
                var advanced = await Task.WhenAll(main.Players.Select(TimeAsync));
                Check(times.Zip(advanced).All(pair => Math.Abs(pair.First - pair.Second) > 0.1),
                      "All six playback clocks advance together.");
                var wall = first.Parent as TableLayoutPanel;
                Check(wall is { ColumnCount: 3, RowCount: 2 } &&
                      main.Players.All(pane => pane.Parent == wall), "Six videos form a 3 × 2 wall.");
                var previousSize = window.Size;
                window.Size = window.MinimumSize;
                var narrowControls = Control.FromHandle(first.ToolbarHandle)!.Controls
                    .OfType<FlowLayoutPanel>().Single();
                Check(narrowControls.Width <= first.ClientSize.Width,
                      "Player buttons fit the smallest supported six-tile wall.");
                window.Size = previousSize;
                Check(main.FormBorderStyle == FormBorderStyle.None &&
                      main.VideoWall.FormBorderStyle == FormBorderStyle.None,
                      "Normal browser and wall windows use custom borderless chrome.");
                var otherOrigin = new UriBuilder(fixture) { Host = "localhost", Path = "/external" }.Uri;
                var sourceBeforeAddress = file.Core.Source;
                var countBeforeAddress = main.TabCount;
                var addressBox = (TextBox)toolbar.Controls.Find("BrowserAddress", true).Single();
                Check(addressBox.ReadOnly, "The pinned 115 tab has a read-only address.");
                addressBox.Text = otherOrigin.AbsoluteUri;
                SendMessage(addressBox.Handle, 0x0100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                SendMessage(addressBox.Handle, 0x0101, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                Check(main.TabCount == countBeforeAddress && file.Core.Source == sourceBeforeAddress,
                    "Enter cannot open an address from the pinned tab.");
                var blank = await main.OpenTabAsync("about:blank");
                Check(blank != null && !addressBox.ReadOnly, "Ordinary tabs have an editable address.");
                countBeforeAddress = main.TabCount;
                addressBox.Text = otherOrigin.AbsoluteUri;
                SendMessage(addressBox.Handle, 0x0100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                SendMessage(addressBox.Handle, 0x0101, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                await WaitAsync(() => Task.FromResult(main.TabCount == countBeforeAddress + 1 &&
                    main.SelectedBrowser()?.View.CoreWebView2?.Source == otherOrigin.AbsoluteUri));
                var external = main.SelectedBrowser();
                Check(external != null && external != file && file.Core.Source == sourceBeforeAddress,
                    "Submitting the address bar opens a new tab and leaves the 115 tab unchanged.");
                await WaitAsync(async () => await external!.Core.ExecuteScriptAsync(
                    "Boolean(document.getElementById('magnet'))") == "true");
                main.BookmarkCurrentPage();
                var tabs = main.Controls.OfType<BrowserTabs>().Single();
                var tabStrip = tabs.Strip;
                var tabIndex = tabs.SelectedIndex;
                var tabRect = tabStrip.GetTabRect(tabIndex);
                var buttonPoint = (tabRect.Right - 15) | ((tabRect.Top + tabRect.Height / 2) << 16);
                var beforeClose = main.TabCount;
                SendMessage(tabStrip.Handle, 0x0201, new IntPtr(1), new IntPtr(buttonPoint));
                SendMessage(tabStrip.Handle, 0x0202, IntPtr.Zero, new IntPtr(buttonPoint));
                Check(main.TabCount == beforeClose - 1 && external!.IsDisposed,
                    "Clicking X closes the corresponding tab.");
                await main.ReopenClosedTabAsync();
                Check(main.TabCount == beforeClose && tabs.SelectedIndex == tabIndex,
                    "The recently closed page opens in the previous tab position.");
                external = main.SelectedBrowser();
                await WaitAsync(async () => await external!.Core.ExecuteScriptAsync(
                    "Boolean(document.getElementById('magnet'))") == "true");
                var star = icons.Single(icon => icon.Icon == ToolbarIcon.Starred);
                star.PerformClick();
                var bookmarksBar = (ToolStrip)main.Controls["BookmarksBar"]!;
                Check(bookmarksBar.Visible && bookmarksBar.Top == toolbar.Bottom &&
                      bookmarksBar.Bottom == tabs.Top &&
                      bookmarksBar.Items.Cast<ToolStripItem>().Any(item => item.Text == "管理收藏"),
                    "The star shows a horizontal Chinese bookmark bar between the address and tabs.");
                var savedItem = bookmarksBar.Items.OfType<ToolStripButton>()
                    .Single(item => item.ToolTipText == otherOrigin.AbsoluteUri);
                var countBeforeBookmark = main.TabCount;
                savedItem.PerformClick();
                star.PerformClick();
                Check(!bookmarksBar.Visible, "The star also hides the bookmark bar.");
                await WaitAsync(() => Task.FromResult(main.TabCount == countBeforeBookmark + 1 &&
                    main.SelectedBrowser()?.View.CoreWebView2?.Source == otherOrigin.AbsoluteUri));
                external = main.SelectedBrowser();
                await WaitAsync(async () => await external!.Core.ExecuteScriptAsync(
                    "Boolean(document.getElementById('magnet'))") == "true");
                var activePage = tabs.SelectedTab;
                var beforeCtrlClick = main.TabCount;
                var activeAddress = addressBox.Text;
                await external!.Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    "{\"type\":\"mousePressed\",\"x\":25,\"y\":140,\"button\":\"left\",\"clickCount\":1,\"modifiers\":2}");
                await external.Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    "{\"type\":\"mouseReleased\",\"x\":25,\"y\":140,\"button\":\"left\",\"clickCount\":1,\"modifiers\":2}");
                await WaitAsync(() => Task.FromResult(main.TabCount == beforeCtrlClick + 1 &&
                    (tabs.Pages[^1].Tag as BrowserPane)?.View.CoreWebView2?.Source.EndsWith("?q=background") == true));
                Check(tabs.SelectedTab == activePage && addressBox.Text == activeAddress && external.View.Visible,
                    "Ctrl-click loads in a background tab without changing the visible page.");
                var backgroundTab = tabs.Pages[^1];
                var previousIndex = tabs.IndexOf(activePage!);
                var start = tabStrip.GetTabRect(previousIndex);
                var destination = tabStrip.GetTabRect(tabs.IndexOf(backgroundTab));
                var y = start.Top + start.Height / 2;
                SendMessage(tabStrip.Handle, 0x0201, new IntPtr(1), new IntPtr((start.Left + 12) | (y << 16)));
                SendMessage(tabStrip.Handle, 0x0200, new IntPtr(1), new IntPtr((destination.Right - 32) | (y << 16)));
                SendMessage(tabStrip.Handle, 0x0202, IntPtr.Zero, new IntPtr((destination.Right - 32) | (y << 16)));
                Check(tabs.IndexOf(activePage!) == previousIndex + 1 && tabs.Pages[0].Tag == file &&
                      tabs.SelectedTab == activePage && main.SelectedBrowser() == external,
                    "Dragging reorders tabs while preserving the active WebView and the pinned 115 tab.");
                await WaitAsync(() => Task.FromResult(activePage!.Icon != null));
                var tabsPreview = Path.ChangeExtension(evidence, ".tabs.png");
                using (var preview = new Bitmap(tabStrip.Width, tabStrip.Height))
                {
                    tabStrip.DrawToBitmap(preview, tabStrip.ClientRectangle);
                    preview.Save(tabsPreview);
                }
                var tabCount = main.TabCount;
                var status = main.Controls.OfType<Label>().Single().Text;
                Check(!file.View.Visible, "The 115 pane starts out in the background.");
                async Task CheckMagnetAsync(int submissions, string message, Color expectedColor, Func<Task> trigger)
                {
                    await trigger();
                    await WaitAsync(async () => main.CloudBrowser?.View.CoreWebView2 != null &&
                        await main.CloudBrowser.Core.ExecuteScriptAsync(
                            $"document.body.dataset.submissions === '{submissions}'") == "true");
                    await WaitAsync(() => Task.FromResult(main.OwnedForms.OfType<CloudToastWindow>()
                        .Any(toast => toast.BackgroundImage is Bitmap bitmap &&
                            bitmap.GetPixel(bitmap.Width - 5, bitmap.Height - 5).ToArgb() == expectedColor.ToArgb())));
                    Check(await main.CloudBrowser!.Core.ExecuteScriptAsync(
                        $"document.querySelector('.ex-popoup-hint [rel=con]').textContent === '{message}'") == "true",
                        "The relayed notification contains the website result.");
                    Check(main.TabCount == tabCount && main.SelectedBrowser() == external &&
                          !main.ClientRectangle.IntersectsWith(main.CloudBrowser.Bounds) && file.Core.Source == sourceBeforeAddress &&
                          await file.Core.ExecuteScriptAsync("document.body.dataset.submissions") == "\"0\"" &&
                          main.Controls.OfType<Label>().Single().Text == status,
                        "Cloud download keeps the primary source, selected tab, tab count and status unchanged.");
                }
                await CheckMagnetAsync(1, "成功添加云下载", Color.FromArgb(54, 183, 75), async () =>
                {
                    await external!.Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                        "{\"type\":\"mousePressed\",\"x\":25,\"y\":25,\"button\":\"left\",\"clickCount\":1}");
                    await external.Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                        "{\"type\":\"mouseReleased\",\"x\":25,\"y\":25,\"button\":\"left\",\"clickCount\":1}");
                });
                await CheckMagnetAsync(2, "任务已存在，请勿输入重复的链接地址", Color.FromArgb(253, 156, 7), () =>
                {
                    addressBox.Text = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
                    SendMessage(addressBox.Handle, 0x0100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                    SendMessage(addressBox.Handle, 0x0101, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                    return Task.CompletedTask;
                });
                var worker = main.CloudBrowser;
                await CheckMagnetAsync(3, "成功添加云下载", Color.FromArgb(54, 183, 75), async () =>
                {
                    await external!.Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                        "{\"type\":\"mousePressed\",\"x\":25,\"y\":80,\"button\":\"left\",\"clickCount\":1}");
                    await external.Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                        "{\"type\":\"mouseReleased\",\"x\":25,\"y\":80,\"button\":\"left\",\"clickCount\":1}");
                });
                await Task.Delay(1100);
                await CheckMagnetAsync(4, "任务已存在，请勿输入重复的链接地址", Color.FromArgb(253, 156, 7), () =>
                {
                    addressBox.Text = "ed2k://|file|sample.bin|42|0123456789abcdef0123456789abcdef|/";
                    SendMessage(addressBox.Handle, 0x0100, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                    SendMessage(addressBox.Handle, 0x0101, new IntPtr((int)Keys.Enter), IntPtr.Zero);
                    return Task.CompletedTask;
                });
                Check(main.CloudBrowser == worker && await worker!.Core.ExecuteScriptAsync("Boolean(document.body.dataset.clickedTooEarly)") == "false",
                    "Cloud download never clicks Start before the 115 plugin is ready.");
                var logFile = Directory.GetFiles(log.DirectoryPath!, "*.jsonl").Single();
                using (var reader = new StreamReader(File.Open(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    var magnetLog = reader.ReadToEnd().Split('\n')
                        .Where(line => line.Contains("\"name\":\"cloud_download_result\"")).ToArray();
                    Check(magnetLog.Length == 4 && magnetLog.All(line => line.Contains("\"HttpStatus\":200")),
                        "Both magnet actions reach the Cloud Download endpoint.");
                }
                var demo = new LoginDetails("wall-qa", "fixture-only");
                var testVault = new WindowsLoginStore("115VideoWall/test/" + Guid.NewGuid().ToString("N"));
                try
                {
                    testVault.Save(demo);
                    var restored = testVault.Load();
                    Check(restored?.Account == demo.Account && restored.Password == demo.Password,
                        "The Windows vault saves and reloads an isolated fixture entry.");
                }
                finally
                {
                    testVault.Forget();
                }
                Check(testVault.Load() == null, "The test vault entry was removed.");

                var accountTab = await main.OpenTabAsync(new Uri(fixture, "/account-login").AbsoluteUri);
                Check(accountTab != null, "The account login fixture opened.");
                await WaitAsync(async () => await accountTab!.Core.ExecuteScriptAsync(
                    "Boolean(document.getElementById('js-login_form'))") == "true");
                var login = await LoginAutomation.TryAsync(accountTab!, new SitePolicy(fixture), demo);
                Check(login == LoginOutcome.NeedsVerification &&
                      await accountTab!.Core.ExecuteScriptAsync(
                          "document.body.dataset.valid === 'true' && document.body.dataset.submits === '1'") == "true",
                    "Auto sign-in selects the agreement, submits once and stops at verification.");

                var challengeTab = await main.OpenTabAsync(new Uri(fixture, "/verify-first").AbsoluteUri);
                Check(challengeTab != null, "The existing verification fixture opened.");
                await WaitAsync(async () => await challengeTab!.Core.ExecuteScriptAsync(
                    "Boolean(document.getElementById('js-mvcode'))") == "true");
                var challenge = await LoginAutomation.TryAsync(challengeTab!, new SitePolicy(fixture), demo);
                Check(challenge == LoginOutcome.NeedsVerification &&
                      await challengeTab!.Core.ExecuteScriptAsync(
                          "document.body.dataset.submits === '0' && !document.getElementById('js-passwd').value") == "true",
                      "Existing verification prevents credential entry or submission.");
                Check(await LoginAutomation.TryAsync(external, new SitePolicy(fixture), demo) == LoginOutcome.Unavailable,
                    "An unrelated origin is ineligible for auto sign-in.");
                var redirect = new UriBuilder(fixture) { Host = "localhost", Path = "/login-redirect" }.Uri;
                external.Core.Navigate(redirect.AbsoluteUri);
                await WaitAsync(async () => await external.Core.ExecuteScriptAsync(
                    "Boolean(document.getElementById('login-complete'))") == "true");
                Check(new Uri(external.Core.Source).Host == fixture.Host,
                    "Browser allows the cross-origin post-login navigation.");
                var historyUri = new Uri(fixture, "/query-page?q=search");
                var tracked = await main.OpenTabAsync(historyUri.AbsoluteUri);
                Check(tracked != null, "History fixture opened.");
                await WaitAsync(async () => await tracked!.Core.ExecuteScriptAsync(
                    "Boolean(document.getElementById('history-fixture'))") == "true");
                main.CloseBrowserTab(tracked!);
                Check(!BrowserDataStore.CanRemember("https://example.test/callback?auth_token=fixture")
                      && !BrowserDataStore.CanRemember("https://example.test/#/auth?access_token=fixture"),
                    "Temporary credential links do not enter browser history.");
                await second.Core.ExecuteScriptAsync("window.wallMoveMarker = 'retained'");
                var outside = new Point(SystemInformation.VirtualScreen.Left - 30, SystemInformation.VirtualScreen.Top - 30);
                var release = second.PointToClient(outside);
                SendMessage(second.DragTitle.Handle, 0x0201, new IntPtr(1), new IntPtr(10 | (10 << 16)));
                SendMessage(second.Handle, 0x0202, IntPtr.Zero,
                    new IntPtr((release.X & 0xFFFF) | ((release.Y & 0xFFFF) << 16)));
                var otherWall = (WallWindow)second.FindForm()!;
                Check(otherWall != window && otherWall.Players.SequenceEqual(new[] { second }),
                    "Dropping outside creates another wall with the same video.");
                await file.Core.ExecuteScriptAsync("window.open('/watch/7', '_blank')");
                await WaitAsync(() => Task.FromResult(main.Players.Count == 7 && otherWall.Players.Count == 2));
                var seventh = main.Players[^1];
                main.MoveVideoToWall(seventh, null, otherWall.Location);
                var thirdWall = (WallWindow)seventh.FindForm()!;
                thirdWall.Bounds = otherWall.Bounds;
                var mergePoint = seventh.PointToClient(new Point(otherWall.Left + 90, otherWall.Top + 86));
                SendMessage(seventh.DragTitle.Handle, 0x0201, new IntPtr(1), new IntPtr(10 | (10 << 16)));
                SendMessage(seventh.Handle, 0x0202, IntPtr.Zero,
                    new IntPtr((mergePoint.X & 0xFFFF) | ((mergePoint.Y & 0xFFFF) << 16)));
                Check(thirdWall.IsDisposed && otherWall.Players.Contains(seventh),
                    "Dragging from an overlapping detached wall merges the video into the other wall.");
                main.MoveVideoToWall(second, window, window.Location);
                Check(window.Players.Contains(second) && !otherWall.Players.Contains(second) &&
                      await second.Core.ExecuteScriptAsync("window.wallMoveMarker") == "\"retained\"",
                    "New videos prefer the destination wall and a transfer keeps the loaded video document.");
                var customGrid = new WallGridSettings(false, 1, 2);
                var settingsPreview = Path.ChangeExtension(evidence, ".settings.png");
                main.BeginInvoke((MethodInvoker)(() =>
                {
                    var settings = Application.OpenForms.Cast<Form>().Single(form => form.Text == "设置");
                    var nav = settings.Controls.OfType<FlowLayoutPanel>().Single();
                    var details = (Panel)settings.Controls["SettingsContent"]!;
                    nav.Controls.OfType<Button>().Single(button => button.Text == "关于").PerformClick();
                    Check(details.Controls.Cast<Panel>().Single(page => page.Visible).Controls.OfType<FlowLayoutPanel>().Single()
                        .Controls.OfType<Label>().Any(label => label.Text.Contains("1.0.0") && label.Text.Contains("2026-11-22")),
                        "About displays the release version.");
                    using (var preview = new Bitmap(settings.Width, settings.Height))
                    {
                        settings.DrawToBitmap(preview, new Rectangle(Point.Empty, settings.Size));
                        preview.Save(settingsPreview);
                    }
                    nav.Controls.OfType<Button>().Single(button => button.Text == "视频墙").PerformClick();
                    var options = details.Controls.Cast<Panel>().Single(page => page.Visible).Controls.OfType<TableLayoutPanel>().Single();
                    options.Controls.OfType<CheckBox>().Single().Checked = false;
                    var numbers = options.Controls.OfType<NumericUpDown>().ToArray();
                    numbers[0].Value = customGrid.Columns;
                    numbers[1].Value = customGrid.Rows;
                    options.Controls.OfType<Button>().Single(button => button.Text == "保存").PerformClick();
                }));
                icons.Single(icon => icon.Icon == ToolbarIcon.Settings).PerformClick();
                Check(main.Players.Count == 7 &&
                      main.Players.All(video => video.FindForm() is WallWindow w && w.Players.Count <= 2 &&
                          w.Grid.ColumnCount == 1 && w.Grid.RowCount == 2) &&
                      new LocalJsonFile<WallGridSettings>(scratch, "wall-grid", log).Read() == customGrid,
                    "A smaller saved grid redistributes all playing videos.");
                Check(main.SaveWallGrid(new WallGridSettings(true, 3, 2)), "The automatic layout can be restored.");
                var boss = new BossShortcut(true, Keys.Control | Keys.Alt | Keys.Shift | Keys.F9);
                Check(RegisterHotKey(main.Handle, 777, 7, (uint)Keys.F9), "The test reserves a shortcut.");
                try
                {
                    Check(main.SaveBossShortcut(boss)?.Contains("已被占用") == true,
                        "Shortcut registration reports an actual Windows conflict.");
                }
                finally
                {
                    UnregisterHotKey(main.Handle, 777);
                }
                Check(main.SaveBossShortcut(boss) == null, "An available custom shortcut registers.");
                await first.CommandAsync("play");
                SendMessage(main.Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
                await WaitAsync(async () => !main.Visible && !window.Visible && first.Core.IsMuted &&
                    await first.Core.ExecuteScriptAsync("document.querySelector('video').paused") == "true");
                SendMessage(main.Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
                await WaitAsync(() => Task.FromResult(main.Visible && window.Visible));
                Check(await first.Core.ExecuteScriptAsync("document.querySelector('video').paused") == "true" &&
                      main.SaveBossShortcut(boss with { Enabled = false }) == null,
                    "Restoring from the shortcut leaves the video paused.");
                var closingWall = (WallWindow)seventh.FindForm()!;
                Check(closingWall != window, "The wall-close fixture has its own window.");
                closingWall.Close();
                await WaitAsync(() => Task.FromResult(seventh.IsDisposed));
                Check(!first.IsDisposed && main.Players.Contains(first),
                    "Closing a wall disposes its own playback WebView and leaves another wall intact.");
                if (!second.IsDisposed)
                {
                    var closeVideo = second.Controls.OfType<Panel>().Single().Controls.OfType<FlowLayoutPanel>().Single()
                        .Controls.OfType<ToolbarIconButton>().Single(button => button.Icon == ToolbarIcon.CloseTab);
                    closeVideo.PerformClick();
                    Check(second.IsDisposed && !first.IsDisposed, "Closing one tile ends only that playback page.");
                }
                main.Bounds = LayoutFixtureBounds(915, 640, 30, 36);
                window.Bounds = LayoutFixtureBounds(865, 570, 80, 74);
                first.MoveButton.PerformClick();
                await WaitAsync(() => Task.FromResult(first.Popout != null));
                first.Popout!.Bounds = LayoutFixtureBounds(520, 310, 120, 108);
                var layout = Path.Combine(scratch, "window-layout.json");
                await WaitAsync(() => Task.FromResult(new WindowLayoutStore(scratch, log).Get("player-1")?.Rectangle ==
                    LayoutFixtureBounds(520, 310, 120, 108)));
                window.SetFullscreen(true);
                main.MaximizeOnCurrentScreen();
                main.WindowState = FormWindowState.Minimized;
                var oldScreen = new Rectangle(-1600, 0, 1600, 900);
                var newScreen = new Rectangle(0, 0, 1280, 720);
                var oldWindow = new Rectangle(-1500, 90, 850, 620);
                Check(WindowPlacement.FitOnScreens(oldWindow, [oldScreen, newScreen], newScreen) == oldWindow &&
                      newScreen.Contains(WindowPlacement.FitOnScreens(oldWindow, [newScreen], newScreen)),
                      "Preserve a window on a connected monitor and recover it after disconnect.");
                File.WriteAllText(evidence, JsonSerializer.Serialize(new
                {
                    passed = true, players = main.Players.Count, independentPause = true,
                    seek = true, borderlessPopout = true, preservedDocument = true,
                    sharedProfile = true, concurrentPlayback = true, grid = "3x2",
                    websiteFullscreen = true, independentWall = true, immersiveWall = true, hoverToolbar = true, logDirectory = log.DirectoryPath, profileData = scratch,
                    borderlessBrowser = true, thirdPartyTabs = true, addressOpensNewTab = true, pinnedAddressReadOnly = true,
                    backgroundMagnet = true, magnetFromAddress = true, waitsForCloudAuthorization = true,
                    nativeSuccessAndDuplicate = true, backgroundCloudBrowser = true, ed2k = true,
                    wallTransfer = true, preferredWallRouting = true, loginRedirect = true, closeTabWithX = true, bookmarksBar = true,
                    backgroundCtrlClick = true, dragTabReorder = true, tabsPreview, customWallGrid = true, overlappingWallDrop = true,
                    settingsNavigation = true, settingsPreview, bossShortcutAndConflict = true, wallClosesPlayback = true,
                    compactToolbar = true, toolbarPreview,
                    credentialVault = true, autoSignInStopsForVerification = true,
                    promoClosed = true, localPlaybackToggle = true, groupButtons = true, playerToolbarPreview,
                }));
                exitCode = 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(evidence, JsonSerializer.Serialize(new
                {
                    passed = false, error = ex.ToString(), logDirectory = log.DirectoryPath,
                }));
            }
            finally
            {
                main.Close();
            }
        };
        Application.Run(main);
        return exitCode;
    }

    internal static int VerifyRestart(Uri fixture, string profileData, string evidence)
    {
        var passed = false;
        var vault = new WindowsLoginStore("115VideoWall/test/" + Guid.NewGuid().ToString("N"));
        vault.Save(new LoginDetails("wall-qa", "fixture-only"));
        using var log = new RunLog(profileData);
        using var main = new MainForm(profileData, log, new SitePolicy(new Uri(fixture, "/account-login")), vault);
        main.Shown += async (_, _) =>
        {
            try
            {
                await WaitAsync(async () => main.FileBrowser?.View.CoreWebView2 != null &&
                    await main.FileBrowser!.Core.ExecuteScriptAsync(
                        "document.body.dataset.valid === 'true' && document.body.dataset.submits === '1'") == "true");
                var expectedHistory = new Uri(fixture, "/query-page?q=search").AbsoluteUri;
                var expectedBookmark = new UriBuilder(fixture) { Host = "localhost", Path = "/external" }.Uri.AbsoluteUri;
                using (var browserData = new BrowserDataStore(profileData, log))
                {
                    Check(browserData.Bookmarks.Any(page => page.Address == expectedBookmark) &&
                          browserData.History.Any(page => page.Address == expectedHistory) &&
                          browserData.LastClosed?.Page.Address == expectedHistory,
                          "Bookmarks, visit history, and recently closed tabs persist across restart.");
                }
                await main.ReopenClosedTabAsync();
                await WaitAsync(() => Task.FromResult(main.SelectedBrowser()?.View.CoreWebView2?.Source == expectedHistory));
                Check(main.WindowState == FormWindowState.Maximized &&
                      main.RestoreBounds == LayoutFixtureBounds(915, 640, 30, 36),
                      "The browser opens maximized with its previous normal bounds even if closed minimized.");
                var video = await main.OpenTabAsync(new Uri(fixture, "/watch/1").AbsoluteUri);
                Check(video != null, "The test player reopened.");
                await WaitAsync(() => Task.FromResult(main.VideoWall.Fullscreen && main.Players.Count == 1));
                Check(main.VideoWall.CapturePlacement().Rectangle == LayoutFixtureBounds(865, 570, 80, 74),
                      "The wall restores fullscreen and its previous normal bounds.");
                main.VideoWall.SetFullscreen(false);
                Check(main.VideoWall.Bounds == LayoutFixtureBounds(865, 570, 80, 74),
                      "The restored wall returns to its saved normal size and position.");
                var player = main.Players[0];
                player.MoveButton.PerformClick();
                await WaitAsync(() => Task.FromResult(player.Popout != null));
                Check(player.Popout!.Bounds == LayoutFixtureBounds(520, 310, 120, 108),
                      "The detached player restores its saved position and size.");
                var browser = await main.OpenTabAsync(new Uri(fixture, "/verify-login").AbsoluteUri);
                if (browser == null)
                    throw new InvalidOperationException("The verification tab did not open.");
                await WaitAsync(async () => await browser.Core.ExecuteScriptAsync(
                    "Boolean(document.getElementById('session-persisted'))") == "true");
                passed = true;
                var sessionCookie = await browser.Core.ExecuteScriptAsync(
                    "document.getElementById('session-persisted')?.dataset.session") == "\"true\"";
                File.WriteAllText(evidence, JsonSerializer.Serialize(new { passed, persistentCookie = true, sessionCookie, autoSignInOnStartup = true, windowPlacementRestored = true, browserLibraryRestored = true }));
            }
            catch (Exception ex)
            {
                File.WriteAllText(evidence, JsonSerializer.Serialize(new { passed, error = ex.ToString() }));
            }
            finally
            {
                main.Close();
            }
        };
        try
        {
            Application.Run(main);
            return passed ? 0 : 1;
        }
        finally
        {
            vault.Forget();
        }
    }

    internal static int CheckLanding(string evidence)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "115VideoWall-tests", Guid.NewGuid().ToString("N"));
        var passed = false;
        using var log = new RunLog(scratch);
        using var main = new MainForm(scratch, log);
        main.Shown += async (_, _) =>
        {
            try
            {
                await WaitAsync(async () =>
                {
                    if (main.FileBrowser?.View.CoreWebView2 == null ||
                        !Uri.TryCreate(main.FileBrowser.Core.Source, UriKind.Absolute, out var address) ||
                        address.Scheme != Uri.UriSchemeHttps || !new SitePolicy().Is115Page(address.AbsoluteUri))
                        return false;
                    return await main.FileBrowser.Core.ExecuteScriptAsync(
                        "Boolean(document.body && document.body.innerText.trim().length > 10)") == "true";
                });
                var pane = main.FileBrowser!;
                var page = new Uri(pane.Core.Source);
                await Task.Delay(1500);
                var ui = await pane.Core.ExecuteScriptAsync("""
                    (() => {
                      const visible = node => Boolean(node?.getClientRects().length);
                      return {
                        formExists: Boolean(document.querySelector('#js-login_form #js-account')),
                        formVisible: visible(document.querySelector('#js-login_form')),
                        accountMethodVisible: visible(document.querySelector('a[lgb-nav="login"]')),
                        publicLoginEntries: [...document.querySelectorAll('a,button,span')]
                          .filter(n => visible(n) && n.children.length === 0 && n.textContent.trim() === '登录')
                          .slice(0, 4).map(n => ({ id: n.id, className: String(n.className), rel: n.getAttribute('rel') }))
                      };
                    })()
                    """);
                await pane.Core.ExecuteScriptAsync("""
                    (() => {
                      const method = document.querySelector('a[lgb-nav="login"]');
                      if (method?.getClientRects().length) method.click();
                    })()
                    """);
                var officialForm = await pane.Core.ExecuteScriptAsync("""
                    Boolean(document.querySelector('#js-login_form #js-passwd')?.getClientRects().length &&
                      document.querySelector('[rel="login_agreement"] [role="checkbox"]')?.getClientRects().length)
                    """) == "true";
                File.WriteAllText(evidence, JsonSerializer.Serialize(new {
                    passed = true, officialHost = page.IdnHost, logDirectory = log.DirectoryPath,
                    loginUi = JsonSerializer.Deserialize<JsonElement>(ui), officialForm,
                }));
                passed = true;
            }
            catch (Exception ex)
            {
                File.WriteAllText(evidence, JsonSerializer.Serialize(new {
                    passed = false, error = ex.ToString(), logDirectory = log.DirectoryPath,
                }));
            }
            finally
            {
                main.Close();
            }
        };
        Application.Run(main);
        return passed ? 0 : 1;
    }

    private static Rectangle LayoutFixtureBounds(int width, int height, int x, int y)
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        return new Rectangle(area.X + x, area.Y + y, width, height);
    }

    private static async Task<double> TimeAsync(BrowserPane pane)
    {
        if (pane.View.CoreWebView2 == null)
            return 0;
        var time = await pane.Core.ExecuteScriptAsync("document.querySelector('video')?.currentTime || 0");
        return JsonSerializer.Deserialize<double>(time);
    }

    private static async Task<bool> PausedAsync(BrowserPane pane) =>
        await pane.Core.ExecuteScriptAsync("Boolean(document.querySelector('video')?.paused)") == "true";

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition())
            await Task.Delay(80, timeout.Token);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr ChildWindowFromPointEx(IntPtr parent, Point point, uint flags);

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif
