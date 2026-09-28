using System.Text;

namespace ImpiousBonum.Core.Remote;

/// <summary>The request line and headers of an HTTP/1.x request: all the tablet view ever needs to read.</summary>
public sealed class HttpRequestHead
{
    /// <summary>Requests with a bigger head than this are refused rather than buffered.</summary>
    public const int MaxLength = 8 * 1024;

    private HttpRequestHead(string method, string path, IReadOnlyDictionary<string, string> query, IReadOnlyDictionary<string, string> headers)
    {
        Method = method;
        Path = path;
        Query = query;
        Headers = headers;
    }

    public string Method { get; }

    /// <summary>The path without its query string, e.g. <c>/frame</c>.</summary>
    public string Path { get; }

    public IReadOnlyDictionary<string, string> Query { get; }

    /// <summary>Header names are case-insensitive.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Parses everything before the blank line that ends the head. Returns null for anything malformed.</summary>
    public static HttpRequestHead? Parse(string head)
    {
        var lines = head.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal)
            || requestLine[0].Length == 0 || !requestLine[1].StartsWith('/'))
            return null;

        var target = requestLine[1];
        var questionMark = target.IndexOf('?');
        var path = questionMark < 0 ? target : target[..questionMark];
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (questionMark >= 0)
        {
            foreach (var pair in target[(questionMark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                var name = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
                var value = equals < 0 ? "" : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
                query.TryAdd(name, value);
            }
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
                continue;
            var colon = line.IndexOf(':');
            if (colon <= 0)
                return null;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        return new HttpRequestHead(requestLine[0], Uri.UnescapeDataString(path), query, headers);
    }

    /// <summary>
    /// Reads up to the blank line that ends the head. Returns null if the connection closes first or the head is
    /// longer than <see cref="MaxLength"/>. A request body, which a GET doesn't have, is left unread.
    /// </summary>
    public static async Task<HttpRequestHead?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxLength];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
                return null;
            length += read;

            var end = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
            if (end >= 0)
                return Parse(Encoding.ASCII.GetString(buffer, 0, end));
        }
        return null;
    }
}
