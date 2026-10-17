namespace ImpiousBonum.App.Editor;

/// <summary>
/// The rows of the editor's Widgets list: the widget drawn on top first, the other way up from the layout's drawing
/// order, and each group under a row of its own. Works out what dragging a row somewhere else does to the drawing order
/// and the groups. No UI, so it can be tested.
/// </summary>
internal static class WidgetRows
{
    /// <summary>A widget's row, by its index in the drawing order, and the group it's in; or a group's own row (no widget).</summary>
    internal sealed record Row(int Widget, string? Group)
    {
        public bool IsGroup => Widget < 0;

        public static Row ForGroup(string group) => new(-1, group);
    }

    /// <summary>The rows for widgets in these groups (by index, back to front, null for none): top first, each group's row above its widgets.</summary>
    public static List<Row> For(IReadOnlyList<string?> groups)
    {
        var rows = new List<Row>();
        string? current = null;
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            if (group is not null && group != current)
                rows.Add(Row.ForGroup(group));
            current = group;
            rows.Add(new Row(i, group));
        }
        return rows;
    }

    /// <summary>
    /// What moving row <paramref name="from"/> so it ends up at <paramref name="to"/> does (take it out, then put it in
    /// there, as <see cref="Controls.DragReorder"/> says): the drawing order afterwards, as the widgets' indices now, back
    /// to front, and the group each widget is in then, by its index now. A group's row takes its widgets with it, and
    /// goes above or below another group rather than into it. A widget put among a group's widgets, or just under its
    /// row, is in that group; anywhere else, it's in none. Null when nothing would change.
    /// </summary>
    public static (List<int> Order, string?[] Groups)? Move(IReadOnlyList<string?> groups, int from, int to)
    {
        var rows = For(groups);
        if (from == to || from < 0 || from >= rows.Count || to < 0 || to >= rows.Count)
            return null;

        var moved = rows[from];
        List<Row> result;
        if (moved.IsGroup)
        {
            var end = RunEnd(rows, from);
            // The gap in the list as it is now that the moved row's top lands in.
            var gap = to < from ? to : to + 1;
            // A group's row a place down, as Alt+Down does, goes past what's below the group rather than into itself.
            if (to == from + 1)
                gap = end < rows.Count ? (rows[end].IsGroup ? RunEnd(rows, end) : end + 1) : end;
            if (gap >= from && gap <= end)
                return null;

            var block = rows.GetRange(from, end - from);
            result = [.. rows.Take(from), .. rows.Skip(end)];
            var at = gap < from ? gap : gap - block.Count;
            // Not into another group: above it, going up, or below it, going down.
            if (Inside(result, at))
            {
                if (gap < from)
                {
                    while (!result[at].IsGroup)
                        at--;
                }
                else
                {
                    while (at < result.Count && Inside(result, at))
                        at++;
                }
            }
            result.InsertRange(at, block);
        }
        else
        {
            result = [.. rows];
            result.RemoveAt(from);
            var below = to < result.Count ? result[to] : null;
            var above = to > 0 ? result[to - 1] : null;
            var group = below is { IsGroup: false, Group: { } inside } ? inside
                : above is { IsGroup: true } ? above.Group
                : null;
            result.Insert(to, moved with { Group = group });
        }

        var topFirst = result.Where(r => !r.IsGroup).ToList();
        var order = topFirst.Select(r => r.Widget).Reverse().ToList();
        var after = new string?[groups.Count];
        foreach (var row in topFirst)
            after[row.Widget] = row.Group;
        if (order.SequenceEqual(Enumerable.Range(0, groups.Count)) && after.SequenceEqual(groups))
            return null;
        return (order, after);
    }

    /// <summary>Where the rows of the group whose row is at <paramref name="start"/> end: the index after its last widget's.</summary>
    private static int RunEnd(List<Row> rows, int start)
    {
        var end = start + 1;
        while (end < rows.Count && !rows[end].IsGroup && rows[end].Group == rows[start].Group)
            end++;
        return end;
    }

    /// <summary>Whether a row put in at <paramref name="at"/> would be among a group's rows: just under its row, or between its widgets.</summary>
    private static bool Inside(List<Row> rows, int at) =>
        at > 0 && at < rows.Count && !rows[at].IsGroup && rows[at].Group is { } group && rows[at - 1].Group == group;
}
