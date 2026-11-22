"""Exercise the debug build with a local H.264 MP4 and a fake 115 file page."""
import argparse
import json
import os
import re
import subprocess
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from tempfile import gettempdir
from threading import Thread


def serve(sample):
    class Fixture(BaseHTTPRequestHandler):
        def do_GET(self):
            path = self.path.split("?", 1)[0]
            if path == "/":
                links = """
                  <a id="one" target="_blank" href="/watch/1">First video</a>
                  <button id="two" onclick="const w=window.open('about:blank');w.location='/watch/2'">Second video</button>
                  <a id="v3" href="/watch/3">Same-tab video</a>
                """
                links += "\n".join(
                    f'<a id="v{i}" target="_blank" href="/watch/{i}">Video {i}</a>'
                    for i in range(4, 7)
                )
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Set-Cookie", "shared_profile=yes; Path=/; HttpOnly; SameSite=Lax")
                self.end_headers()
                self.wfile.write(("<!doctype html><meta charset='utf-8'>" + links + """
                    <style>
                    .ex-popoup-hint { position:fixed; top:95px; left:100px; width:180px; height:125px; color:white; z-index:10; }
                    .exph-suc { background:rgb(54,183,75) }
                    .exph-err { background:rgb(253,156,7) }
                    </style>
                    <button id="upload">上传</button>
                    <div class="offline-box" hidden>
                    <div class="dialog-handle"><button btn="close">关闭</button></div>
                    <textarea id="js_offline_new_add"></textarea>
                    <button data-btn="start">开始下载</button>
                    </div>
                    <div class="ex-popoup-hint" hidden><span rel="con"></span></div>
                    <script>
                    document.body.dataset.submissions='0';
                    const offline = {};
                    window.Core = { OFFL5Plug: { GetGlobals: () => offline } };
                    document.getElementById('upload').addEventListener('mouseover', () => {
                      if (document.getElementById('cloud-action')) return;
                      const entry = document.createElement('button');
                      entry.id = 'cloud-action';
                      entry.textContent = '添加云下载';
                      entry.onclick = () => {
                        document.querySelector('.offline-box').hidden = false;
                        if (!offline.KEY_OBJ) setTimeout(() => { offline.KEY_OBJ = {}; }, 1400);
                      };
                      document.body.appendChild(entry);
                    });
                    const field = document.getElementById('js_offline_new_add');
                    const dialog = field.closest('.offline-box');
                    const close = () => { dialog.hidden = true; field.value = ''; };
                    document.querySelector('[btn=close]').onclick = close;
                    let hide;
                    document.querySelector('[data-btn="start"]').onclick = async () => {
                      if (!/^(magnet:|ed2k:)/i.test(field.value)) return;
                      if (!offline.KEY_OBJ) {
                        document.body.dataset.clickedTooEarly = 'true';
                        throw new Error('Cloud authorization not loaded');
                      }
                      const response = await fetch('/web/lixian/?ct=lixian&ac=add_task_url', {
                        method: 'POST', body: field.value
                      });
                      if (!response.ok) return;
                      const first = ++document.body.dataset.submissions % 2 === 1;
                      if (first) close();
                      setTimeout(() => {
                        const toast = document.querySelector('.ex-popoup-hint');
                        toast.querySelector('[rel=con]').textContent = first ?
                          '成功添加云下载' : '任务已存在，请勿输入重复的链接地址';
                        toast.className = 'ex-popoup-hint ' + (first ? 'exph-suc' : 'exph-err');
                        toast.hidden = false;
                        clearTimeout(hide);
                        hide = setTimeout(() => toast.hidden = true, 2000);
                        if (first) location.href = '/?tab=offline&mode=wangpan';
                      }, 220);
                    };
                    </script>""").encode())
            elif path == "/external":
                link = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"
                page = '<title>Background links test</title><link rel="icon" href="/tab-icon.svg">'
                page += f'<a id="magnet" style="position:absolute;left:10px;top:10px;width:180px;height:45px" href="{link}">Magnet fixture</a>'
                page += '<a id="ed2k" style="position:absolute;left:10px;top:65px;width:180px;height:45px" href="ed2k://|file|sample.bin|42|0123456789abcdef0123456789abcdef|/">ED2K fixture</a>'
                page += '<a style="position:absolute;left:10px;top:120px;width:180px;height:45px" href="/query-page?q=background">Background tab fixture</a>'
                self.send_response(200)
                self.send_header("Content-Type", "text/html")
                self.end_headers()
                self.wfile.write(page.encode())
            elif path == "/tab-icon.svg":
                self.send_response(200)
                self.send_header("Content-Type", "image/svg+xml")
                self.end_headers()
                self.wfile.write(b'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect width="16" height="16" rx="4" fill="#36b74b"/><circle cx="8" cy="8" r="4" fill="#fff"/></svg>')
            elif path in {"/account-login", "/verify-first"}:
                visible = "true" if path == "/verify-first" else "false"
                page = f"""<!doctype html><meta charset="utf-8">
                <a id="login-method" href="#" lgb-nav="login">使用账号登录</a>
                <form id="js-login_form" hidden>
                    <input id="js-account" name="account">
                    <input id="js-passwd" name="passwd" type="password">
                    <button type="button" id="js-submit">登录</button>
                </form>
                <div rel="login_agreement" hidden>
                    <button type="button" role="checkbox" aria-checked="false">选择</button>
                    <span>我已阅读并同意</span>
                </div>
                <input id="js-mvcode" autocomplete="one-time-code" hidden>
                <script>
                const q = selector => document.querySelector(selector);
                const form = q('form');
                const agreement = q('[rel=login_agreement]');
                const check = q('[role=checkbox]');
                const code = q('#js-mvcode');
                document.body.dataset.submits = '0';
                q('#login-method').onclick = event => {{
                    event.preventDefault();
                    form.hidden = agreement.hidden = false;
                }};
                check.onclick = () => check.setAttribute('aria-checked', 'true');
                q('#js-submit').onclick = () => {{
                    document.body.dataset.submits = String(Number(document.body.dataset.submits) + 1);
                    document.body.dataset.valid = String(q('#js-account').value === 'wall-qa' &&
                        q('#js-passwd').value === 'fixture-only' &&
                        check.getAttribute('aria-checked') === 'true');
                    code.hidden = false;
                }};
                code.hidden = !{visible};
                </script>"""
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.end_headers()
                self.wfile.write(page.encode())
            elif path == "/query-page":
                self.send_response(200)
                self.send_header("Content-Type", "text/html")
                self.end_headers()
                self.wfile.write(b'<title>History fixture</title><div id="history-fixture">History page</div>')
            elif path == "/login-redirect":
                self.send_response(302)
                self.send_header("Location", f"http://127.0.0.1:{self.server.server_port}/signed-in")
                self.end_headers()
            elif path == "/signed-in":
                self.send_response(200)
                self.send_header("Content-Type", "text/html")
                self.send_header("Set-Cookie", "login_fixture=yes; Path=/; HttpOnly; Max-Age=600")
                self.send_header("Set-Cookie", "session_fixture=yes; Path=/; HttpOnly")
                self.end_headers()
                self.wfile.write(b'<div id="login-complete">OK</div>')
            elif path == "/verify-login":
                self.send_response(200)
                self.send_header("Content-Type", "text/html")
                self.end_headers()
                ok = "login_fixture=yes" in self.headers.get("Cookie", "")
                session = str("session_fixture=yes" in self.headers.get("Cookie", "")).lower()
                self.wfile.write(f'<div id="session-persisted" data-session="{session}">OK</div>'.encode()
                                 if ok else b'<div>Signed out</div>')
            elif path.startswith("/watch/"):
                shared = "yes" if "shared_profile=yes" in self.headers.get("Cookie", "") else "no"
                already_fullscreen = "true" if path.endswith("/2") else "false"
                page = f"""<!doctype html><title>Fixture video</title>
                <body data-shared-profile="{shared}" style="margin:0;background:#000">
                <script>
                document.body.dataset.documentId=crypto.randomUUID();
                document.body.dataset.fullscreenClicks='0';
                document.body.dataset.promoClosed='0';
                document.body.dataset.otherClosed='0';
                setTimeout(() => {{
                  const box=document.createElement('div');
                  box.id='js_common_mini-dialog';
                  box.innerHTML='<a href="#" rel="close">Close promotion</a>';
                  box.firstChild.onclick=(event) => {{
                    event.preventDefault();
                    document.body.dataset.promoClosed='1';
                    box.remove();
                  }};
                  document.body.appendChild(box);
                }}, 850);

                setTimeout(() => {{
                  const button=document.createElement('button');
                  button.setAttribute('rel','web_fullscreen');
                  const icon=document.createElement('i');
                  button.appendChild(icon);
                  const full={already_fullscreen};
                  if (full) icon.classList.add('iop-browser-exitfull');
                  button.onclick=() => {{
                    const enabled=icon.classList.toggle('iop-browser-exitfull');
                    document.body.dataset.webFullscreen=String(enabled);
                    document.body.dataset.fullscreenClicks=String(Number(document.body.dataset.fullscreenClicks)+1);
                  }};
                  document.body.dataset.webFullscreen=String(full);
                  document.body.appendChild(button);
                }}, 320);
                </script>
                <a rel="close" href="#" onclick="document.body.dataset.otherClosed='1'">Unrelated close button</a>
                <video muted autoplay loop controls playsinline style="width:100vw;height:100vh" src="/sample.mp4"></video>
                """
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.end_headers()
                self.wfile.write(page.encode())
            elif path == "/sample.mp4":
                requested = re.fullmatch(r"bytes=(\d+)-(\d*)", self.headers.get("Range", ""))
                start = int(requested[1]) if requested else 0
                end = min(len(sample) - 1, int(requested[2])) if requested and requested[2] else len(sample) - 1
                if start > end:
                    self.send_error(416)
                    return
                self.send_response(206 if requested else 200)
                self.send_header("Content-Type", "video/mp4")
                self.send_header("Accept-Ranges", "bytes")
                self.send_header("Content-Length", str(end - start + 1))
                if requested:
                    self.send_header("Content-Range", f"bytes {start}-{end}/{len(sample)}")
                self.end_headers()
                try:
                    self.wfile.write(sample[start:end + 1])
                except (BrokenPipeError, ConnectionResetError):
                    pass
            else:
                self.send_error(404)

        def do_POST(self):
            if self.path != "/web/lixian/?ct=lixian&ac=add_task_url":
                self.send_error(404)
                return
            link = self.rfile.read(int(self.headers.get("Content-Length", "0")))
            self.send_response(200 if link.startswith((b"magnet:?xt=urn:btih:", b"ed2k://|file|")) else 400)
            self.end_headers()

        def log_message(self, *args):
            pass

    return ThreadingHTTPServer(("127.0.0.1", 0), Fixture)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("mp4", type=Path, help="H.264 fixture longer than 12 seconds")
    args = parser.parse_args()
    directory = Path(gettempdir()) / "115VideoWall-tests"
    directory.mkdir(parents=True, exist_ok=True)
    evidence = directory / f"smoke-{os.getpid()}.json"
    with serve(args.mp4.read_bytes()) as http:
        thread = Thread(target=http.serve_forever, daemon=True)
        thread.start()
        try:
            process = subprocess.run(
                [str(args.exe.resolve()), "--smoke", f"http://127.0.0.1:{http.server_port}/", str(evidence)],
                timeout=130, check=False,
            )
            if not evidence.is_file():
                raise RuntimeError(f"The debug build exited without a result ({process.returncode})")
            result = json.loads(evidence.read_text(encoding="utf-8"))
            if process.returncode:
                print(json.dumps(result, indent=2))
                return process.returncode
            restart_evidence = evidence.with_name(f"restart-{os.getpid()}.json")
            restart = subprocess.run(
                [str(args.exe.resolve()), "--smoke-restart", f"http://127.0.0.1:{http.server_port}/",
                 result["profileData"], str(restart_evidence)], timeout=45, check=False,
            )
            result["restart"] = json.loads(restart_evidence.read_text(encoding="utf-8"))
            print(json.dumps(result, indent=2))
            return restart.returncode
        finally:
            http.shutdown()


if __name__ == "__main__":
    raise SystemExit(main())
