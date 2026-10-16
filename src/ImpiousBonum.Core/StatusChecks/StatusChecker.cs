using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace ImpiousBonum.Core.StatusChecks;

/// <summary>
/// Checks one address: a ping for a host name or IP address, or a request for a web address's page, which counts as up
/// when it answers 2xx or 3xx (and has the expected word, if there is one). Only the address typed is contacted: a page
/// that sends you on to another site isn't followed there.
/// </summary>
public static class StatusChecker
{
    /// <summary>How long a ping waits for its reply.</summary>
    public static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How long a web address has to answer, its page included.</summary>
    public static readonly TimeSpan WebTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Most moves to another page of the same site that are followed.</summary>
    public const int MostRedirects = 5;

    /// <summary>How much of a page is searched for the expected word.</summary>
    public const int MaxPageChars = 1024 * 1024;

    private static readonly HttpClient Http = CreateClient(anyCertificate: false);

    private static readonly Lazy<HttpClient> AnyCertificateHttp = new(() => CreateClient(anyCertificate: true));

    /// <summary>Checks <paramref name="check"/> once. Problems with the address come back as a down result saying why.</summary>
    public static Task<CheckResult> CheckAsync(StatusCheck check, CancellationToken cancellationToken) =>
        check.IsWeb
            ? GetAsync(check, (check.AnyCertificate ? AnyCertificateHttp.Value : Http).SendAsync, cancellationToken)
            : PingAsync(check.Address.Trim(), cancellationToken);

    /// <summary>A ping's reply, or why there wasn't one.</summary>
    internal static async Task<CheckResult> PingAsync(string host, CancellationToken cancellationToken)
    {
        // One Ping per check: a Ping can only send one at a time, and checks run side by side.
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(host, PingTimeout, cancellationToken: cancellationToken).ConfigureAwait(false);
            return reply.Status switch
            {
                IPStatus.Success => CheckResult.Answered(reply.RoundtripTime),
                IPStatus.DestinationHostUnreachable or IPStatus.DestinationNetworkUnreachable or IPStatus.DestinationUnreachable => CheckResult.Down("Can't be reached"),
                _ => CheckResult.Down("No answer"),
            };
        }
        catch (PingException ex)
        {
            return CheckResult.Down(ex.InnerException is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain }
                ? "Address not found"
                : $"Couldn't ping it: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    /// <summary>Asks for the page, following moves within the same site, and looks for the expected word.</summary>
    /// <param name="send">Sends a request; HTTP unless a test swaps it.</param>
    internal static async Task<CheckResult> GetAsync(
        StatusCheck check, Func<HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WebTimeout);
        var address = new Uri(check.Address.Trim());
        var clock = Stopwatch.StartNew();
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                // A new connection each time, so the time includes connecting, as it would for anyone else, and nothing is left open in between.
                request.Headers.ConnectionClose = true;
                using var response = await send(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                var milliseconds = Math.Round(clock.Elapsed.TotalMilliseconds);
                var code = (int)response.StatusCode;
                if (code is < 200 or >= 400)
                    return CheckResult.Down($"HTTP {code} {response.ReasonPhrase}".TrimEnd());

                if (code >= 300 && response.Headers.Location is { } location)
                {
                    var next = location.IsAbsoluteUri ? location : new Uri(address, location);
                    // Within the same site it's followed, so an expected word can be looked for on the page it leads to.
                    if (next.Scheme is "http" or "https" && next.Host.Equals(address.Host, StringComparison.OrdinalIgnoreCase) && redirects < MostRedirects)
                    {
                        address = next;
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(check.Expect))
                        return CheckResult.Down($"It sends you on to {next.Host}, which isn't checked");
                    return CheckResult.Answered(milliseconds);
                }

                if (!string.IsNullOrWhiteSpace(check.Expect))
                {
                    var page = await ReadLimitedAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    if (!page.Contains(check.Expect.Trim(), StringComparison.OrdinalIgnoreCase))
                        return CheckResult.Down($"The page doesn't say \"{check.Expect.Trim()}\"");
                }
                return CheckResult.Answered(milliseconds);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CheckResult.Down($"No answer within {WebTimeout.TotalSeconds:0} seconds");
        }
        catch (HttpRequestException ex)
        {
            return CheckResult.Down(ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "Address not found",
                HttpRequestError.SecureConnectionError when ex.InnerException is AuthenticationException && !check.AnyCertificate =>
                    "Windows doesn't trust its certificate. If it's your own server's, tick Accept its own certificate.",
                HttpRequestError.SecureConnectionError => "Couldn't make a secure connection",
                HttpRequestError.ConnectionError => "Couldn't connect",
                _ => $"Couldn't connect: {ex.Message}",
            });
        }
        catch (IOException ex)
        {
            return CheckResult.Down($"The connection broke: {ex.Message}");
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[8192];
        var text = new StringBuilder();
        int read;
        while (text.Length < MaxPageChars && (read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            text.Append(buffer, 0, Math.Min(read, MaxPageChars - text.Length));
        return text.ToString();
    }

    private static HttpClient CreateClient(bool anyCertificate)
    {
        var handler = new SocketsHttpHandler
        {
            // Moves are followed by hand, and only within the same site (see GetAsync).
            AllowAutoRedirect = false,
            UseCookies = false,
        };
        if (anyCertificate)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        return client;
    }
}
