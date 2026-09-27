using System.Diagnostics;
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
    private string _saved;
    private string? _lastMergeKey;
    private long _lastEditTimestamp;

    public LayoutSession(LayoutDocument document)
    {
        Document = Clone(document);
        _saved = Serialize(Document);
    }

    public LayoutDocument Document { get; private set; }

    /// <summary>The selected widget, or -1 for the canvas and theme.</summary>
    public int SelectedIndex { get; private set; } = -1;

    public JsonObject? SelectedWidget => SelectedIndex >= 0 ? Document.Widgets[SelectedIndex] : null;

    public bool IsDirty => Serialize(Document) != _saved;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event EventHandler<LayoutChange>? Changed;

    public event EventHandler? SelectionChanged;

    public void Select(int index)
    {
        index = index < 0 || index >= Document.Widgets.Count ? -1 : index;
        if (index == SelectedIndex)
            return;
        SelectedIndex = index;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

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
        SettingTargetKind.Theme => ThemeObject()[key]?.DeepClone(),
        _ => Resolve(target)?[key],
    };

    /// <summary>Sets a value, or removes it (back to the default) when <paramref name="value"/> is null.</summary>
    public void SetValue(SettingTarget target, string key, JsonNode? value, object? origin)
    {
        // Controls often write back the value they were just given (e.g. a drop-down finishing loading).
        // Ignoring no-op writes keeps them out of the undo history and avoids pointless rebuilds.
        if (JsonNode.DeepEquals(GetValue(target, key), value))
            return;

        var isGeometry = target.Kind == SettingTargetKind.Widget && key is "x" or "y" or "width" or "height";
        var kind = isGeometry ? ChangeKind.Geometry : ChangeKind.Content;

        Edit(kind, target.Kind is SettingTargetKind.Widget or SettingTargetKind.Item ? target.WidgetIndex : null, $"set:{target}:{key}", origin, document =>
        {
            switch (target.Kind)
            {
                case SettingTargetKind.Canvas:
                    var size = JsonDefaults.TryGetNumber(value, out var n) ? n : key == "width" ? 1920 : 480;
                    if (key == "width")
                        document.Width = size;
                    else
                        document.Height = size;
                    break;

                case SettingTargetKind.Theme:
                    var theme = ThemeObject();
                    // Remove rather than write null: a missing key deserialises to the built-in default, an explicit null doesn't.
                    if (value is null)
                        theme.Remove(key);
                    else
                        theme[key] = value.DeepClone();
                    document.Theme = theme.Deserialize<ThemeSettings>(JsonDefaults.Options) ?? new ThemeSettings();
                    break;

                default:
                    var obj = Resolve(target)!;
                    if (value is null)
                        obj.Remove(key);
                    else
                        obj[key] = value.DeepClone();
                    break;
            }
        });
    }

    public void SetGeometry(int index, Rect rect, string mergeKey, object? origin)
    {
        if (GeometryOf(Document.Widgets[index]) == rect)
            return;
        Edit(ChangeKind.Geometry, index, mergeKey, origin, document =>
        {
            var widget = document.Widgets[index];
            widget["x"] = Number(rect.X);
            widget["y"] = Number(rect.Y);
            widget["width"] = Number(Math.Max(1, rect.Width));
            widget["height"] = Number(Math.Max(1, rect.Height));
        });
    }

    public static Rect GeometryOf(JsonObject widget) => new(
        widget.GetDouble("x", 0),
        widget.GetDouble("y", 0),
        Math.Max(1, widget.GetDouble("width", 200)),
        Math.Max(1, widget.GetDouble("height", 100)));

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
        Edit(ChangeKind.Structure, null, null, null, document => document.Widgets.Add(widget));
        Select(Document.Widgets.Count - 1);
    }

    public void DuplicateSelected()
    {
        if (SelectedWidget is not { } source)
            return;
        var copy = source.DeepClone().AsObject();
        copy["x"] = Number(source.GetDouble("x", 0) + 20);
        copy["y"] = Number(source.GetDouble("y", 0) + 20);
        var index = SelectedIndex + 1;
        Edit(ChangeKind.Structure, null, null, null, document => document.Widgets.Insert(index, copy));
        Select(index);
    }

    public void DeleteSelected()
    {
        if (SelectedIndex < 0)
            return;
        var index = SelectedIndex;
        SelectedIndex = -1;
        Edit(ChangeKind.Structure, null, null, null, document => document.Widgets.RemoveAt(index));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the selected widget up (+1, drawn later, so on top) or down (-1) the stacking order.</summary>
    public void MoveSelected(int delta)
    {
        var from = SelectedIndex;
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Document.Widgets.Count)
            return;
        Edit(ChangeKind.Structure, null, null, null, document =>
        {
            var widget = document.Widgets[from];
            document.Widgets.RemoveAt(from);
            document.Widgets.Insert(to, widget);
        });
        SelectedIndex = to;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds, removes or reorders entries of a list setting (e.g. a rows widget's rows).</summary>
    public void EditItems(int widgetIndex, string key, Action<JsonArray> change) =>
        Edit(ChangeKind.Content, widgetIndex, null, null, document =>
        {
            var widget = document.Widgets[widgetIndex];
            if (widget[key] is not JsonArray items)
                widget[key] = items = [];
            change(items);
        });

    // ---- History ---------------------------------------------------------------------------------

    public void Undo() => Restore(_undo, _redo);

    public void Redo() => Restore(_redo, _undo);

    /// <summary>Replaces the working copy (e.g. "revert to saved"). Undoable.</summary>
    public void Replace(LayoutDocument document)
    {
        PushUndo();
        _redo.Clear();
        _lastMergeKey = null;
        Document = Clone(document);
        SelectedIndex = -1;
        Changed?.Invoke(this, new LayoutChange(ChangeKind.Reset, null, null));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved()
    {
        _saved = Serialize(Document);
        _lastMergeKey = null;
    }

    /// <summary>
    /// Applies a change. Consecutive edits with the same <paramref name="mergeKey"/> within a short window
    /// share one undo step; a null key always starts a new one.
    /// </summary>
    public void Edit(ChangeKind kind, int? widgetIndex, string? mergeKey, object? origin, Action<LayoutDocument> change)
    {
        var now = Stopwatch.GetTimestamp();
        var merge = mergeKey is not null && mergeKey == _lastMergeKey && Stopwatch.GetElapsedTime(_lastEditTimestamp, now) < MergeWindow;
        if (!merge)
            PushUndo();

        _lastMergeKey = mergeKey;
        _lastEditTimestamp = now;
        _redo.Clear();

        change(Document);
        Changed?.Invoke(this, new LayoutChange(kind, widgetIndex, origin));
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
        if (from.Count == 0)
            return;
        to.Push(Serialize(Document));
        Document = Deserialize(from.Pop());
        _lastMergeKey = null;
        if (SelectedIndex >= Document.Widgets.Count)
            SelectedIndex = -1;
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

    private JsonObject ThemeObject() => JsonSerializer.SerializeToNode(Document.Theme, JsonDefaults.Options)!.AsObject();

    /// <summary>Whole numbers are stored as integers so layout.json stays tidy.</summary>
    public static JsonValue Number(double value) =>
        value == Math.Round(value) && Math.Abs(value) < int.MaxValue ? JsonValue.Create((int)value) : JsonValue.Create(Math.Round(value, 2));

    private static string Serialize(LayoutDocument document) => JsonSerializer.Serialize(document, JsonDefaults.Options);

    private static LayoutDocument Deserialize(string json) => JsonSerializer.Deserialize<LayoutDocument>(json, JsonDefaults.Options)!;

    private static LayoutDocument Clone(LayoutDocument document) => Deserialize(Serialize(document));
}
