using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ImpiousBonum.Core.Remote;

/// <summary>One rendered picture of the dashboard. <see cref="Sequence"/> goes up each time the picture changes.</summary>
public sealed record TabletFrame(byte[] Image, string ContentType, long Sequence);

/// <summary>
/// The tablet view's web server: a page that shows the dashboard, and the latest <see cref="Frame"/> for it to fetch
/// once a second. A link can name a saved layout (<c>&amp;layout=</c>) to show that instead, with pictures of its own.
/// Read-only unless <see cref="Tap"/> is set (an opt-in), answers only local-network addresses that know
/// the link's secret, and closes every connection after one response. Written on <see cref="TcpListener"/> because HttpListener needs an admin-registered
/// URL reservation to accept anything but localhost.
/// </summary>
public sealed class TabletServer : IDisposable
{
    public const int DefaultPort = 8787;

    /// <summary>How long after a tablet's last frame request it still counts as watching.</summary>
    public static readonly TimeSpan WatchWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a layout a link names is kept after the last tablet showing it stops asking, so one that reloads or
    /// drops off the Wi-Fi for a moment doesn't start again from nothing. After that it's forgotten and costs nothing.
    /// </summary>
    public static readonly TimeSpan LayoutLifetime = TimeSpan.FromMinutes(3);

    /// <summary>How many layouts of their own tablets can show at once, besides the main dashboard: each is a copy of the dashboard in memory.</summary>
    public const int MaxLayouts = 4;

    private const int MaxConnections = 16;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private volatile string _token;
    private readonly SemaphoreSlim _connections = new(MaxConnections);
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeProvider _time;
    private readonly Feed _main = new();
    // The layouts links name, by their saved names.
    private readonly Dictionary<string, Feed> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _layoutsGate = new();
    private TcpListener? _listener;

    public TabletServer(int port, string token, TimeProvider? time = null)
    {
        Port = port;
        _token = token;
        _time = time ?? TimeProvider.System;
    }

    public int Port { get; }

    /// <summary>The link's secret. Changing it locks out tablets using the old link from their next request.</summary>
    public string Token
    {
        get => _token;
        set => _token = value;
    }

    /// <summary>
    /// Presses whatever is at a point of a layout's picture (0–1 across and down), returning whether there was something
    /// to press. The layout is the saved one a link names, or null for the main dashboard. Null, the default, means
    /// tablets can only look: <c>/tap</c> doesn't exist. Called on the thread pool.
    /// </summary>
    public Func<string?, double, double, bool>? Tap { get; set; }

    /// <summary>
    /// Finds a saved layout by the name a link gives it, returning the name as saved, or null if there's no such layout.
    /// Null, the default, means links can only show the main dashboard. Called on the thread pool.
    /// </summary>
    public Func<string, string?>? FindLayout { get; set; }

    /// <summary>The main dashboard's picture to serve. Set from any thread; read by requests on the thread pool.</summary>
    public TabletFrame? Frame
    {
        get => _main.Frame;
        set => _main.Frame = value;
    }

    /// <summary>A tablet has asked for the main dashboard within <see cref="WatchWindow"/>, so frames are worth rendering.</summary>
    public bool IsWatched => _main.AskedWithin(WatchWindow, _time);

    /// <summary>The saved layouts tablets have asked for by name, until each is forgotten (see <see cref="LayoutLifetime"/>).</summary>
    public IReadOnlyList<string> Layouts
    {
        get
        {
            lock (_layoutsGate)
                return [.. _layouts.Keys];
        }
    }

    /// <summary>A tablet has asked for the layout <paramref name="name"/> within <see cref="WatchWindow"/>, so frames of it are worth rendering.</summary>
    public bool IsLayoutWatched(string name) => FeedOf(name)?.AskedWithin(WatchWindow, _time) == true;

    /// <summary>The picture to serve for the layout <paramref name="name"/>. Ignored if it's been forgotten meanwhile.</summary>
    public void SetFrame(string name, TabletFrame frame)
    {
        if (FeedOf(name) is { } feed)
            feed.Frame = frame;
    }

    /// <summary>The picture being served for the layout <paramref name="name"/>, if any.</summary>
    public TabletFrame? FrameOf(string name) => FeedOf(name)?.Frame;

    /// <summary>Forgets the layouts no tablet has asked for within <see cref="LayoutLifetime"/>, and their pictures.</summary>
    public void ForgetIdleLayouts()
    {
        lock (_layoutsGate)
        {
            foreach (var name in _layouts.Where(l => !l.Value.AskedWithin(LayoutLifetime, _time)).Select(l => l.Key).ToList())
                _layouts.Remove(name);
        }
    }

    /// <summary>Starts listening on every address (refusing non-local ones per connection). Throws <see cref="SocketException"/> if the port is taken.</summary>
    public void Start()
    {
        TcpListener listener;
        if (Socket.OSSupportsIPv6)
        {
            listener = new TcpListener(IPAddress.IPv6Any, Port);
            listener.Server.DualMode = true;
        }
        else
        {
            listener = new TcpListener(IPAddress.Any, Port);
        }

        // Stop another app binding the same port with address reuse and answering in our place.
        if (OperatingSystem.IsWindows())
            listener.ExclusiveAddressUse = true;
        listener.Start();
        _listener = listener;
        _ = AcceptLoopAsync(listener, _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Stop();
        _listener = null;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                Trace.TraceWarning($"Tablet view: accept failed: {ex.Message}");
                continue;
            }

            if (!_connections.Wait(0))
            {
                client.Dispose();
                continue;
            }
            _ = ServeAsync(client, cancellationToken);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stopping)
    {
        try
        {
            using (client)
            {
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !TabletAccess.IsLocalNetwork(remote.Address))
                    return;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                timeout.CancelAfter(RequestTimeout);
                var stream = client.GetStream();
                var head = await HttpRequestHead.ReadAsync(stream, timeout.Token);
                await Respond(head).WriteToAsync(stream, timeout.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The tablet went away mid-request, or was too slow; nothing to tell anyone.
        }
        finally
        {
            _connections.Release();
        }
    }

    /// <summary>What to send back for a request (null for one that couldn't be read).</summary>
    public HttpResponse Respond(HttpRequestHead? request)
    {
        if (request is null)
            return HttpResponse.Text(400, "Bad Request");
        if (request.Method == "POST" && request.Path == "/tap")
            return RespondToTap(request);
        if (request.Method != "GET")
            return HttpResponse.Text(405, "Method Not Allowed");
        // Without the secret, nothing here exists.
        if (!TabletAccess.TokenMatches(request.Query.GetValueOrDefault("k"), _token))
            return HttpResponse.Text(404, "Not Found");

        switch (request.Path)
        {
            case "/":
                if (!TryLayout(request, out var named))
                    return HttpResponse.Html(404, TabletPage.NoSuchLayout(request.Query["layout"]), TabletPage.MessagePolicy);
                // Opening the page counts as watching, so the first picture is ready by the time the page asks for it.
                if (AskFor(named) is null)
                    return HttpResponse.Html(503, TabletPage.TooManyLayouts, TabletPage.MessagePolicy);
                return HttpResponse.Html(200, TabletPage.Html, TabletPage.ContentSecurityPolicy);

            case "/frame":
                // Renamed or deleted while a tablet shows it: the page reloads, and then says so.
                if (!TryLayout(request, out var layout))
                    return HttpResponse.Text(410, "Gone");
                if (AskFor(layout)?.Frame is not { } frame)
                    return HttpResponse.Text(503, "Starting") with { RetryAfterSeconds = 1 };
                var etag = $"\"{frame.Sequence}\"";
                if (request.Headers.GetValueOrDefault("If-None-Match") == etag)
                    return new HttpResponse(304, null, []) { ETag = etag };
                return new HttpResponse(200, frame.ContentType, frame.Image) { ETag = etag };

            default:
                return HttpResponse.Text(404, "Not Found");
        }
    }

    /// <summary>
    /// A tap from the page. Needs the secret like everything else, only exists while taps are allowed, and refuses a
    /// request another site's page made (its Origin isn't this server), so a web page can't press buttons through it.
    /// </summary>
    private HttpResponse RespondToTap(HttpRequestHead request)
    {
        if (!TabletAccess.TokenMatches(request.Query.GetValueOrDefault("k"), _token) || Tap is not { } tap)
            return HttpResponse.Text(404, "Not Found");
        if (request.Headers.GetValueOrDefault("Origin") is { } origin
            && (!Uri.TryCreate(origin, UriKind.Absolute, out var from) || !string.Equals(from.Authority, request.Headers.GetValueOrDefault("Host"), StringComparison.OrdinalIgnoreCase)))
            return HttpResponse.Text(403, "Forbidden");
        if (!TryLayout(request, out var layout))
            return HttpResponse.Text(410, "Gone");
        if (!TryFraction(request.Query.GetValueOrDefault("x"), out var x) || !TryFraction(request.Query.GetValueOrDefault("y"), out var y))
            return HttpResponse.Text(400, "Bad Request");
        return tap(layout, x, y) ? HttpResponse.Text(200, "Pressed") : new HttpResponse(204, null, []);
    }

    /// <summary>
    /// The saved layout a request's link names, as saved, or null for the main dashboard (a link without <c>layout</c>).
    /// False when it names one there's no layout of.
    /// </summary>
    private bool TryLayout(HttpRequestHead request, out string? layout)
    {
        layout = null;
        if (request.Query.GetValueOrDefault("layout") is not { Length: > 0 } name)
            return true;
        layout = FindLayout?.Invoke(name);
        return layout is not null;
    }

    /// <summary>
    /// Notes a tablet asking for a layout (null for the main dashboard) and returns what's served for it: made on the
    /// first ask, or null when tablets are already showing <see cref="MaxLayouts"/> others.
    /// </summary>
    private Feed? AskFor(string? layout)
    {
        var feed = _main;
        if (layout is not null)
        {
            lock (_layoutsGate)
            {
                if (!_layouts.TryGetValue(layout, out feed))
                {
                    if (_layouts.Count >= MaxLayouts)
                        return null;
                    _layouts[layout] = feed = new Feed();
                }
            }
        }
        feed.Asked(_time);
        return feed;
    }

    private Feed? FeedOf(string layout)
    {
        lock (_layoutsGate)
            return _layouts.GetValueOrDefault(layout);
    }

    private static bool TryFraction(string? text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 1;

    /// <summary>What's served for one layout: its latest picture, and when a tablet last asked for it.</summary>
    private sealed class Feed
    {
        private long _lastAsked;

        public TabletFrame? Frame { get; set; }

        public void Asked(TimeProvider time) => Interlocked.Exchange(ref _lastAsked, time.GetTimestamp());

        public bool AskedWithin(TimeSpan window, TimeProvider time)
        {
            var last = Interlocked.Read(ref _lastAsked);
            return last != 0 && time.GetElapsedTime(last) < window;
        }
    }
}

public sealed record HttpResponse(int Status, string? ContentType, byte[] Body)
{
    public string? ETag { get; init; }

    public string? ContentSecurityPolicy { get; init; }

    public int? RetryAfterSeconds { get; init; }

    public static HttpResponse Text(int status, string text) => new(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));

    public static HttpResponse Html(int status, string html, string contentSecurityPolicy) =>
        new(status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)) { ContentSecurityPolicy = contentSecurityPolicy };

    public string Head()
    {
        var head = new StringBuilder();
        head.Append($"HTTP/1.1 {Status} {Reason(Status)}\r\n");
        if (ContentType is not null)
            head.Append($"Content-Type: {ContentType}\r\n");
        head.Append($"Content-Length: {Body.Length}\r\n");
        head.Append("Cache-Control: no-store\r\n");
        head.Append("Connection: close\r\n");
        head.Append("X-Content-Type-Options: nosniff\r\n");
        // The secret is in the page's address; never pass it on.
        head.Append("Referrer-Policy: no-referrer\r\n");
        if (ETag is not null)
            head.Append($"ETag: {ETag}\r\n");
        if (ContentSecurityPolicy is not null)
            head.Append($"Content-Security-Policy: {ContentSecurityPolicy}\r\n");
        if (RetryAfterSeconds is { } seconds)
            head.Append($"Retry-After: {seconds}\r\n");
        head.Append("\r\n");
        return head.ToString();
    }

    public async Task WriteToAsync(Stream stream, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(Head()), cancellationToken);
        if (Body.Length > 0)
            await stream.WriteAsync(Body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        304 => "Not Modified",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        410 => "Gone",
        503 => "Service Unavailable",
        _ => "",
    };
}
