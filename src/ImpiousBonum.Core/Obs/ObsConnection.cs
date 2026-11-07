using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ImpiousBonum.Core.Obs;

/// <summary>Where OBS's WebSocket server is, and its password (null when it has none).</summary>
public sealed record ObsRequest(Uri Address, string? Password)
{
    public const int DefaultPort = 4455;

    /// <summary>
    /// The address typed in settings: <c>localhost</c>, <c>192.168.1.20:4455</c> or <c>ws://streampc:4455</c>; empty is
    /// this PC. Null when it isn't one.
    /// </summary>
    public static Uri? AddressOf(string? typed)
    {
        var text = string.IsNullOrWhiteSpace(typed) ? "localhost" : typed.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "ws://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss") || uri.Host.Length == 0)
            return null;
        // Without a port, OBS's own rather than the web's 80. A port comes after the host, and after an IPv6 address's ].
        var authority = text[(text.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/')[0];
        return authority.LastIndexOf(':') > authority.LastIndexOf(']') ? uri : new UriBuilder(uri) { Port = DefaultPort }.Uri;
    }

    /// <summary>Whether OBS is on this PC, so its program can be looked for before trying to connect.</summary>
    public bool IsLocal => Address.IsLoopback || Address.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
}

/// <summary>How the connection to OBS is going.</summary>
public enum ObsState
{
    /// <summary>Not set up in settings.</summary>
    Off,

    /// <summary>OBS isn't running on this PC.</summary>
    NotRunning,

    Connected,

    /// <summary>OBS turned the password away, or wants one and none is set.</summary>
    WrongPassword,

    /// <summary>OBS couldn't be reached: its WebSocket server is off, or the address is wrong.</summary>
    Unreachable,
}

/// <summary>One request to OBS, such as <c>ToggleRecord</c>, with its data.</summary>
public sealed record ObsCall(string Type, JsonObject? Data = null);

/// <summary>OBS's answer to an <see cref="ObsCall"/>: whether it did it, and what it sent back.</summary>
public sealed record ObsResult(bool Ok, JsonObject? Data);

/// <summary>
/// A connection to OBS Studio's WebSocket server (obs-websocket 5, built into OBS since version 28), shared by the OBS
/// readings and buttons. It connects when first asked and again after OBS closes, at most every few seconds, and only
/// while OBS is running when it's on this PC, so it costs nothing while OBS is closed. It asks for no events: the
/// readings ask what they want each second. Thread-safe.
/// </summary>
public sealed class ObsConnection : IAsyncDisposable
{
    /// <summary>How long to wait before trying again after OBS couldn't be reached or turned the password away.</summary>
    internal static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    /// <summary>How long a connection or a request may take.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private const int OpHello = 0, OpIdentify = 1, OpIdentified = 2, OpRequestResponse = 7, OpRequestBatch = 8, OpRequestBatchResponse = 9;

    /// <summary>The close code OBS uses when the password is wrong.</summary>
    private const int AuthenticationFailed = 4009;

    private readonly Func<ObsRequest?> _settings;
    private readonly Func<bool> _obsRunning;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> _pending = [];
    private readonly Lock _pendingGate = new();
    private ClientWebSocket? _socket;
    private ObsRequest? _connectedTo;
    private DateTimeOffset _triedAt = DateTimeOffset.MinValue;
    private long _nextId;
    private volatile ObsState _state;

    /// <param name="settings">Where OBS is; null while it's off in settings.</param>
    /// <param name="obsRunning">Whether OBS is running on this PC; looks for its program unless a test swaps it.</param>
    /// <param name="time">The clock; the system's unless a test swaps it.</param>
    public ObsConnection(Func<ObsRequest?> settings, Func<bool>? obsRunning = null, TimeProvider? time = null)
    {
        _settings = settings;
        _obsRunning = obsRunning ?? ObsIsRunning;
        _time = time ?? TimeProvider.System;
    }

    public ObsState State => _state;

    /// <summary>
    /// Sends <paramref name="calls"/> in one go, connecting first if need be, and returns OBS's answers in the same
    /// order; null when OBS can't be reached (see <see cref="State"/>).
    /// </summary>
    public async Task<IReadOnlyList<ObsResult>?> SendAsync(IReadOnlyList<ObsCall> calls, CancellationToken cancellationToken = default)
    {
        if (calls.Count == 0 || await ConnectedAsync(cancellationToken).ConfigureAwait(false) is not { } socket)
            return null;
        var id = Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var requests = new JsonArray();
        foreach (var call in calls)
        {
            var request = new JsonObject { ["requestType"] = call.Type };
            if (call.Data is not null)
                request["requestData"] = call.Data.DeepClone();
            requests.Add(request);
        }
        var message = new JsonObject
        {
            ["op"] = OpRequestBatch,
            ["d"] = new JsonObject { ["requestId"] = id, ["haltOnFailure"] = false, ["requests"] = requests },
        };

        var answer = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingGate)
            _pending[id] = answer;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            await SendTextAsync(socket, message.ToJsonString(), timeout.Token).ConfigureAwait(false);
            var response = await answer.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            return Results(response, calls.Count);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            if (!cancellationToken.IsCancellationRequested)
                Trace.TraceInformation($"OBS didn't answer: {ex.Message}");
            return null;
        }
        finally
        {
            lock (_pendingGate)
                _pending.Remove(id);
        }
    }

    /// <summary>Sends one request; true when OBS did it.</summary>
    public async Task<bool> SendAsync(string type, JsonObject? data = null, CancellationToken cancellationToken = default) =>
        await SendAsync([new ObsCall(type, data)], cancellationToken).ConfigureAwait(false) is [{ Ok: true }];

    /// <summary>The answers of a batch, in the order asked; one OBS left out counts as not done.</summary>
    private static List<ObsResult> Results(JsonObject response, int count)
    {
        var results = response["results"] as JsonArray ?? [];
        var answers = new List<ObsResult>(count);
        for (var i = 0; i < count; i++)
        {
            var result = i < results.Count ? results[i] as JsonObject : null;
            var ok = result?["requestStatus"]?["result"]?.GetValueKind() == System.Text.Json.JsonValueKind.True;
            answers.Add(new ObsResult(ok, result?["responseData"] as JsonObject));
        }
        return answers;
    }

    /// <summary>The socket to OBS, connecting if it's time to try; null when it can't be had now.</summary>
    private async Task<ClientWebSocket?> ConnectedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = _settings();
            if (settings != _connectedTo || _socket is { State: not WebSocketState.Open })
                Close();
            if (settings is null)
            {
                _state = ObsState.Off;
                _triedAt = DateTimeOffset.MinValue;
                return null;
            }
            if (_socket is not null)
                return _socket;
            var now = _time.GetUtcNow();
            if (now - _triedAt < RetryAfter)
                return null;
            _triedAt = now;
            if (settings.IsLocal && !_obsRunning())
            {
                _state = ObsState.NotRunning;
                return null;
            }

            var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("obsws.json");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Timeout);
                await socket.ConnectAsync(settings.Address, timeout.Token).ConfigureAwait(false);
                var hello = await ReceiveAsync(socket, timeout.Token).ConfigureAwait(false);
                if (hello?["op"]?.GetValue<int>() != OpHello)
                    throw new WebSocketException("OBS didn't say hello");
                var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
                if (hello["d"]?["authentication"] is JsonObject auth)
                {
                    if (string.IsNullOrEmpty(settings.Password))
                    {
                        _state = ObsState.WrongPassword;
                        socket.Dispose();
                        return null;
                    }
                    identify["authentication"] = Authentication(settings.Password, (string?)auth["salt"] ?? "", (string?)auth["challenge"] ?? "");
                }
                await SendTextAsync(socket, new JsonObject { ["op"] = OpIdentify, ["d"] = identify }.ToJsonString(), timeout.Token).ConfigureAwait(false);
                var identified = await ReceiveAsync(socket, timeout.Token).ConfigureAwait(false);
                if (identified?["op"]?.GetValue<int>() != OpIdentified)
                {
                    _state = socket.CloseStatus == (WebSocketCloseStatus)AuthenticationFailed ? ObsState.WrongPassword : ObsState.Unreachable;
                    socket.Dispose();
                    return null;
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                _state = socket.CloseStatus == (WebSocketCloseStatus)AuthenticationFailed ? ObsState.WrongPassword : ObsState.Unreachable;
                socket.Dispose();
                return null;
            }

            _socket = socket;
            _connectedTo = settings;
            _state = ObsState.Connected;
            _ = Task.Run(() => ListenAsync(socket));
            return socket;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// obs-websocket's password check: the password and OBS's salt hashed (SHA-256, then base 64), then that and OBS's
    /// challenge hashed the same way.
    /// </summary>
    internal static string Authentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    /// <summary>Hands OBS's answers to whoever's waiting for them, until the connection closes.</summary>
    private async Task ListenAsync(ClientWebSocket socket)
    {
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                if (await ReceiveAsync(socket, CancellationToken.None).ConfigureAwait(false) is not { } message)
                    break;
                var op = message["op"]?.GetValue<int>();
                if (op is not (OpRequestResponse or OpRequestBatchResponse) || message["d"] is not JsonObject data || (string?)data["requestId"] is not { } id)
                    continue;
                // A single request's answer looks like a batch of one.
                var answer = op == OpRequestBatchResponse ? data : new JsonObject { ["results"] = new JsonArray(data.DeepClone()) };
                TaskCompletionSource<JsonObject>? waiting;
                lock (_pendingGate)
                    _pending.TryGetValue(id, out waiting);
                waiting?.TrySetResult(answer);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or System.Text.Json.JsonException or ObjectDisposedException or InvalidOperationException or FormatException)
        {
            Trace.TraceInformation($"OBS connection closed: {ex.Message}");
        }
        // OBS is closing the connection: say goodbye back, as it waits for that.
        if (socket.State == WebSocketState.CloseReceived)
        {
            try
            {
                using var timeout = new CancellationTokenSource(Timeout);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
        // OBS closed, or the app did: the next request connects again.
        if (_state == ObsState.Connected && ReferenceEquals(_socket, socket))
            _state = ObsState.Unreachable;
    }

    private async Task SendTextAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken)
    {
        await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sending.Release();
        }
    }

    /// <summary>One whole message from OBS, or null once it closes.</summary>
    private static async Task<JsonObject?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }
        return JsonNode.Parse(message.ToArray()) as JsonObject;
    }

    private void Close()
    {
        var socket = _socket;
        _socket = null;
        _connectedTo = null;
        if (socket is null)
            return;
        socket.Abort();
        socket.Dispose();
        lock (_pendingGate)
        {
            foreach (var waiting in _pending.Values)
                waiting.TrySetCanceled();
        }
    }

    /// <summary>Whether OBS Studio's program is running (obs64, or obs32 on old 32-bit installs).</summary>
    private static bool ObsIsRunning()
    {
        foreach (var name in new[] { "obs64", "obs32", "obs" })
        {
            var processes = Process.GetProcessesByName(name);
            var found = processes.Length > 0;
            foreach (var process in processes)
                process.Dispose();
            if (found)
                return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Close();
            _state = ObsState.Off;
        }
        finally
        {
            _gate.Release();
        }
    }
}
