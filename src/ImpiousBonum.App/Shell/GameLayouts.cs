namespace ImpiousBonum.App.Shell;

/// <summary>A link from a game, by process name (<c>.exe</c> optional), to the saved layout shown while it's in front.</summary>
public sealed record GameLayoutRule(string Process, string Layout)
{
    /// <summary>Case-insensitive, ignoring a trailing <c>.exe</c> on either side. A rule with no process never matches.</summary>
    public bool Matches(string? processName) =>
        !string.IsNullOrWhiteSpace(Process) && !string.IsNullOrWhiteSpace(processName)
        && string.Equals(Normalize(Process), Normalize(processName), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string processName)
    {
        var name = processName.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}

/// <summary>
/// Decides which game's layout should be showing, from the app the user is looking at (sampled about once a second).
/// A change only counts once it has held for the delay, so alt-tabbing or a launcher flashing up doesn't flicker the
/// dashboard, and leaving a game only switches back once it has been gone for the same time.
/// </summary>
public sealed class GameLayoutSwitcher(TimeSpan delay)
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(3);

    private GameLayoutRule? _pending;
    private DateTime _pendingSince;
    private bool _hasPending;

    /// <summary>The rule whose layout should be showing, or null for the user's own choice.</summary>
    public GameLayoutRule? Current { get; private set; }

    /// <summary>The first rule for <paramref name="processName"/>, or null.</summary>
    public static GameLayoutRule? Match(IEnumerable<GameLayoutRule> rules, string? processName) =>
        rules.FirstOrDefault(rule => rule is not null && rule.Matches(processName));

    /// <summary>Feeds one sample. Returns true when <see cref="Current"/> changed.</summary>
    public bool Update(IEnumerable<GameLayoutRule> rules, string? processName, DateTime now)
    {
        var match = Match(rules, processName);
        if (SameLayout(match, Current))
        {
            // Back before the delay ran out (or never left): forget the pending change.
            _hasPending = false;
            return false;
        }

        if (!_hasPending || !SameLayout(match, _pending))
        {
            _pending = match;
            _pendingSince = now;
            _hasPending = true;
        }

        if (now - _pendingSince < delay)
            return false;

        Current = _pending;
        _hasPending = false;
        return true;
    }

    /// <summary>Back to the user's own layout straight away (automatic switching turned off).</summary>
    public void Reset()
    {
        Current = null;
        _hasPending = false;
    }

    // Two games linked to the same layout count as no change.
    private static bool SameLayout(GameLayoutRule? a, GameLayoutRule? b) =>
        string.Equals(a?.Layout, b?.Layout, StringComparison.OrdinalIgnoreCase);
}
