using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ImpiousBonum.App.Controls;

/// <summary>
/// Lets the user put a vertical list in order by dragging its entries: the items of a ListBox (or any ItemsControl), or
/// the children of a Panel. A drag only starts once the pointer has moved Windows' drag distance, so a click is still a
/// click. A line shows where the entry will land, the list scrolls when the pointer nears its top or bottom, and Esc
/// (or losing the mouse) cancels. Alt+Up and Alt+Down move the entry that has focus, for the keyboard. Either way the
/// list's owner is told where the entry was and where it goes, and does the moving itself.
/// </summary>
public sealed class DragReorder
{
    /// <summary>
    /// Marks the grip an entry is dragged by, for lists attached with <c>byHandle</c>, whose entries hold text boxes
    /// and buttons that need the mouse for themselves.
    /// </summary>
    public static readonly DependencyProperty IsHandleProperty = DependencyProperty.RegisterAttached(
        "IsHandle", typeof(bool), typeof(DragReorder), new PropertyMetadata(false));

    public static bool GetIsHandle(DependencyObject element) => (bool)element.GetValue(IsHandleProperty);

    public static void SetIsHandle(DependencyObject element, bool value) => element.SetValue(IsHandleProperty, value);

    private static readonly DependencyProperty InstanceProperty = DependencyProperty.RegisterAttached(
        "Instance", typeof(DragReorder), typeof(DragReorder));

    /// <summary>How near the top or bottom of the scrolled area the pointer starts it scrolling.</summary>
    private const double EdgeZone = 32;

    /// <summary>The most it scrolls each tick, with the pointer at (or past) the edge.</summary>
    private const double MaxScrollStep = 10;

    private static readonly TimeSpan ScrollTick = TimeSpan.FromMilliseconds(30);

    private readonly FrameworkElement _list;
    private readonly Action<int, int> _moved;
    private readonly bool _byHandle;
    private Press? _press;
    private Drag? _drag;

    private DragReorder(FrameworkElement list, Action<int, int> moved, bool byHandle)
    {
        _list = list;
        _moved = moved;
        _byHandle = byHandle;
        list.SetValue(InstanceProperty, this);
        // Preview events, so a press on a button or check box inside an entry (which handle it) can still start a drag.
        list.PreviewMouseLeftButtonDown += OnPress;
        list.PreviewMouseMove += OnPreviewMove;
        list.PreviewMouseLeftButtonUp += (_, _) => _press = null;
        // Bubbling, so a drop-down in an entry keeps Alt+Down for opening itself.
        list.KeyDown += OnKeyDown;
    }

    /// <summary>Lets the items of a ListBox (or another ItemsControl) be dragged into order.</summary>
    /// <param name="moved">
    /// Called with where the entry was and where it ends up: take it out at <c>from</c>, then insert it at <c>to</c>.
    /// </param>
    /// <param name="byHandle">Drag only by an element marked <see cref="IsHandleProperty"/>, not anywhere in the entry.</param>
    public static DragReorder Attach(ItemsControl list, Action<int, int> moved, bool byHandle = false) => new(list, moved, byHandle);

    /// <summary>Lets the children of a panel (a stack of cards, say) be dragged into order.</summary>
    /// <inheritdoc cref="Attach(ItemsControl, Action{int, int}, bool)" />
    public static DragReorder Attach(Panel panel, Action<int, int> moved, bool byHandle = false) => new(panel, moved, byHandle);

    /// <summary>Whether an entry is being dragged.</summary>
    internal bool IsDragging => _drag is not null;

    /// <summary>
    /// Moves the entry an element is in up (-1) or down (+1) a place in its list, as Alt+Up and Alt+Down do.
    /// </summary>
    /// <returns>False when the element isn't in a reorderable list, or the entry is already at that end.</returns>
    internal static bool Move(DependencyObject element, int step)
    {
        for (var e = element; e is not null; e = ParentOf(e))
        {
            if (e.GetValue(InstanceProperty) is DragReorder reorder)
                return reorder.MoveBy(element, step);
        }
        return false;
    }

    // ---- Where things are (no UI, so they can be tested) -----------------------------------------

    /// <summary>Where an entry is in its list, by its index and its top and bottom edges.</summary>
    internal readonly record struct Place(int Index, double Top, double Bottom);

    /// <summary>
    /// The gap between entries that a pointer at <paramref name="y"/> points to, by the index of the entry after it
    /// (or the number of entries, below the last). Above an entry's middle is the gap before it, below is the gap after.
    /// </summary>
    /// <param name="places">The entries shown, top to bottom; a list may leave out ones it hasn't drawn.</param>
    internal static int GapAt(IReadOnlyList<Place> places, double y)
    {
        foreach (var place in places)
        {
            if (y < (place.Top + place.Bottom) / 2)
                return place.Index;
        }
        return places.Count == 0 ? 0 : places[^1].Index + 1;
    }

    /// <summary>
    /// Where an entry dragged from <paramref name="from"/> ends up when dropped in <paramref name="gap"/>. It comes out of
    /// the list first, so the gaps below it are one place higher, and the gaps either side of it leave it where it was.
    /// </summary>
    internal static int DropIndex(int from, int gap) => gap > from ? gap - 1 : gap;

    /// <summary>Where the line for a gap goes: midway between the entries either side, or at the edge of the first or last.</summary>
    internal static double LineAt(IReadOnlyList<Place> places, int gap)
    {
        Place? before = null;
        Place? after = null;
        foreach (var place in places)
        {
            if (place.Index < gap)
            {
                before = place;
            }
            else
            {
                after = place;
                break;
            }
        }
        return (before, after) switch
        {
            ({ } b, { } a) => (b.Bottom + a.Top) / 2,
            (null, { } a) => a.Top,
            ({ } b, null) => b.Bottom,
            _ => 0,
        };
    }

    /// <summary>
    /// How far to scroll each tick with the pointer at <paramref name="y"/> in a scrolled area <paramref name="height"/>
    /// high: up (negative) or down, faster the nearer the pointer is to the edge or the further past it, and not at all
    /// away from the edges.
    /// </summary>
    internal static double ScrollStep(double y, double height)
    {
        var zone = Math.Min(EdgeZone, height / 4);
        if (zone <= 0)
            return 0;
        if (y < zone)
            return -MaxScrollStep * Math.Min(1, (zone - y) / zone);
        if (y > height - zone)
            return MaxScrollStep * Math.Min(1, (y - (height - zone)) / zone);
        return 0;
    }

    /// <summary>Whether the mouse has moved far enough with the button down to start a drag, rather than still being a click.</summary>
    internal static bool PastThreshold(Vector moved) =>
        Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance || Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance;

    /// <summary>Alt+Up moves an entry up a place (-1) and Alt+Down down one (+1); other keys, 0.</summary>
    /// <param name="key">The event's key: <see cref="Key.System"/> for a key pressed with Alt, which is then <paramref name="systemKey"/>.</param>
    internal static int KeyStep(Key key, Key systemKey, ModifierKeys modifiers) =>
        modifiers != ModifierKeys.Alt ? 0 : (key == Key.System ? systemKey : key) switch
        {
            Key.Up => -1,
            Key.Down => 1,
            _ => 0,
        };

    // ---- Entries ----------------------------------------------------------------------------------

    private int Count => _list switch
    {
        ItemsControl items => items.Items.Count,
        Panel panel => panel.Children.Count,
        _ => 0,
    };

    /// <summary>The entry at an index, or null when a virtualising list hasn't made one for it (it's scrolled away).</summary>
    private FrameworkElement? EntryAt(int index) => _list switch
    {
        ItemsControl items => items.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement,
        Panel panel => panel.Children[index] as FrameworkElement,
        _ => null,
    };

    /// <summary>
    /// What's at an index: the item, for an ItemsControl (a virtualising list can let go of an item's entry, or give it
    /// to another item, once it's scrolled away), or the entry itself, for a panel.
    /// </summary>
    private object? ItemAt(int index) => _list switch
    {
        ItemsControl items => items.Items[index],
        Panel panel => panel.Children[index],
        _ => null,
    };

    /// <summary>The entry's place in the list, or -1 once it's no longer in it (the list was rebuilt).</summary>
    private int IndexOf(FrameworkElement entry) => _list switch
    {
        ItemsControl items => items.ItemContainerGenerator.IndexFromContainer(entry),
        Panel panel => panel.Children.IndexOf(entry),
        _ => -1,
    };

    /// <summary>The entry an element is in, or null when it isn't in one (a scroll bar, say).</summary>
    private FrameworkElement? EntryOf(DependencyObject element)
    {
        if (_list is ItemsControl items)
            return ItemsControl.ContainerFromElement(items, element) as FrameworkElement;
        for (var e = element; e is not null; e = ParentOf(e))
        {
            if (ParentOf(e) == _list)
                return e as FrameworkElement;
        }
        return null;
    }

    /// <summary>
    /// The entry a press on an element can drag: the one it's in, but in a list dragged by handles only from the handle,
    /// so the entry's text boxes and buttons keep the mouse. Null anywhere else.
    /// </summary>
    internal FrameworkElement? DraggableEntryOf(DependencyObject element) =>
        EntryOf(element) is { } entry && (!_byHandle || OnHandle(element, entry)) ? entry : null;

    /// <summary>Whether a press on an element is on its entry's handle.</summary>
    private static bool OnHandle(DependencyObject element, FrameworkElement entry)
    {
        for (var e = element; e is not null && e != entry; e = ParentOf(e))
        {
            if (GetIsHandle(e))
                return true;
        }
        return GetIsHandle(entry);
    }

    /// <summary>Where each entry shown is, top to bottom, in the list's own coordinates.</summary>
    private List<Place> Places()
    {
        var places = new List<Place>();
        for (var i = 0; i < Count; i++)
        {
            if (EntryAt(i) is not { IsVisible: true } entry)
                continue;
            var top = entry.TranslatePoint(new Point(), _list).Y;
            places.Add(new Place(i, top, top + entry.ActualHeight));
        }
        return places;
    }

    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    // ---- Keyboard ---------------------------------------------------------------------------------

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var step = KeyStep(e.Key, e.SystemKey, Keyboard.Modifiers);
        if (step != 0 && _drag is null && e.OriginalSource is DependencyObject source && MoveBy(source, step))
            e.Handled = true;
    }

    private bool MoveBy(DependencyObject element, int step)
    {
        if (EntryOf(element) is not { } entry)
            return false;
        var from = IndexOf(entry);
        var to = from + step;
        if (from < 0 || to < 0 || to >= Count)
            return false;
        _moved(from, to);
        return true;
    }

    // ---- Dragging ---------------------------------------------------------------------------------

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _press = null;
        if (_drag is null && e.ClickCount <= 1 && e.OriginalSource is DependencyObject source && DraggableEntryOf(source) is { } entry)
            _press = new Press(entry, e.GetPosition(_list));
    }

    private void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (_press is not { } press || _drag is not null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _press = null;
            return;
        }

        if (!PastThreshold(e.GetPosition(_list) - press.Start))
            return;
        _press = null;
        if (Begin(press.Entry))
            e.Handled = true;
    }

    /// <summary>Starts dragging an entry, once the mouse has moved far enough with it pressed.</summary>
    /// <returns>False when it can't: the entry has gone, or there's nowhere to show the line or no mouse to hold.</returns>
    internal bool Begin(FrameworkElement entry)
    {
        var from = IndexOf(entry);
        if (from < 0 || AdornerLayer.GetAdornerLayer(_list) is not { } layer)
            return false;

        // Across the entry, measured now: a virtualising list can let go of the entry once it's scrolled away.
        var left = entry.TranslatePoint(new Point(), _list).X;
        var line = new InsertionLine(_list, left, left + entry.ActualWidth) { Cursor = Cursors.SizeNS };
        layer.Add(line);
        line.SetResourceReference(InsertionLine.BrushProperty, "AccentFillColorDefaultBrush");
        // The line holds the mouse for the drag, so the list doesn't also take the drag as selecting what it passes over.
        if (!line.CaptureMouse())
        {
            layer.Remove(line);
            return false;
        }

        var keys = Window.GetWindow(_list) as UIElement ?? _list;
        // The entry is dimmed while it's dragged, unless it has an opacity of its own to keep.
        var dim = entry.ReadLocalValue(UIElement.OpacityProperty) == DependencyProperty.UnsetValue;
        var drag = new Drag(entry, from, ItemAt(from), layer, line, keys, dim, OwnScroller(), OuterScroller());
        _drag = drag;
        line.MouseMove += (_, e) => Update(e.GetPosition(_list));
        line.MouseLeftButtonUp += (_, _) => End(drop: true);
        line.LostMouseCapture += (_, _) => End(drop: false);
        keys.PreviewKeyDown += OnDragKey;
        // Only while dragging, so a list nobody is dragging costs nothing.
        drag.Timer = new DispatcherTimer(ScrollTick, DispatcherPriority.Input, (_, _) => AutoScroll(), _list.Dispatcher);
        if (dim)
            entry.Opacity = 0.5;
        Update(Mouse.GetPosition(_list));
        return true;
    }

    /// <summary>
    /// Moves the line to the gap under the pointer, or hides it when the pointer is off to the side (a drop there cancels).
    /// Above or below the part of the list in view (while it scrolls, say), the pointer counts as at that edge, so the
    /// entry lands where the line shows rather than among entries scrolled out of sight.
    /// </summary>
    internal void Update(Point at)
    {
        if (_drag is not { } drag)
            return;
        var places = Places();
        var (top, bottom) = Shown(drag);
        var over = at.X >= 0 && at.X <= _list.ActualWidth && places.Count > 0 && top < bottom;
        drag.Gap = over ? GapAt(places, Math.Clamp(at.Y, top, bottom)) : -1;
        if (drag.Gap < 0)
        {
            drag.Line.Hide();
            return;
        }
        drag.Line.Show(LineAt(places, drag.Gap), top, bottom);
    }

    /// <summary>
    /// The part of the list in view, top to bottom in its own coordinates: inside its own scroll viewer and the one it's
    /// in, so not the entries scrolled out of sight (or made ready just out of sight, by a virtualising list).
    /// </summary>
    private (double Top, double Bottom) Shown(Drag drag)
    {
        var (top, bottom) = (0.0, _list.ActualHeight);
        ReadOnlySpan<ScrollViewer?> viewers = [drag.Own, drag.Outer];
        foreach (var viewer in viewers)
        {
            if (viewer is null)
                continue;
            var y = viewer.TranslatePoint(new Point(), _list).Y;
            (top, bottom) = (Math.Max(top, y), Math.Min(bottom, y + viewer.ActualHeight));
        }
        return (top, bottom);
    }

    private void AutoScroll()
    {
        if (_drag is not { } drag)
            return;
        var at = Mouse.GetPosition(_list);
        Update(at);

        // The list's own scroll bar when it has one in use, else the one around it (a list in a scrolling page).
        var viewer = drag.Own is { ScrollableHeight: > 0 } own ? own : drag.Outer;
        if (viewer is null || at.X < 0 || at.X > _list.ActualWidth)
            return;
        var step = ScrollStep(Mouse.GetPosition(viewer).Y, viewer.ActualHeight);
        if (step == 0)
            return;
        // A list in a longer page scrolls the page only until the list's end is in view, not on past it out of sight.
        if (viewer == drag.Outer)
        {
            var listTop = _list.TranslatePoint(new Point(), viewer).Y;
            if (step < 0 ? listTop >= 0 : listTop + _list.ActualHeight <= viewer.ActualHeight)
                return;
        }
        if (!viewer.CanContentScroll)
        {
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset + step);
            return;
        }

        // Scrolling by whole entries rather than pixels: a line at a time, once the steps add up to an entry's height.
        drag.Carry += step;
        var entryHeight = Math.Max(1, drag.Entry.ActualHeight);
        if (drag.Carry <= -entryHeight)
            viewer.LineUp();
        else if (drag.Carry >= entryHeight)
            viewer.LineDown();
        else
            return;
        drag.Carry = 0;
    }

    private void OnDragKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        End(drop: false);
    }

    /// <summary>Drops the entry where the line shows (unless it's hidden), or with <paramref name="drop"/> false, cancels.</summary>
    internal void End(bool drop)
    {
        if (_drag is not { } drag)
            return;
        _drag = null;
        drag.Timer?.Stop();
        drag.Keys.PreviewKeyDown -= OnDragKey;
        drag.Line.ReleaseMouseCapture();
        drag.Layer.Remove(drag.Line);
        if (drag.Dimmed)
            drag.Entry.ClearValue(UIElement.OpacityProperty);

        // If the list changed under the drag (an undo, say), its indexes no longer mean the same entries. Checked by item,
        // not by entry, since a virtualising list can let go of the entry while it's scrolled away.
        if (!drop || drag.Gap < 0 || drag.From >= Count || !Equals(ItemAt(drag.From), drag.Item))
            return;
        var to = DropIndex(drag.From, drag.Gap);
        if (to != drag.From)
            _moved(drag.From, to);
    }

    /// <summary>A list's own scroll viewer, inside its template.</summary>
    private ScrollViewer? OwnScroller() => _list is ItemsControl ? Descendant<ScrollViewer>(_list) : null;

    /// <summary>The scroll viewer the list is in, if any.</summary>
    private ScrollViewer? OuterScroller()
    {
        for (var e = VisualTreeHelper.GetParent(_list); e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is ScrollViewer viewer)
                return viewer;
        }
        return null;
    }

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if ((child as T ?? Descendant<T>(child)) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>The mouse went down on an entry; a drag starts if it moves far enough before coming up.</summary>
    private sealed record Press(FrameworkElement Entry, Point Start);

    /// <param name="Item">What's dragged, as <see cref="ItemAt"/> gives it, to check it's still there when it's dropped.</param>
    /// <param name="Keys">Where Esc is listened for: the whole window, wherever focus is.</param>
    /// <param name="Dimmed">Whether the entry was dimmed, so its opacity goes back afterwards.</param>
    /// <param name="Own">The list's own scroll viewer, if it has one.</param>
    /// <param name="Outer">The scroll viewer the list is in, if any.</param>
    private sealed record Drag(
        FrameworkElement Entry, int From, object? Item, AdornerLayer Layer, InsertionLine Line, UIElement Keys, bool Dimmed, ScrollViewer? Own, ScrollViewer? Outer)
    {
        public DispatcherTimer? Timer { get; set; }

        /// <summary>The gap the entry would be dropped in, or -1 for none (the pointer is off to the side).</summary>
        public int Gap { get; set; } = -1;

        /// <summary>Scrolling built up towards the next whole entry, in a list that scrolls by entries.</summary>
        public double Carry { get; set; }
    }

    /// <summary>
    /// The line showing where a dragged entry will land, in the theme's accent colour, across the entry from
    /// <paramref name="left"/> to <paramref name="right"/>.
    /// </summary>
    private sealed class InsertionLine(UIElement list, double left, double right) : Adorner(list)
    {
        public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
            nameof(Brush), typeof(Brush), typeof(InsertionLine), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private const double Thickness = 2;
        private const double Dot = 3;

        private double? _y;
        private double _top;
        private double _bottom;

        public Brush? Brush
        {
            get => (Brush?)GetValue(BrushProperty);
            set => SetValue(BrushProperty, value);
        }

        /// <summary>Shows the line at <paramref name="y"/>, kept between <paramref name="top"/> and <paramref name="bottom"/>: the part of the list in view.</summary>
        public void Show(double y, double top, double bottom)
        {
            (_y, _top, _bottom) = (y, top, bottom);
            InvalidateVisual();
        }

        public void Hide()
        {
            _y = null;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            if (_y is not { } y || Brush is not { } brush)
                return;
            // Kept inside the part of the list in view, so a line above the first entry there or below the last isn't cut
            // in half, or drawn out of sight.
            y = Math.Clamp(y, _top + Dot, Math.Max(_top + Dot, _bottom - Dot));
            drawing.DrawEllipse(brush, null, new Point(left + Dot, y), Dot, Dot);
            drawing.DrawRoundedRectangle(brush, null, new Rect(left + Dot, y - Thickness / 2, Math.Max(0, right - left - Dot), Thickness), 1, 1);
        }
    }
}
