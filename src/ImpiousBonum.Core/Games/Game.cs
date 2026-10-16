namespace ImpiousBonum.Core.Games;

/// <summary>A game: its name, its Steam app id (null if Steam didn't install it), and its pictures.</summary>
public sealed record Game(string Name, int? AppId, GameArt Art);

/// <summary>
/// A game's pictures, as image bytes (JPEG, PNG or ICO) for a widget to decode at the size it shows them, like the album
/// art: Steam's cover (the tall box art), header (the wide banner) and hero (the big picture at the top of the game's
/// page in Steam's library); or for a game Steam didn't install, or hasn't pictures of yet, the exe's own icon.
/// </summary>
public sealed record GameArt(byte[]? Cover, byte[]? Header, byte[]? Hero, byte[]? Icon)
{
    public const string CoverKind = "cover";
    public const string HeaderKind = "header";
    public const string HeroKind = "hero";

    /// <summary>The kinds of picture to choose from, for a widget's settings.</summary>
    public static readonly IReadOnlyList<string> Kinds = [CoverKind, HeaderKind, HeroKind];

    /// <summary>
    /// The picture of that kind; else whichever of Steam's others it has, cover first; else the icon. Null if there's
    /// nothing at all.
    /// </summary>
    public byte[]? Picture(string? kind) => kind?.ToLowerInvariant() switch
    {
        HeaderKind => Header,
        HeroKind => Hero,
        _ => Cover,
    } ?? Cover ?? Header ?? Hero ?? Icon;

    /// <summary>Whether <paramref name="picture"/> is the icon rather than one of Steam's: it's square and small, so it's best shown whole.</summary>
    public bool IsIcon(byte[]? picture) => picture is not null && ReferenceEquals(picture, Icon);

    /// <summary>True when Steam has any of its pictures of the game.</summary>
    public bool HasSteamArt => Cover is not null || Header is not null || Hero is not null;
}
