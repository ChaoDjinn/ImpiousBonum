namespace ImpiousBonum.Core;

/// <summary>
/// Sends the web requests the app makes for you: an alert's (see <see cref="Alerts.AlertWebhook"/>) and a web button's.
/// Each runs off the calling thread and gives up after its timeout. What went wrong names only the server and its
/// answer, never the address's path or the request's headers, which can hold a token.
/// </summary>
public static class WebRequestSender
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>The address, if it's an absolute http or https one.</summary>
    public static Uri? Address(string? address) =>
        Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;

    /// <summary>Sends it: null when the server said it worked, or what went wrong, e.g. "homeassistant.local answered 401 Unauthorized".</summary>
    /// <param name="send">Sends the request; HTTP unless a test swaps it.</param>
    public static async Task<string?> SendAsync(HttpRequestMessage request, TimeSpan timeout,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null, CancellationToken cancellationToken = default)
    {
        var host = request.RequestUri!.Host;
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            // On a pool thread from the start: looking up a proxy can hold up the caller before the request is under way.
            using var response = await Task.Run(() => (send ?? Http.SendAsync)(request, timer.Token), timer.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? null : $"{host} answered {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ex is TaskCanceledException ? $"{host} took too long to answer" : $"couldn't reach {host}: {ex.Message}";
        }
    }

    private static HttpClient CreateClient()
    {
        // Each request has its own timeout.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        return client;
    }
}
