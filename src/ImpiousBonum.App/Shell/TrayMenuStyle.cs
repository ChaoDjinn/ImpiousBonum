using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImpiousBonum.App.Shell;

/// <summary>The tray menu's colours, dark or light with the taskbar.</summary>
public sealed record TrayMenuPalette(Color Background, Color Border, Color Text, Color SecondaryText, Color DisabledText, Color Hover, Color Separator, Color Accent)
{
    /// <summary>Like Windows 11's own dark menus, with the app's orange for checks.</summary>
    public static readonly TrayMenuPalette Dark = new(
        Background: Color.FromArgb(0x2C, 0x2C, 0x2C),
        Border: Color.FromArgb(0x45, 0x45, 0x45),
        Text: Color.FromArgb(0xF3, 0xF3, 0xF3),
        SecondaryText: Color.FromArgb(0xA8, 0xA8, 0xA8),
        DisabledText: Color.FromArgb(0x86, 0x86, 0x86),
        Hover: Color.FromArgb(0x3D, 0x3D, 0x3D),
        Separator: Color.FromArgb(0x40, 0x40, 0x40),
        Accent: Color.FromArgb(0xFF, 0xAA, 0x00));

    /// <summary>Windows 11's light menus; the orange is darker so checks stand out on white.</summary>
    public static readonly TrayMenuPalette Light = new(
        Background: Color.FromArgb(0xF9, 0xF9, 0xF9),
        Border: Color.FromArgb(0xD9, 0xD9, 0xD9),
        Text: Color.FromArgb(0x1A, 0x1A, 0x1A),
        SecondaryText: Color.FromArgb(0x5F, 0x5F, 0x5F),
        DisabledText: Color.FromArgb(0x8F, 0x8F, 0x8F),
        Hover: Color.FromArgb(0xEA, 0xEA, 0xEA),
        Separator: Color.FromArgb(0xE0, 0xE0, 0xE0),
        Accent: Color.FromArgb(0xB8, 0x6E, 0x00));

    /// <summary>The palette for Windows' current mode.</summary>
    public static TrayMenuPalette Current => Override ?? (WindowsColourMode.IsSystemDark() ? Dark : Light);

    /// <summary>A fixed palette instead of following Windows, for tests and previews.</summary>
    internal static TrayMenuPalette? Override { get; set; }
}

/// <summary>
/// Gives the tray menu (and every submenu as it opens) a look of its own, like Windows 11's menus rather than Windows
/// Forms' default: dark or light with the taskbar, rounded corners and a soft shadow, roomy rows with a rounded
/// highlight, a line icon beside the main items, chevrons for submenus and check marks in the app's orange.
/// </summary>
public static class TrayMenuStyle
{
    /// <summary>Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets (the same code points) on Windows 10.</summary>
    private static readonly string IconFont = FontInstalled("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    private static readonly TrayMenuRenderer Renderer = new();
    // Weak, so submenus rebuilt each time the menu opens aren't kept alive by having been styled.
    private static readonly ConditionalWeakTable<ToolStripDropDown, object> Styled = [];
    private static readonly ConditionalWeakTable<ToolStripItem, StrongBox<char>> Glyphs = [];

    // Which way each submenu opened (true: to the left of its menu), and the items whose side is already chosen for them.
    private static readonly ConditionalWeakTable<ToolStripDropDown, StrongBox<bool>> OpenedLeft = [];
    private static readonly ConditionalWeakTable<ToolStripDropDownItem, object> Sided = [];
    private static readonly Dictionary<(char Glyph, Color Color, int Size), Bitmap> GlyphImages = [];

    /// <summary>Common glyphs, by what they show.</summary>
    public static class Icons
    {
        public const char Edit = '';
        public const char Layout = '';
        public const char Display = '';
        public const char Sensors = '';
        public const char Weather = '';
        public const char Calendar = '';
        public const char News = '';
        public const char Chat = '';
        public const char Globe = '';
        public const char Bell = '';
        public const char Keyboard = '';
        public const char History = '';
        public const char Sync = '';
        public const char Help = '';
        public const char Power = '';
        public const char Restart = '';
        public const char Check = '';
        public const char Chevron = '';
    }

    /// <summary>Styles the menu now and each submenu when it first opens, following Windows' light or dark mode each time.</summary>
    public static void Attach(ToolStripDropDown menu)
    {
        if (!Styled.TryAdd(menu, menu))
            return;
        menu.Renderer = Renderer;
        menu.DropShadowEnabled = true;
        menu.Opening += (_, _) => Restyle(menu);
        menu.HandleCreated += (_, _) => RoundCorners(menu);
        if (menu.IsHandleCreated)
            RoundCorners(menu);
        Restyle(menu);
    }

    /// <summary>A line icon beside <paramref name="item"/>, drawn in the menu's text colour.</summary>
    public static T WithIcon<T>(this T item, char glyph) where T : ToolStripItem
    {
        Glyphs.AddOrUpdate(item, new StrongBox<char>(glyph));
        return item;
    }

    private static void Restyle(ToolStripDropDown menu)
    {
        var palette = TrayMenuPalette.Current;
        Renderer.Palette = palette;
        menu.BackColor = palette.Background;
        menu.ForeColor = palette.Text;
        var dpi = menu.DeviceDpi / 96f;
        int Scale(int value) => (int)Math.Round(value * dpi);
        menu.Padding = new Padding(Scale(4), Scale(4), Scale(4), Scale(4));
        menu.ImageScalingSize = new Size(Scale(16), Scale(16));

        foreach (ToolStripItem item in menu.Items)
        {
            if (item is ToolStripSeparator separator)
            {
                separator.Margin = new Padding(0, Scale(3), 0, Scale(3));
                continue;
            }
            item.Padding = new Padding(0, Scale(5), Scale(8), Scale(5));
            if (Glyphs.TryGetValue(item, out var glyph))
            {
                item.Image = GlyphImage(glyph.Value, palette.Text, Scale(16));
                item.ImageScaling = ToolStripItemImageScaling.None;
            }
            if (item is ToolStripMenuItem { HasDropDown: true } parent)
            {
                Attach(parent.DropDown);
                if (Sided.TryAdd(parent, parent))
                    parent.DropDownOpening += (_, _) => ChooseSide(parent);
            }
        }
        if (menu.IsHandleCreated)
            RoundCorners(menu);
    }

    /// <summary>
    /// Opens a submenu beside its menu on the same screen, carrying on the way the menus already go: from a tray at the
    /// right of the screen, each opens to the left of the last. Windows Forms opens to the right whenever that fits on
    /// any screen, so with a monitor to the right submenus landed on that one, and without one the next level opened
    /// over the main menu, where moving the mouse across it closed them all.
    /// </summary>
    private static void ChooseSide(ToolStripDropDownItem item)
    {
        if (item.Owner is not ToolStripDropDown menu)
            return;
        var area = Screen.FromRectangle(menu.Bounds).WorkingArea;
        var left = Side(menu.Bounds, area, item.DropDown.GetPreferredSize(Size.Empty).Width,
            OpenedLeft.TryGetValue(menu, out var openedLeft) ? openedLeft.Value : null);
        item.DropDownDirection = left ? ToolStripDropDownDirection.Left : ToolStripDropDownDirection.Right;
        OpenedLeft.AddOrUpdate(item.DropDown, new StrongBox<bool>(left));
    }

    /// <summary>
    /// True to open a submenu <paramref name="width"/> wide to the left of <paramref name="menu"/>, within the screen's
    /// working <paramref name="area"/>. It keeps going the way the menu itself opened (<paramref name="menuOpenedLeft"/>;
    /// for the first menu, towards the side with more room) while that fits, and otherwise turns to the roomier side.
    /// </summary>
    internal static bool Side(Rectangle menu, Rectangle area, int width, bool? menuOpenedLeft)
    {
        var roomLeft = menu.Left - area.Left;
        var roomRight = area.Right - menu.Right;
        var goLeft = menuOpenedLeft ?? roomLeft > roomRight;
        var room = goLeft ? roomLeft : roomRight;
        return width <= room ? goLeft : roomLeft > roomRight;
    }

    /// <summary>A glyph from the icon font as a picture, made once for each colour and size.</summary>
    private static Bitmap GlyphImage(char glyph, Color color, int size)
    {
        if (GlyphImages.TryGetValue((glyph, color, size), out var cached))
            return cached;
        var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        using (var font = new Font(IconFont, size * 0.75f, GraphicsUnit.Pixel))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            TextRenderer.DrawText(g, glyph.ToString(), font, new Rectangle(0, 0, size, size), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        GlyphImages[(glyph, color, size)] = bitmap;
        return bitmap;
    }

    /// <summary>Windows 11 rounds the menu's corners and draws its border; earlier Windows ignore this and the renderer draws a square one.</summary>
    private static void RoundCorners(ToolStripDropDown menu)
    {
        var round = DwmWindowCornerRound;
        var corners = DwmSetWindowAttribute(menu.Handle, DwmWindowCornerPreference, ref round, sizeof(int)) == 0;
        Renderer.DrawsBorder = !corners;
        if (!corners)
            return;
        var border = ColorTranslator.ToWin32(Renderer.Palette.Border);
        DwmSetWindowAttribute(menu.Handle, DwmBorderColor, ref border, sizeof(int));
    }

    private static bool FontInstalled(string name)
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Draws the items in the palette's colours; sizes and positions are Windows Forms' own.</summary>
    private sealed class TrayMenuRenderer : ToolStripRenderer
    {
        public TrayMenuPalette Palette { get; set; } = TrayMenuPalette.Dark;

        public bool DrawsBorder { get; set; } = true;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Palette.Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (!DrawsBorder)
                return;
            using var pen = new Pen(Palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Same as the background: icons and checks sit on it without a column of their own.
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
                return;
            var scale = Dpi(e.Item) / 96f;
            var bounds = new RectangleF(scale, scale * 0.5f, e.Item.Width - 2 * scale, e.Item.Height - scale);
            using var path = RoundedRectangle(bounds, 4 * scale);
            using var brush = new SolidBrush(Palette.Hover);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(Palette.Separator);
            e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // The shortcut column holds secondary details, like the version beside the app's name.
            var secondary = e.Item is ToolStripMenuItem { ShortcutKeyDisplayString: { } detail } && e.Text == detail;
            var color = secondary ? Palette.SecondaryText
                : e.Item.Enabled || e.Item.Tag is HeaderTag ? Palette.Text
                : Palette.DisabledText;
            // Drawn here rather than by the base renderer, which greys out disabled items its own way.
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image is null)
                return;
            if (e.Item.Enabled || e.Item.Tag is HeaderTag)
            {
                e.Graphics.DrawImage(e.Image, e.ImageRectangle);
                return;
            }
            using var faded = new System.Drawing.Imaging.ImageAttributes();
            faded.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.45f });
            e.Graphics.DrawImage(e.Image, e.ImageRectangle, 0, 0, e.Image.Width, e.Image.Height, GraphicsUnit.Pixel, faded);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) =>
            DrawGlyph(e.Graphics, Icons.Check, e.ImageRectangle, e.Item.Enabled ? Palette.Accent : Palette.DisabledText, Dpi(e.Item));

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            var color = e.Item?.Enabled == false ? Palette.DisabledText : Palette.SecondaryText;
            DrawGlyph(e.Graphics, Icons.Chevron, e.ArrowRectangle, color, e.Item is null ? 96 : Dpi(e.Item), size: 10);
        }

        private static int Dpi(ToolStripItem item) => item.Owner?.DeviceDpi ?? 96;

        private static void DrawGlyph(Graphics g, char glyph, Rectangle bounds, Color color, int dpi, float size = 12)
        {
            using var font = new Font(IconFont, size * dpi / 96f, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, glyph.ToString(), font, bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static GraphicsPath RoundedRectangle(RectangleF r, float radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Marks the menu's header (the app's name and version): disabled, but drawn as plain text, not greyed.</summary>
    public sealed class HeaderTag
    {
        public static readonly HeaderTag Instance = new();

        private HeaderTag()
        {
        }
    }

    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmWindowCornerRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
