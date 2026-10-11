using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class ImageAndShapeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public ImageAndShapeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Png(string name, int size = 8)
    {
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)0xFF, size * size * 4).ToArray(), size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(_root, name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private static T Show<T>(Widget widget) where T : FrameworkElement
    {
        widget.Width = 200;
        widget.Height = 100;
        widget.Measure(new Size(200, 100));
        widget.Arrange(new Rect(0, 0, 200, 100));
        widget.UpdateLayout();
        return widget.Children.OfType<T>().First();
    }

    [Fact]
    public void An_image_shows_its_picture_and_can_be_recoloured()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var file = Png("logo.png");

            var plain = Show<Border>(WidgetFactory.Create(new JsonObject { ["type"] = "image", ["file"] = file }, theme));
            Assert.IsType<ImageBrush>(plain.Background);
            Assert.Null(plain.OpacityMask);

            var recoloured = Show<Border>(WidgetFactory.Create(new JsonObject { ["type"] = "image", ["file"] = file, ["recolor"] = true, ["color"] = "warning" }, theme));
            Assert.Same(theme.Warning, recoloured.Background);
            Assert.IsType<ImageBrush>(recoloured.OpacityMask);
        });
    }

    [Fact]
    public void A_missing_picture_only_shows_a_hint_while_editing()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "image", ["file"] = Path.Combine(_root, "gone.png") }, Theme.From(new ThemeSettings()));
            Show<Border>(widget);
            var hint = widget.Children.OfType<Border>().Last();

            widget.Refresh(new MetricStore(), DateTime.Now);
            Assert.Equal(Visibility.Collapsed, hint.Visibility);
            widget.Editing = true;
            widget.Refresh(new MetricStore(), DateTime.Now);
            Assert.Equal(Visibility.Visible, hint.Visibility);
        });
    }

    [Fact]
    public void Shapes_use_theme_colours_and_keep_their_opacity_while_editing()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var panel = WidgetFactory.Create(new JsonObject { ["type"] = "shape", ["opacity"] = 0.4 }, theme);
            var border = Show<Border>(panel);
            Assert.Same(theme.Track, border.Background);
            panel.Editing = true;
            Assert.Equal(0.4, border.Opacity);

            var circle = Show<Ellipse>(WidgetFactory.Create(new JsonObject { ["type"] = "shape", ["shape"] = "circle", ["color"] = "background" }, theme));
            Assert.Same(theme.Background, circle.Fill);
        });
    }

    [Fact]
    public void Shapes_are_added_behind_everything_else()
    {
        var session = new LayoutSession(new LayoutDocument { Widgets = [new JsonObject { ["type"] = "text" }] }, new ThemeLibrary(null));
        session.AddWidget(ShapeWidget.Descriptor);
        Assert.Equal("shape", session.Document.Widgets[0].GetString("type"));
        Assert.Equal(0, session.SelectedIndex);

        session.AddWidget(ImageWidget.Descriptor);
        Assert.Equal("image", session.Document.Widgets[^1].GetString("type"));
    }

    [Fact]
    public void Shared_layouts_carry_their_image_widgets_pictures()
    {
        var logo = Png("logo.png");
        var layout = new LayoutDocument
        {
            Widgets =
            [
                new JsonObject { ["type"] = "image", ["file"] = logo },
                new JsonObject { ["type"] = "text" },
                new JsonObject { ["type"] = "image", ["file"] = Path.Combine(_root, "missing.png") },
            ],
        };

        using var stream = new MemoryStream();
        var warnings = LayoutPackage.Write(layout, "Logos", stream);
        Assert.Single(warnings);
        Assert.Equal(logo, layout.Widgets[0].GetString("file"));
        stream.Position = 0;

        var package = LayoutPackage.Read(stream, "Logos");
        Assert.Equal([0], package.Images.Keys);
        Assert.Null(package.Layout.Widgets[0].GetString("file"));

        var library = new LayoutLibrary(Path.Combine(_root, "other PC"));
        library.EnsureSeeded();
        library.Import(package, "Logos");
        var imported = library.Load("Logos").Widgets[0].GetString("file")!;
        Assert.True(File.Exists(imported));
        Assert.StartsWith(Path.Combine(library.Directory, "Logos"), imported);
        Assert.Equal(File.ReadAllBytes(logo), File.ReadAllBytes(imported));

        // Still in use, so the cleanup leaves it.
        library.DeleteUnusedImportFolders();
        Assert.True(File.Exists(imported));
    }
}
