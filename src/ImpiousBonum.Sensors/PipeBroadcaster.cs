using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Sensors;

/// <summary>
/// One-way named pipe server that pushes JSON lines to every connected dashboard.
/// Clients can only read: they can't send commands to this elevated process or create their own instances of the pipe.
/// </summary>
internal sealed class PipeBroadcaster : IAsyncDisposable
{
    private const int MaxClients = 8;
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

    // Serialises all writes so a new client's handshake can't interleave with a broadcast.
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly List<NamedPipeServerStream> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private Task? _acceptLoop;
    private string? _statusLine;
    private string? _catalogLine;

    public bool HasClients
    {
        get
        {
            lock (_clients)
                return _clients.Count > 0;
        }
    }

    public void Start() => _acceptLoop = Task.Run(() => AcceptLoopAsync(_stop.Token));

    public Task SetStatusAsync(SensorMessage status) => SetAndBroadcastAsync(SensorProtocol.Serialize(status), line => _statusLine = line);

    public Task SetCatalogAsync(SensorMessage catalog) => SetAndBroadcastAsync(SensorProtocol.Serialize(catalog), line => _catalogLine = line);

    public async Task BroadcastAsync(SensorMessage message)
    {
        var bytes = Encode(SensorProtocol.Serialize(message));
        await _writeGate.WaitAsync();
        try
        {
            await WriteToAllAsync(bytes);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task SetAndBroadcastAsync(string line, Action<string> remember)
    {
        await _writeGate.WaitAsync();
        try
        {
            remember(line);
            await WriteToAllAsync(Encode(line));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var first = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServer(first);
                first = false;
                await server.WaitForConnectionAsync(cancellationToken);

                await _writeGate.WaitAsync(cancellationToken);
                try
                {
                    if (_statusLine is not null)
                        await WriteAsync(server, Encode(_statusLine));
                    if (_catalogLine is not null)
                        await WriteAsync(server, Encode(_catalogLine));
                    lock (_clients)
                        _clients.Add(server);
                    Log.Info("Dashboard connected.");
                }
                finally
                {
                    _writeGate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                server?.Dispose();
                return;
            }
            catch (Exception ex) when (first && ex is UnauthorizedAccessException or IOException)
            {
                // FirstPipeInstance failed: something else already owns our pipe name. Refuse to share it.
                Log.Error($"Couldn't create pipe {SensorProtocol.PipeName} (already in use?): {ex.Message}");
                return;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                // Client vanished or stalled during the handshake, or every instance is busy. Back off briefly.
                server?.Dispose();
                try
                {
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static NamedPipeServerStream CreateServer(bool first)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        // Whoever runs the host (e.g. a developer running it from a console) must be able to create further instances.
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        // Everyone else may only read. Deliberately no Write: it includes CreateNewInstance, which would let others impersonate this server.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.Read | PipeAccessRights.WriteAttributes | PipeAccessRights.Synchronize,
            AccessControlType.Allow));

        var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(SensorProtocol.PipeName, PipeDirection.Out, MaxClients, PipeTransmissionMode.Byte, options, 0, 64 * 1024, security);
    }

    private async Task WriteToAllAsync(byte[] bytes)
    {
        NamedPipeServerStream[] clients;
        lock (_clients)
            clients = [.. _clients];

        foreach (var client in clients)
        {
            try
            {
                await WriteAsync(client, bytes);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Disconnected, or stopped reading. Drop it; it will reconnect if it's still alive.
                lock (_clients)
                    _clients.Remove(client);
                await client.DisposeAsync();
                Log.Info("Dashboard disconnected.");
            }
        }
    }

    private static async Task WriteAsync(NamedPipeServerStream stream, byte[] bytes)
    {
        using var timeout = new CancellationTokenSource(WriteTimeout);
        await stream.WriteAsync(bytes, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }

    private static byte[] Encode(string line) => Encoding.UTF8.GetBytes(line + "\n");

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_acceptLoop is not null)
            await _acceptLoop;

        NamedPipeServerStream[] clients;
        lock (_clients)
        {
            clients = [.. _clients];
            _clients.Clear();
        }
        foreach (var client in clients)
            await client.DisposeAsync();

        _stop.Dispose();
        _writeGate.Dispose();
    }
}
