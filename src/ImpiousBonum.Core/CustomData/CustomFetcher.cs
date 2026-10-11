using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ImpiousBonum.Core.CustomData;

/// <summary>A source couldn't be read; the message is for the user, e.g. "HTTP 401 Unauthorized".</summary>
public sealed class CustomSourceException(string message) : Exception(message);

/// <summary>Gets a source's output: a web request, or a command run without a window. Both give up after <see cref="Timeout"/>.</summary>
public static class CustomFetcher
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>More than this is cut off: these are meant to be small status documents.</summary>
    public const int MaxOutputChars = 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Throws <see cref="CustomSourceException"/> with a reason the user can act on.</summary>
    public static async Task<string> FetchAsync(CustomSource source, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            return source.IsWeb ? await GetAsync(source, timeout.Token).ConfigureAwait(false) : await RunAsync(source, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CustomSourceException($"No answer within {Timeout.TotalSeconds:0} seconds");
        }
        catch (HttpRequestException ex)
        {
            throw new CustomSourceException(ex.HttpRequestError == HttpRequestError.NameResolutionError ? "Address not found" : $"Couldn't connect: {ex.Message}");
        }
    }

    private static async Task<string> GetAsync(CustomSource source, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
        foreach (var (name, value) in source.Headers ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(name, value);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new CustomSourceException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await ReadLimitedAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> RunAsync(CustomSource source, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(source.Command!), Environment.ExpandEnvironmentVariables(source.Arguments ?? string.Empty))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new CustomSourceException($"Couldn't start {source.Command}");
        }
        catch (Win32Exception ex)
        {
            throw new CustomSourceException($"Couldn't start {source.Command}: {ex.Message}");
        }

        using (process)
        {
            try
            {
                var output = ReadLimitedAsync(process.StandardOutput, cancellationToken);
                var errors = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var text = await output.ConfigureAwait(false);
                if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(text))
                {
                    var firstError = (await errors.ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                    throw new CustomSourceException($"Exited with code {process.ExitCode}{(firstError is null ? "" : $": {firstError}")}");
                }
                return text;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Already gone.
                }
                throw;
            }
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        var text = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            text.Append(buffer, 0, Math.Min(read, MaxOutputChars - text.Length));
            if (text.Length >= MaxOutputChars)
                break;
        }
        return text.ToString();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        return client;
    }
}
