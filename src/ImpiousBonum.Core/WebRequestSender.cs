namespace ImpiousBonum.Core;

/// <summary>
/// Sends the web requests the app makes for you: an alert's (see <see cref="Alerts.AlertWebhook"/>) and a web button's.
/// Each runs off the calling thread and gives up after its timeout; one to this PC itself skips the system's proxy. What
/// went wrong names only the server and its answer, never the address's path or the request's headers, which can hold a token.
/// </summary>
public static class WebRequestSender
{
    private static readonly HttpClient Http = CreateClient(new HttpClientHandler());

    // For this PC's own addresses: no proxy could reach them, and asking Windows whether to use one can have it look for
    // a proxy script on the network first (it's set to by default), a step whose time is out of the app's hands.
    private static readonly HttpClient Direct = CreateClient(new HttpClientHandler { UseProxy = false });

    /// <summary>The address, if it's an absolute http or https one.</summary>
    public static Uri? Address(string? address) =>
        Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;

    /// <summary>Whether a request to <paramref name="address"/> skips the system's proxy: it does for this PC itself.</summary>
    internal static bool GoesDirect(Uri address) => address.IsLoopback;

    /// <summary>Sends it: null when the server said it worked, or what went wrong, e.g. "homeassistant.local answered 401 Unauthorized".</summary>
    /// <param name="send">Sends the request; HTTP unless a test swaps it.</param>
    public static async Task<string?> SendAsync(HttpRequestMessage request, TimeSpan timeout,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null, CancellationToken cancellationToken = default)
    {
        var host = request.RequestUri!.Host;
        send ??= (GoesDirect(request.RequestUri) ? Direct : Http).SendAsync;
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            // On a pool thread from the start: looking up a proxy can hold up the caller before the request is under way.
            using var response = await Task.Run(() => send(request, timer.Token), timer.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? null : $"{host} answered {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ex is TaskCanceledException ? $"{host} took too long to answer" : $"couldn't reach {host}: {ex.Message}";
        }
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        // Each request has its own timeout.
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        return client;
    }
}
