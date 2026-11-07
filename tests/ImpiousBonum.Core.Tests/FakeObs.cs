using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using ImpiousBonum.Core.Obs;

namespace ImpiousBonum.Core.Tests;

/// <summary>
/// A stand-in for OBS's WebSocket server on this PC: says hello (asking for <see cref="Password"/> when it has one),
/// turns a wrong one away as OBS does, and answers request batches with <see cref="Answer"/>.
/// </summary>
internal sealed class FakeObs : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<WebSocket> _sockets = [];
    private readonly Task _accepting;

    public FakeObs(string? password = null)
    {
        Password = password;
        var port = FreePort();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        Address = new Uri($"ws://localhost:{port}/");
        _accepting = Task.Run(AcceptAsync);
    }

    public Uri Address { get; }

    public string? Password { get; }

    /// <summary>OBS's answer to each request: whether it worked, and its data. By default, every request works with no data.</summary>
    public Func<string, JsonObject?, (bool Ok, JsonObject? Data)> Answer { get; set; } = (_, _) => (true, null);

    /// <summary>Every request received, by type, with its data.</summary>
    public List<(string Type, JsonObject? Data)> Requests { get; } = [];

    public int Connections { get; private set; }

    public ObsRequest Request(string? password) => new(Address, password);

    /// <summary>Closes every connection, as OBS does when it quits.</summary>
    public async Task DropAsync()
    {
        List<WebSocket> sockets;
        lock (_sockets)
        {
            sockets = [.. _sockets];
            _sockets.Clear();
        }
        foreach (var socket in sockets)
        {
            try
            {
                // Without waiting for the goodbye back, which a client that's gone never sends.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "OBS is closing", timeout.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }
            var socket = (await context.AcceptWebSocketAsync("obsws.json")).WebSocket;
            lock (_sockets)
                _sockets.Add(socket);
            Connections++;
            _ = Task.Run(() => ServeAsync(socket));
        }
    }

    private async Task ServeAsync(WebSocket socket)
    {
        try
        {
            const string Salt = "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", Challenge = "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=";
            var hello = new JsonObject { ["obsWebSocketVersion"] = "5.5.0", ["rpcVersion"] = 1 };
            if (Password is not null)
                hello["authentication"] = new JsonObject { ["challenge"] = Challenge, ["salt"] = Salt };
            await SendAsync(socket, new JsonObject { ["op"] = 0, ["d"] = hello });

            var identify = await ReceiveAsync(socket);
            if (Password is not null && (string?)identify?["d"]?["authentication"] != ObsConnection.Authentication(Password, Salt, Challenge))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync((WebSocketCloseStatus)4009, "Authentication failed.", timeout.Token);
                return;
            }
            await SendAsync(socket, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } });

            while (await ReceiveAsync(socket) is { } message)
            {
                if ((int?)message["op"] != 8 || message["d"] is not JsonObject batch)
                    continue;
                var results = new JsonArray();
                foreach (var request in batch["requests"]!.AsArray().OfType<JsonObject>())
                {
                    var type = (string)request["requestType"]!;
                    var data = request["requestData"] as JsonObject;
                    lock (Requests)
                        Requests.Add((type, data));
                    var (ok, answer) = Answer(type, data);
                    var result = new JsonObject
                    {
                        ["requestType"] = type,
                        ["requestStatus"] = new JsonObject { ["result"] = ok, ["code"] = ok ? 100 : 600 },
                    };
                    if (answer is not null)
                        result["responseData"] = answer.DeepClone();
                    results.Add(result);
                }
                await SendAsync(socket, new JsonObject { ["op"] = 9, ["d"] = new JsonObject { ["requestId"] = batch["requestId"]!.DeepClone(), ["results"] = results } });
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static Task SendAsync(WebSocket socket, JsonObject message) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

    private async Task<JsonObject?> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        var result = await socket.ReceiveAsync(buffer, _stop.Token);
        return result.MessageType == WebSocketMessageType.Close ? null : JsonNode.Parse(buffer.AsSpan(0, result.Count)) as JsonObject;
    }

    public async ValueTask DisposeAsync()
    {
        await DropAsync();
        _stop.Cancel();
        _listener.Close();
        try
        {
            await _accepting.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or TimeoutException)
        {
        }
    }
}
