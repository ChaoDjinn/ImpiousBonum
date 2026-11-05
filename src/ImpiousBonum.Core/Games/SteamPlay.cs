namespace ImpiousBonum.Core.Games;

/// <summary>
/// When a Steam game was last played (null if Steam doesn't say) and for how many minutes altogether, as Steam keeps
/// them on this PC for whoever signs in.
/// </summary>
public sealed record SteamPlay(DateTimeOffset? LastPlayed, int Minutes);
