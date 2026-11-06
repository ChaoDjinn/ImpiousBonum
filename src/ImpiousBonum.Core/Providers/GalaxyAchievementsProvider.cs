using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Steam;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The GOG game in front's achievements (<see cref="GameAchievements"/>), from GOG Galaxy's own record on the PC: how many
/// are unlocked, as Galaxy last synced them, read when a GOG game comes to the front and again when Galaxy's record
/// changes, at most every <see cref="ReadEvery"/>. No key, and nothing fetched. The readings are written only while a GOG
/// game is in front, and cleared once when it goes, as other launchers' games have their own.
/// </summary>
/// <param name="files">Galaxy's record; the real one unless a test swaps it.</param>
public sealed class GalaxyAchievementsProvider(IGameFiles? files = null, TimeProvider? time = null) : IMetricProvider
{
    /// <summary>How often Galaxy's record is read again at most while the same game is in front.</summary>
    public static readonly TimeSpan ReadEvery = TimeSpan.FromMinutes(1);

    private readonly IGameFiles _files = files ?? new GameFiles();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string? _key;
    private AchievementCount? _achievements;
    private DateTime? _written;
    private DateTimeOffset _read = DateTimeOffset.MinValue;
    private bool _shown;

    public TimeSpan Interval => TimeSpan.FromSeconds(15);

    public void Initialize(MetricStore store) => GameAchievements.Register(store);

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var key = store.GetAttachment(GameArtProvider.Current) is Game { Store: GameStore.Gog, StoreId: { } id } ? GogGalaxy.KeyOf(GameStore.Gog, id) : null;
        if (key is null)
        {
            if (_shown)
                GameAchievements.Publish(store, null);
            _shown = false;
            _key = null;
            _achievements = null;
            return ValueTask.CompletedTask;
        }

        var now = _time.GetUtcNow();
        var written = _files.GalaxyWritten();
        if (key != _key)
        {
            _key = key;
            _achievements = null;
            _written = null;
            _read = DateTimeOffset.MinValue;
        }
        // Galaxy busy writing gives nothing: it's read next time, with what was read before standing till then.
        if (written != _written && now - _read >= ReadEvery && _files.GalaxyGames([key]) is { } galaxy)
        {
            _achievements = galaxy.GetValueOrDefault(key)?.Achievements;
            _written = written;
            _read = now;
        }
        GameAchievements.Publish(store, _achievements);
        _shown = true;
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
    }
}
