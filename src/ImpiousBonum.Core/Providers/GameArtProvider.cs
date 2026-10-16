using System.Globalization;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The game you're playing: its name, its Steam app id, and its pictures as an attachment (a <see cref="GameArt"/>) for
/// the image widget. A game is an app in front that Steam installed, or one linked to a layout or theme on the Game
/// layouts page. Steam's pictures come from its own cache on this PC; nothing is fetched. Nothing has a reading while
/// no game is in front, so widgets that hide without a reading disappear.
/// </summary>
/// <param name="appInFront">The app in front, as the FPS reading follows it; the foreground app unless the dashboard says otherwise.</param>
/// <param name="linkedGame">The name of the app in front when it's linked on the Game layouts page, else null.</param>
/// <param name="files">Steam's files and the game's exe; the real ones unless a test swaps them.</param>
/// <param name="exePath">The exe of a process, by its id; Windows' unless a test swaps it.</param>
public sealed class GameArtProvider(
    Func<(int ProcessId, string? Name)>? appInFront = null, Func<string?>? linkedGame = null,
    IGameFiles? files = null, Func<int, string?>? exePath = null, TimeProvider? time = null) : IMetricProvider
{
    public const string Name = "game.name";
    public const string AppId = "game.appid";

    /// <summary>The game's pictures (a <see cref="GameArt"/>), as a <see cref="MetricStore"/> attachment.</summary>
    public const string Art = "game.art";

    /// <summary>How long a game stays after it leaves the front, so a quick alt-tab doesn't blank its picture.</summary>
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(3);

    private readonly Func<(int ProcessId, string? Name)> _appInFront = appInFront ?? ForegroundApp.Get;
    private readonly Func<string?> _linkedGame = linkedGame ?? (() => null);
    private readonly Func<int, string?> _exePath = exePath ?? ExeFile.PathOf;
    private readonly GameFinder _finder = new(files ?? new GameFiles(), time);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private int _processId;
    private bool _linked;
    private Game? _inFront;
    private Game? _shown;
    private DateTimeOffset _lastSeen;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Name, "Game: name", "Game", MetricUnit.Text));
        store.Register(new MetricDefinition(AppId, "Game: Steam app id", "Game", MetricUnit.Text));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        // Only a different app in front, or one linked or unlinked, means looking anything up.
        var (processId, name) = _appInFront();
        var linked = name is not null && string.Equals(_linkedGame(), name, StringComparison.OrdinalIgnoreCase);
        if (processId != _processId || linked != _linked)
        {
            _processId = processId;
            _linked = linked;
            _inFront = processId != 0 && _exePath(processId) is { } exe ? _finder.Find(exe, anyApp: linked) : null;
        }

        var now = _time.GetUtcNow();
        if (_inFront is not null || _shown is null)
        {
            _shown = _inFront;
            _lastSeen = now;
        }
        else if (now - _lastSeen >= Linger)
        {
            _shown = null;
        }
        Publish(store, _shown);
        return ValueTask.CompletedTask;
    }

    /// <summary>Writes the game's readings and pictures, or clears them for none.</summary>
    internal static void Publish(MetricStore store, Game? game)
    {
        store.SetText(Name, game?.Name);
        store.SetText(AppId, game?.AppId?.ToString(CultureInfo.InvariantCulture));
        store.SetAttachment(Art, game?.Art);
    }

    public void Dispose()
    {
    }
}
