using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Editor;

/// <summary>What changed, so views can do the least work: move one widget, rebuild one, or rebuild everything.</summary>
public enum ChangeKind
{
    /// <summary>A widget's position or size.</summary>
    Geometry,

    /// <summary>Settings of one widget, the theme or the canvas.</summary>
    Content,

    /// <summary>Widgets added, removed or reordered.</summary>
    Structure,

    /// <summary>The whole document was replaced (undo, redo, revert).</summary>
    Reset,
}

/// <param name="Origin">The view that made the change, so it can skip refreshing itself (and keep the caret where it is).</param>
public sealed record LayoutChange(ChangeKind Kind, int? WidgetIndex, object? Origin);

/// <summary>Where a setting lives: the canvas, the theme, a widget, or an entry in a widget's list setting.</summary>
public sealed record SettingTarget(SettingTargetKind Kind, int WidgetIndex = -1, string? ItemsKey = null, int ItemIndex = -1)
{
    public static readonly SettingTarget Canvas = new(SettingTargetKind.Canvas);
    public static readonly SettingTarget Theme = new(SettingTargetKind.Theme);

    public static SettingTarget Widget(int index) => new(SettingTargetKind.Widget, index);

    public static SettingTarget Item(int widgetIndex, string itemsKey, int itemIndex) => new(SettingTargetKind.Item, widgetIndex, itemsKey, itemIndex);
}

public enum SettingTargetKind
{
    Canvas,
    Theme,
    Widget,
    Item,
}

/// <summary>
/// The editor's working copy of a layout: selection, edits and undo/redo. Views observe <see cref="Changed"/>.
/// Undo keeps JSON snapshots, so it's simple and always exact. Consecutive edits with the same merge key
/// (one drag, a burst of typing in one field) collapse into a single undo step.
/// </summary>
public sealed class LayoutSession
{
    private const int MaxUndo = 200;
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(1.5);

    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly TimeProvider _clock;
    private string _saved;
    private string? _lastMergeKey;
    private long _lastEditTimestamp;
    private ThemeSettings? _resolvedTheme;
    private PreviewState? _preview;
    private List<int> _selection = [];

    /// <param name="themes">Themes the layout can name; null for only the built-in ones.</param>
    /// <param name="clock">For how close together edits are; the system's unless testing.</param>
    public LayoutSession(LayoutDocument document, ThemeLibrary? themes = null, TimeProvider? clock = null)
    {
        Themes = themes ?? new ThemeLibrary(null);
        _clock = clock ?? TimeProvider.System;
        Document = Clone(document);
        _saved = Serialize(Document);
    }

    public LayoutDocument Document { get; private set; }

    public ThemeLibrary Themes { get; }

    /// <summary>The values the layout is drawn with: its own theme values over its named theme's, over the defaults.</summary>
    public ThemeSettings ResolvedTheme => _resolvedTheme ??= Themes.Resolve(Document.Theme);

    /// <summary>The saved theme the layout uses (its theme's <c>base</c>), or null for its own.</summary>
    public string? ThemeName => ThemeLibrary.BaseName(Document.Theme);

    /// <summary>The layout's own theme values that differ from its named theme's (ignoring <c>base</c>).</summary>
    public bool HasThemeOverrides => Document.Theme.Any(p => !string.Equals(p.Key, ThemeLibrary.BaseKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>The selected widgets, in the order they were picked; empty for the canvas and theme.</summary>
    public IReadOnlyList<int> Selection => _selection;

    /// <summary>The selected widget, or -1 for the canvas and theme. With several selected, the first one picked.</summary>
    public int SelectedIndex => _selection.Count > 0 ? _selection[0] : -1;

    public JsonObject? SelectedWidget => SelectedIndex >= 0 ? Document.Widgets[SelectedIndex] : null;

    /// <summary>Whether the layout differs from the saved one. A <see cref="Preview"/> isn't a change until it's kept.</summary>
    public bool IsDirty => (_preview?.Before ?? Serialize(Document)) != _saved;

    public bool CanUndo => _undo.Count > 0;

    /// <summary>
    /// Grid size for moving and resizing, in canvas units; 1 means none. Lives here so the editor's preview and
    /// the dashboard's touch overlay snap the same way. Not part of the layout and not undoable.
    /// </summary>
    public double Snap { get; set; } = 10;

    public bool CanRedo => _redo.Count > 0;

    public event EventHandler<LayoutChange>? Changed;

    public event EventHandler? SelectionChanged;

    /// <summary>Selects one widget on its own, or with -1 none (the canvas and theme).</summary>
    public void Select(int index) => Select(index < 0 ? [] : [index]);

    /// <summary>Selects these widgets, the first being the one picked first. Ones that don't exist are left out.</summary>
    public void Select(IEnumerable<int> indices)
    {
        var selection = indices.Where(i => i >= 0 && i < Document.Widgets.Count).Distinct().ToList();
        if (selection.SequenceEqual(_selection))
            return;
        _selection = selection;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a widget to the selection, or takes it out if it's already in (Ctrl+click).</summary>
    public void ToggleSelected(int index) =>
        Select(_selection.Contains(index) ? _selection.Where(i => i != index) : _selection.Append(index));

    public bool IsSelected(int index) => _selection.Contains(index);

    // ---- Settings -------------------------------------------------------------------------------

    /// <summary>The value stored for <paramref name="key"/>, or null when it isn't set (the default applies).</summary>
    public JsonNode? GetValue(SettingTarget target, string key) => target.Kind switch
    {
        SettingTargetKind.Canvas => key switch
        {
            "width" => Number(Document.Width),
            "height" => Number(Document.Height),
            _ => null,
        },
        SettingTargetKind.Theme => Document.Theme[key]?.DeepClone(),
        _ => Resolve(target)?[key],
    };

    /// <summary>Sets a value, or removes it (back to the default) when <paramref name="value"/> is null.</summary>
    /// <param name="run">
    /// Edits to the setting in the same run share one undo step for as long as nothing else is edited in between, however
    /// long the pauses: stepping through fonts with the arrow keys, stopping to look at each. Without one, only a quick
    /// burst of edits to the setting does, such as typing.
    /// </param>
    /// <param name="merge">
    /// Whether it can share an undo step with the edits just before it to the same setting, as a burst of typing does.
    /// False for a change made in one go, such as ticking a box, which is a step of its own however quickly the next comes.
    /// </param>
    public void SetValue(SettingTarget target, string key, JsonNode? value, object? origin, string? run = null, bool merge = true)
    {
        // Controls often write back the value they were just given (e.g. a drop-down finishing loading).
        // Ignoring no-op writes keeps them out of the undo history and avoids pointless rebuilds.
        if (JsonNode.DeepEquals(GetValue(target, key), value))
            return;

        var (mergeKey, window) = !merge ? (null, MergeWindow)
            : run is null ? ($"set:{target}:{key}", MergeWindow)
            : ($"run:{run}:{target}:{key}", TimeSpan.MaxValue);
        Edit(KindOf(target, key), WidgetIndexOf(target), mergeKey, window, origin, _ => Put(target, key, value));
    }

    /// <summary>Puts a value in the document as it is, with no undo step or notification.</summary>
    private void Put(SettingTarget target, string key, JsonNode? value)
    {
        switch (target.Kind)
        {
            case SettingTargetKind.Canvas:
                var size = JsonDefaults.TryGetNumber(value, out var n) ? n : key == "width" ? 1920 : 480;
                if (key == "width")
                    Document.Width = size;
                else
                    Document.Height = size;
                break;

            case SettingTargetKind.Theme:
                // Removing a value goes back to the named theme's (or the built-in default).
                if (value is null)
                    Document.Theme.Remove(key);
                else
                    Document.Theme[key] = value.DeepClone();
                break;

            default:
                var obj = Resolve(target)!;
                if (value is null)
                    obj.Remove(key);
                else
                    obj[key] = value.DeepClone();
                break;
        }
        _resolvedTheme = null;
    }

    private static ChangeKind KindOf(SettingTarget target, string key) =>
        target.Kind == SettingTargetKind.Widget && key is "x" or "y" or "width" or "height" ? ChangeKind.Geometry : ChangeKind.Content;

    private static int? WidgetIndexOf(SettingTarget target) =>
        target.Kind is SettingTargetKind.Widget or SettingTargetKind.Item ? target.WidgetIndex : null;

    // ---- Previews -------------------------------------------------------------------------------

    /// <summary>
    /// A setting showing a value it hasn't been given yet: what it had, its place among the other settings (-1 when it
    /// had none), and the whole layout before.
    /// </summary>
    private sealed record PreviewState(SettingTarget Target, string Key, JsonNode? Original, int Place, string Before);

    /// <summary>A <see cref="Preview"/> is showing. For tests.</summary>
    internal bool IsPreviewing => _preview is not null;

    /// <summary>
    /// Shows <paramref name="value"/> for a setting without making it an edit yet, such as the font under the mouse in a
    /// drop-down: views redraw, but there's no undo step and the layout doesn't count as changed. Call again to show
    /// another value, then <see cref="EndPreview"/>. An edit, or saving, keeps the value shown first; undo, redo and
    /// revert drop it.
    /// </summary>
    public void Preview(SettingTarget target, string key, JsonNode? value, object? origin)
    {
        if (_preview is { } current && (current.Target != target || current.Key != key))
            EndPreview(keep: true, origin);
        var shown = GetValue(target, key);
        if (JsonNode.DeepEquals(shown, value))
            return;

        _preview ??= new PreviewState(target, key, shown?.DeepClone(), SettingsOf(target)?.IndexOf(key) ?? -1, Serialize(Document));
        Put(target, key, value);
        Changed?.Invoke(this, new LayoutChange(KindOf(target, key), WidgetIndexOf(target), origin));
    }

    /// <summary>
    /// Ends a <see cref="Preview"/>. <paramref name="keep"/> makes the value shown an edit, one undo step back to before
    /// the preview; otherwise the setting goes back exactly as it was. Nothing if there's no preview.
    /// </summary>
    public void EndPreview(bool keep, object? origin)
    {
        if (_preview is not { } preview)
            return;
        _preview = null;

        var (target, key) = (preview.Target, preview.Key);
        var shown = GetValue(target, key)?.DeepClone();

        // Back as it was. Kept, the value goes in again as an ordinary edit, so undo has the layout from before the preview.
        PutBack(preview);
        if (JsonNode.DeepEquals(shown, preview.Original))
            return;
        if (keep)
            Edit(KindOf(target, key), WidgetIndexOf(target), null, MergeWindow, origin, _ => Put(target, key, shown));
        else
            Changed?.Invoke(this, new LayoutChange(KindOf(target, key), WidgetIndexOf(target), origin));
    }

    /// <summary>
    /// Puts a previewed setting back as it was and where it was. Previewing no value takes it out, and putting it back
    /// would add it at the end, which counts as a change to save even though the values are the same.
    /// </summary>
    private void PutBack(PreviewState preview)
    {
        if (SettingsOf(preview.Target) is not { } settings)
        {
            Put(preview.Target, preview.Key, preview.Original);
            return;
        }
        settings.Remove(preview.Key);
        if (preview.Original is { } original)
            settings.Insert(Math.Min(preview.Place, settings.Count), preview.Key, original.DeepClone());
        _resolvedTheme = null;
    }

    /// <summary>The object a setting is stored in, whose order is kept in the file; null for the canvas's size.</summary>
    private JsonObject? SettingsOf(SettingTarget target) => target.Kind switch
    {
        SettingTargetKind.Canvas => null,
        SettingTargetKind.Theme => Document.Theme,
        _ => Resolve(target),
    };

    public void SetGeometry(int index, Rect rect, string mergeKey, object? origin) => SetGeometry([(index, rect)], mergeKey, origin);

    /// <summary>
    /// Moves or resizes several widgets as one edit, so one undo step (or part of one, with <paramref name="mergeKey"/>).
    /// Views hear about each widget that changed, as a <see cref="ChangeKind.Geometry"/> change of its own, so they can
    /// move just those.
    /// </summary>
    public void SetGeometry(IReadOnlyList<(int Index, Rect Rect)> changes, string? mergeKey, object? origin)
    {
        var changed = changes.Where(c => c.Index >= 0 && c.Index < Document.Widgets.Count && GeometryOf(Document.Widgets[c.Index]) != c.Rect).ToList();
        if (changed.Count == 0)
            return;
        Record(mergeKey, MergeWindow, origin);
        foreach (var (index, rect) in changed)
        {
            var widget = Document.Widgets[index];
            widget["x"] = Number(rect.X);
            widget["y"] = Number(rect.Y);
            widget["width"] = Number(Math.Max(1, rect.Width));
            widget["height"] = Number(Math.Max(1, rect.Height));
        }
        foreach (var (index, _) in changed)
            Changed?.Invoke(this, new LayoutChange(ChangeKind.Geometry, index, origin));
    }

    /// <summary>Moves the selected widgets by an offset, as the arrow keys do. A run of nudges to the same widgets is one undo step.</summary>
    public void NudgeSelected(double dx, double dy)
    {
        var changes = _selection.Select(i => (i, Rect.Offset(GeometryOf(Document.Widgets[i]), dx, dy))).ToList();
        SetGeometry(changes, $"nudge:{string.Join(",", _selection)}", null);
    }

    /// <summary>
    /// Lines up, spaces out or sizes the selected widgets (see <see cref="ArrangeGeometry"/>), as one undo step. Nothing
    /// with fewer selected than it needs.
    /// </summary>
    public void ArrangeSelected(Arrangement arrangement)
    {
        var placed = ArrangeGeometry.Apply(arrangement, [.. _selection.Select(i => GeometryOf(Document.Widgets[i]))]);
        SetGeometry([.. _selection.Select((index, n) => (index, placed[n]))], null, null);
    }

    public static Rect GeometryOf(JsonObject widget) => new(
        widget.GetDouble("x", 0),
        widget.GetDouble("y", 0),
        Math.Max(1, widget.GetDouble("width", 200)),
        Math.Max(1, widget.GetDouble("height", 100)));

    /// <summary>The value the theme setting <paramref name="key"/> has once resolved, set or not (for showing a default).</summary>
    public JsonNode? GetResolvedThemeValue(string key) => ResolvedTheme.ToJson()[key]?.DeepClone();

    // ---- Themes ---------------------------------------------------------------------------------

    /// <summary>
    /// Switches to a saved theme (null for the layout's own values, starting from the defaults). The layout's own theme
    /// values are dropped, since they'd otherwise hide the new theme's. Undoable.
    /// </summary>
    public void UseTheme(string? name)
    {
        var theme = name is null ? new JsonObject() : new JsonObject { [ThemeLibrary.BaseKey] = name };
        if (JsonNode.DeepEquals(Document.Theme, theme))
            return;
        Edit(ChangeKind.Content, null, null, null, document => document.Theme = theme);
    }

    /// <summary>Copies the named theme's values into the layout, so it no longer depends on the theme. Undoable.</summary>
    public void DetachTheme()
    {
        if (ThemeName is null)
            return;
        var values = ResolvedTheme.ToJson();
        Edit(ChangeKind.Content, null, null, null, document => document.Theme = values);
    }

    /// <summary>
    /// Saves the theme the layout is drawn with as a saved theme, and switches the layout to it with no values of its own.
    /// Used by "Save theme as" (a new name) and "Update theme" (the named theme). Undoable, though the file stays written.
    /// </summary>
    public void SaveThemeAs(string name)
    {
        Themes.Save(name, ResolvedTheme.ToJson());
        _resolvedTheme = null;
        var saved = Themes.Find(name) ?? name;
        Edit(ChangeKind.Content, null, null, null, document => document.Theme = new JsonObject { [ThemeLibrary.BaseKey] = saved });
    }

    /// <summary>A saved theme changed on disk: re-resolve and redraw.</summary>
    public void RefreshTheme()
    {
        _resolvedTheme = null;
        Changed?.Invoke(this, new LayoutChange(ChangeKind.Content, null, null));
    }

    // ---- Widgets --------------------------------------------------------------------------------

    public void AddWidget(WidgetDescriptor descriptor)
    {
        var width = Math.Min(descriptor.DefaultWidth, Document.Width);
        var height = Math.Min(descriptor.DefaultHeight, Document.Height);
        var widget = new JsonObject
        {
            ["type"] = descriptor.Type,
            ["x"] = Number(Math.Round((Document.Width - width) / 2)),
            ["y"] = Number(Math.Round((Document.Height - height) / 2)),
            ["width"] = Number(width),
            ["height"] = Number(height),
        };
        // Lists with default entries start with them in the file, so editing the list edits what's on screen.
        foreach (var items in descriptor.Settings.Where(s => s.Default is JsonArray))
            widget[items.Key] = ((JsonArray)items.Default!).DeepClone();
        if (descriptor.AddBehind)
        {
            Edit(ChangeKind.Structure, null, null, null, document => document.Widgets.Insert(0, widget));
            Select(0);
            return;
        }
        Edit(ChangeKind.Structure, null, null, null, document => document.Widgets.Add(widget));
        Select(Document.Widgets.Count - 1);
    }

    /// <summary>
    /// Copies the selected widgets, each a little down and to the right of the one it copies and drawn just above it,
    /// as one undo step, and selects the copies.
    /// </summary>
    public void DuplicateSelected()
    {
        if (_selection.Count == 0)
            return;
        var sources = _selection.Order().ToList();
        var copies = sources.Select(i =>
        {
            var source = Document.Widgets[i];
            var copy = source.DeepClone().AsObject();
            copy["x"] = Number(source.GetDouble("x", 0) + 20);
            copy["y"] = Number(source.GetDouble("y", 0) + 20);
            return copy;
        }).ToList();
        Edit(ChangeKind.Structure, null, null, null, document =>
        {
            // From the end, so the places of the ones still to copy don't move.
            for (var n = sources.Count - 1; n >= 0; n--)
                document.Widgets.Insert(sources[n] + 1, copies[n]);
        });
        // Each copy is a place further on for every copy put in before it.
        Select(_selection.Select(i => i + sources.IndexOf(i) + 1));
    }

    /// <summary>Deletes the selected widgets, as one undo step.</summary>
    public void DeleteSelected()
    {
        if (_selection.Count == 0)
            return;
        var doomed = _selection.OrderDescending().ToList();
        _selection = [];
        Edit(ChangeKind.Structure, null, null, null, document =>
        {
            foreach (var index in doomed)
                document.Widgets.RemoveAt(index);
        });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the selected widget up (+1, drawn later, so on top) or down (-1) the stacking order. Not with several selected.</summary>
    public void MoveSelected(int delta)
    {
        if (_selection.Count == 1)
            MoveWidget(SelectedIndex, SelectedIndex + delta);
    }

    /// <summary>
    /// Moves a widget to another place in the drawing order (later draws on top), as one undo step, and selects it:
    /// dragging it in the editor's list, or bringing it forward or sending it back.
    /// </summary>
    public void MoveWidget(int from, int to)
    {
        var count = Document.Widgets.Count;
        if (from == to || from < 0 || from >= count || to < 0 || to >= count)
            return;
        // Selected before the change, so views rebuilding for it already see the moved widget as the selected one.
        _selection = [to];
        Edit(ChangeKind.Structure, null, null, null, document =>
        {
            var widget = document.Widgets[from];
            document.Widgets.RemoveAt(from);
            document.Widgets.Insert(to, widget);
        });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves an entry of a list setting (e.g. a rows widget's rows) to another place, as one undo step: take it out at
    /// <paramref name="from"/>, then insert it at <paramref name="to"/>.
    /// </summary>
    public void MoveItem(int widgetIndex, string key, int from, int to)
    {
        if (widgetIndex < 0 || widgetIndex >= Document.Widgets.Count || Document.Widgets[widgetIndex][key] is not JsonArray items
            || from == to || from < 0 || from >= items.Count || to < 0 || to >= items.Count)
            return;
        EditItems(widgetIndex, key, entries =>
        {
            var entry = entries[from];
            entries.RemoveAt(from);
            entries.Insert(to, entry);
        });
    }

    /// <summary>Adds, removes or reorders entries of a list setting (e.g. a rows widget's rows).</summary>
    public void EditItems(int widgetIndex, string key, Action<JsonArray> change) =>
        Edit(ChangeKind.Content, widgetIndex, null, null, document =>
        {
            var widget = document.Widgets[widgetIndex];
            if (widget[key] is not JsonArray items)
            {
                // A list left out of the file shows its default entries, so editing starts from those.
                var defaults = WidgetFactory.Find(widget.GetString("type"))?.Find(key)?.Default as JsonArray;
                widget[key] = items = defaults?.DeepClone().AsArray() ?? [];
            }
            change(items);
        });

    // ---- History ---------------------------------------------------------------------------------

    public void Undo() => Restore(_undo, _redo);

    public void Redo() => Restore(_redo, _undo);

    /// <summary>Replaces the working copy (e.g. "revert to saved"). Undoable.</summary>
    public void Replace(LayoutDocument document)
    {
        EndPreview(keep: false, null);
        PushUndo();
        _redo.Clear();
        _lastMergeKey = null;
        Document = Clone(document);
        _resolvedTheme = null;
        _selection = [];
        Changed?.Invoke(this, new LayoutChange(ChangeKind.Reset, null, null));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts over on another document (switching layouts): no undo history, and nothing unsaved.</summary>
    public void Reset(LayoutDocument document)
    {
        _preview = null;
        _undo.Clear();
        _redo.Clear();
        _lastMergeKey = null;
        Document = Clone(document);
        _resolvedTheme = null;
        _saved = Serialize(Document);
        _selection = [];
        Changed?.Invoke(this, new LayoutChange(ChangeKind.Reset, null, null));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The layout as it is now has been saved. A preview showing was saved with it, so it's kept.</summary>
    public void MarkSaved()
    {
        EndPreview(keep: true, null);
        _saved = Serialize(Document);
        _lastMergeKey = null;
    }

    /// <summary>
    /// Applies a change. Consecutive edits with the same <paramref name="mergeKey"/> within a short window
    /// share one undo step; a null key always starts a new one.
    /// </summary>
    public void Edit(ChangeKind kind, int? widgetIndex, string? mergeKey, object? origin, Action<LayoutDocument> change) =>
        Edit(kind, widgetIndex, mergeKey, MergeWindow, origin, change);

    private void Edit(ChangeKind kind, int? widgetIndex, string? mergeKey, TimeSpan mergeWindow, object? origin, Action<LayoutDocument> change)
    {
        Record(mergeKey, mergeWindow, origin);
        change(Document);
        _resolvedTheme = null;
        Changed?.Invoke(this, new LayoutChange(kind, widgetIndex, origin));
    }

    /// <summary>Gets ready for an edit: an undo step from before it, unless it carries on the last edit with the same merge key.</summary>
    private void Record(string? mergeKey, TimeSpan mergeWindow, object? origin)
    {
        // A preview showing becomes an edit of its own first, so it's neither lost nor mixed into this one.
        EndPreview(keep: true, origin);

        var now = _clock.GetTimestamp();
        var merge = mergeKey is not null && mergeKey == _lastMergeKey && _clock.GetElapsedTime(_lastEditTimestamp, now) < mergeWindow;
        if (!merge)
            PushUndo();

        _lastMergeKey = mergeKey;
        _lastEditTimestamp = now;
        _redo.Clear();
    }

    private void PushUndo()
    {
        _undo.Push(Serialize(Document));
        if (_undo.Count > MaxUndo)
        {
            // Stack has no "drop oldest"; rebuild without it. Rare, and the documents are small.
            var kept = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            foreach (var snapshot in kept)
                _undo.Push(snapshot);
        }
    }

    private void Restore(Stack<string> from, Stack<string> to)
    {
        // A preview isn't an edit, so there's nothing of it to undo: it goes, and undo or redo carries on as usual.
        EndPreview(keep: false, null);
        if (from.Count == 0)
            return;
        to.Push(Serialize(Document));
        Document = Deserialize(from.Pop());
        _resolvedTheme = null;
        _lastMergeKey = null;
        // Selected widgets the layout no longer has (one added since, say) aren't selected any more.
        _selection = [.. _selection.Where(i => i < Document.Widgets.Count)];
        Changed?.Invoke(this, new LayoutChange(ChangeKind.Reset, null, null));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private JsonObject? Resolve(SettingTarget target)
    {
        if (target.WidgetIndex < 0 || target.WidgetIndex >= Document.Widgets.Count)
            return null;
        var widget = Document.Widgets[target.WidgetIndex];
        if (target.Kind == SettingTargetKind.Widget)
            return widget;
        return widget[target.ItemsKey!] is JsonArray items && target.ItemIndex < items.Count ? items[target.ItemIndex] as JsonObject : null;
    }

    /// <summary>Whole numbers are stored as integers so layout.json stays tidy.</summary>
    public static JsonValue Number(double value) =>
        value == Math.Round(value) && Math.Abs(value) < int.MaxValue ? JsonValue.Create((int)value) : JsonValue.Create(Math.Round(value, 2));

    private static string Serialize(LayoutDocument document) => JsonSerializer.Serialize(document, JsonDefaults.Options);

    private static LayoutDocument Deserialize(string json) => JsonSerializer.Deserialize<LayoutDocument>(json, JsonDefaults.Options)!;

    private static LayoutDocument Clone(LayoutDocument document) => Deserialize(Serialize(document));
}
