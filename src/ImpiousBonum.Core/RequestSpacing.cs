namespace ImpiousBonum.Core;

/// <summary>
/// Keeps requests to a service at least <paramref name="every"/> apart, however quickly they're asked for, for services
/// that set a limit (OpenStreetMap's place search allows one a second).
/// </summary>
internal sealed class RequestSpacing(TimeSpan every, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private DateTimeOffset _next = DateTimeOffset.MinValue;

    /// <summary>Waits for the next free turn. A cancelled wait still uses its turn, which only errs on the side of waiting.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        var wait = Reserve();
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Takes the next free turn and says how long until it.</summary>
    internal TimeSpan Reserve()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var turn = _next > now ? _next : now;
            _next = turn + every;
            return turn - now;
        }
    }
}
