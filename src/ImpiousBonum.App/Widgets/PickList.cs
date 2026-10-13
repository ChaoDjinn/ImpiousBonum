namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The value of a <see cref="SettingKind.Picks"/> setting: some names from a list in the order chosen, or all of them.
/// Stored as text, <c>all</c> or names separated by commas (<c>BBC, The Verge</c>), so it reads well in layout.json and
/// what was typed there before stays valid. Names match whatever their case.
/// </summary>
public sealed class PickList
{
    public const string AllValue = "all";

    private PickList(bool all, IReadOnlyList<string> names)
    {
        All = all;
        Names = names;
    }

    /// <summary>Every option, in the list's own order.</summary>
    public bool All { get; }

    /// <summary>The names picked, in order; none when <see cref="All"/>.</summary>
    public IReadOnlyList<string> Names { get; }

    public static PickList Everything { get; } = new(true, []);

    public static PickList Of(IEnumerable<string> names) => new(false, Tidy(names));

    /// <param name="allowAll">
    /// Whether the setting can pick all. Then <c>all</c> means that, and so does nothing picked, as an empty value always
    /// has; otherwise <c>all</c> is a name like any other.
    /// </param>
    public static PickList Parse(string? value, bool allowAll)
    {
        var text = value?.Trim() ?? string.Empty;
        if (allowAll && text.Equals(AllValue, StringComparison.OrdinalIgnoreCase))
            return Everything;
        var names = Tidy(text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return allowAll && names.Count == 0 ? Everything : new PickList(false, names);
    }

    public override string ToString() => All ? AllValue : string.Join(", ", Names);

    /// <summary>
    /// What a picker lists: the picked names first in their order, then the other options in theirs. With
    /// <see cref="All"/>, every option, ticked. A picked name that isn't an option any more (a feed since removed or
    /// renamed) is still there, ticked, so it can be unticked; one that is takes the option's spelling.
    /// </summary>
    public IReadOnlyList<PickEntry> Entries(IReadOnlyList<string> options)
    {
        if (All)
            return [.. options.Select(o => new PickEntry(o, Ticked: true, Found: true))];
        var picked = Names.Select(n => Option(options, n) is { } option ? new PickEntry(option, true, true) : new PickEntry(n, true, false)).ToList();
        return [.. picked, .. options.Where(o => !Contains(Names, o)).Select(o => new PickEntry(o, Ticked: false, Found: true))];
    }

    /// <summary>With <paramref name="name"/> picked too, after the others.</summary>
    public PickList Tick(string name) => All || Contains(Names, name) ? this : Of([.. Names, name]);

    /// <summary>
    /// Without <paramref name="name"/>. Unticking the last one of a setting that can pick all goes back to all, which is
    /// what nothing picked shows.
    /// </summary>
    public PickList Untick(string name, bool allowAll)
    {
        var names = Names.Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        return allowAll && names.Count == 0 ? Everything : new PickList(false, names);
    }

    /// <summary>The picked name at <paramref name="from"/> moved to <paramref name="to"/>, among the picked ones.</summary>
    public PickList Move(int from, int to)
    {
        if (All || from < 0 || from >= Names.Count)
            return this;
        to = Math.Clamp(to, 0, Names.Count - 1);
        var names = Names.ToList();
        var name = names[from];
        names.RemoveAt(from);
        names.Insert(to, name);
        return new PickList(false, names);
    }

    private static string? Option(IReadOnlyList<string> options, string name) =>
        options.FirstOrDefault(o => o.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool Contains(IReadOnlyList<string> names, string name) => Option(names, name) is not null;

    /// <summary>Trimmed, without blanks, and each name once (the first time it's there).</summary>
    private static List<string> Tidy(IEnumerable<string> names) =>
        [.. names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
}

/// <summary>A name in a picker: whether it's ticked, and whether it's one of the options (or left over from before).</summary>
public sealed record PickEntry(string Name, bool Ticked, bool Found);
