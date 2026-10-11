using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ImpiousBonum.Core.Remote;

/// <summary>One rendered picture of the dashboard. <see cref="Sequence"/> goes up each time the picture changes.</summary>
public sealed record TabletFrame(byte[] Image, string ContentType, long Sequence);

/// <summary>
/// The tablet view's web server: a page that shows the dashboard, and the latest <see cref="Frame"/> for it to fetch
/// once a second. Read-only unless <see cref="Tap"/> is set (an opt-in), answers only local-network addresses that know
/// the link's secret, and closes every connection after one response. Written on <see cref="TcpListener"/> because HttpListener needs an admin-registered
/// URL reservation to accept anything but localhost.
/// </summary>
public sealed class TabletServer : IDisposable
{
    public const int DefaultPort = 8787;

    /// <summary>How long after a tablet's last frame request it still counts as watching.</summary>
    public static readonly TimeSpan WatchWindow = TimeSpan.FromSeconds(5);

    private const int MaxConnections = 16;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private volatile string _token;
    private readonly SemaphoreSlim _connections = new(MaxConnections);
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private long _lastWatchedTicks;

    public TabletServer(int port, string token)
    {
        Port = port;
        _token = token;
    }

    public int Port { get; }

    /// <summary>The link's secret. Changing it locks out tablets using the old link from their next request.</summary>
    public string Token
    {
        get => _token;
        set => _token = value;
    }

    /// <summary>
    /// Presses whatever is at a point of the picture (0–1 across and down), returning whether there was something to
    /// press. Null, the default, means tablets can only look: <c>/tap</c> doesn't exist. Called on the thread pool.
    /// </summary>
    public Func<double, double, bool>? Tap { get; set; }

    /// <summary>The picture to serve. Set from any thread; read by requests on the thread pool.</summary>
    public TabletFrame? Frame { get; set; }

    /// <summary>A tablet has asked for a frame within <see cref="WatchWindow"/>, so frames are worth rendering.</summary>
    public bool IsWatched
    {
        get
        {
            var last = Interlocked.Read(ref _lastWatchedTicks);
            return last != 0 && Stopwatch.GetElapsedTime(last) < WatchWindow;
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
                return new HttpResponse(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(TabletPage.Html))
                {
                    ContentSecurityPolicy = TabletPage.ContentSecurityPolicy,
                };

            case "/frame":
                Interlocked.Exchange(ref _lastWatchedTicks, Stopwatch.GetTimestamp());
                if (Frame is not { } frame)
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
        if (!TryFraction(request.Query.GetValueOrDefault("x"), out var x) || !TryFraction(request.Query.GetValueOrDefault("y"), out var y))
            return HttpResponse.Text(400, "Bad Request");
        return tap(x, y) ? HttpResponse.Text(200, "Pressed") : new HttpResponse(204, null, []);
    }

    private static bool TryFraction(string? text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 1;
}

public sealed record HttpResponse(int Status, string? ContentType, byte[] Body)
{
    public string? ETag { get; init; }

    public string? ContentSecurityPolicy { get; init; }

    public int? RetryAfterSeconds { get; init; }

    public static HttpResponse Text(int status, string text) => new(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));

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
        503 => "Service Unavailable",
        _ => "",
    };
}
