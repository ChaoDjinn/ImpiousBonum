namespace ImpiousBonum.Core.Remote;

/// <summary>
/// The page a tablet opens: the dashboard picture scaled to fit, fetched again every second. Taps on the picture are
/// sent to the PC, which presses a button there only if tablets have been allowed to (otherwise they're ignored).
/// </summary>
public static class TabletPage
{
    /// <summary>Nothing but this page's own inline script and style, and pictures from this server.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; img-src 'self' blob:; connect-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'";

    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
        <meta name="referrer" content="no-referrer">
        <meta name="apple-mobile-web-app-capable" content="yes">
        <meta name="mobile-web-app-capable" content="yes">
        <meta name="apple-mobile-web-app-status-bar-style" content="black">
        <meta name="apple-mobile-web-app-title" content="Impious Bonum">
        <meta name="theme-color" content="#000000">
        <title>Impious Bonum</title>
        <style>
          html, body { margin: 0; height: 100%; background: #000; overflow: hidden; }
          #frame { position: fixed; inset: 0; width: 100%; height: 100%; object-fit: contain; }
          #status { position: fixed; left: 0; right: 0; bottom: 16px; text-align: center; color: #999;
                    font: 15px system-ui, -apple-system, "Segoe UI", sans-serif; pointer-events: none; }
          #full { position: fixed; top: 12px; right: 12px; padding: 8px 14px; border: 1px solid #555; border-radius: 8px;
                  background: rgba(0, 0, 0, .6); color: #ccc; font: 14px system-ui, -apple-system, "Segoe UI", sans-serif;
                  transition: opacity .4s; }
          #full.hidden { opacity: 0; pointer-events: none; }
          .ripple { position: fixed; width: 60px; height: 60px; margin: -30px 0 0 -30px; border-radius: 50%;
                    background: rgba(255, 255, 255, .35); pointer-events: none; animation: ripple .45s ease-out forwards; }
          @keyframes ripple { from { transform: scale(.3); opacity: 1; } to { transform: scale(1.4); opacity: 0; } }
        </style>
        </head>
        <body>
        <img id="frame" alt="">
        <div id="status">Connecting…</div>
        <button id="full" type="button">Full screen</button>
        <script>
          const key = new URLSearchParams(location.search).get("k") || "";
          const frame = document.getElementById("frame");
          const status = document.getElementById("status");
          const full = document.getElementById("full");
          let etag = "";
          let shown = "";
          let failures = 0;

          async function tick() {
            try {
              const response = await fetch("frame?k=" + encodeURIComponent(key), {
                cache: "no-store",
                headers: etag ? { "If-None-Match": etag } : {},
              });
              if (response.status === 200) {
                const next = URL.createObjectURL(await response.blob());
                const previous = shown;
                frame.onload = () => { if (previous) URL.revokeObjectURL(previous); };
                frame.src = shown = next;
                etag = response.headers.get("ETag") || "";
                failures = 0;
              } else if (response.status === 304 || response.status === 503) {
                failures = 0;
              } else {
                failures++;
              }
            } catch {
              failures++;
            }
            status.textContent = failures >= 3 ? "Waiting for the PC…" : shown ? "" : "Connecting…";
            setTimeout(tick, 1000);
          }

          // Full screen where the browser allows it (not on iPhone, and not needed from the home screen).
          const root = document.documentElement;
          const enter = root.requestFullscreen || root.webkitRequestFullscreen;
          const inFullScreen = () => document.fullscreenElement || document.webkitFullscreenElement;
          const standalone = matchMedia("(display-mode: standalone)").matches || navigator.standalone;
          let hideTimer = 0;
          function showButton() {
            if (!enter || standalone || inFullScreen()) {
              full.classList.add("hidden");
              return;
            }
            full.classList.remove("hidden");
            clearTimeout(hideTimer);
            hideTimer = setTimeout(() => full.classList.add("hidden"), 4000);
          }
          full.addEventListener("click", () => enter.call(root));

          // Where a tap lands on the picture itself, as 0–1 across and down (the picture is letterboxed to fit).
          frame.addEventListener("click", async (e) => {
            const box = frame.getBoundingClientRect();
            if (!frame.naturalWidth) return;
            const scale = Math.min(box.width / frame.naturalWidth, box.height / frame.naturalHeight);
            const w = frame.naturalWidth * scale, h = frame.naturalHeight * scale;
            const x = (e.clientX - box.left - (box.width - w) / 2) / w;
            const y = (e.clientY - box.top - (box.height - h) / 2) / h;
            if (x < 0 || x > 1 || y < 0 || y > 1) return;
            try {
              const response = await fetch("tap?k=" + encodeURIComponent(key) + "&x=" + x.toFixed(4) + "&y=" + y.toFixed(4), { method: "POST", cache: "no-store" });
              if (response.status === 200) {
                const ripple = document.createElement("div");
                ripple.className = "ripple";
                ripple.style.left = e.clientX + "px";
                ripple.style.top = e.clientY + "px";
                document.body.appendChild(ripple);
                setTimeout(() => ripple.remove(), 500);
              }
            } catch { }
          });
          document.addEventListener("fullscreenchange", showButton);
          document.addEventListener("webkitfullscreenchange", showButton);
          document.addEventListener("pointerdown", showButton);

          showButton();
          tick();
        </script>
        </body>
        </html>
        """;
}
