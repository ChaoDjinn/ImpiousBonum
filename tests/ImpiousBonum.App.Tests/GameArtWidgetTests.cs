using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class GameArtWidgetTests
{
    private static byte[] Jpeg(int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, new byte[width * height * 4], width * 4);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>A 16-pixel icon picture in the older bitmap form, behind an .ico header, as the game lookup gives one.</summary>
    private static byte[] BitmapIcon()
    {
        const int Size = 16;
        var file = new MemoryStream();
        var writer = new BinaryWriter(file);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        var picture = 40 + Size * Size * 4 + Size * 4;
        writer.Write(new byte[] { Size, Size, 0, 0 });
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(picture);
        writer.Write(22);
        // The bitmap's header, twice as tall for the mask under the colours; then the colours and the mask.
        writer.Write(40);
        writer.Write(Size);
        writer.Write(Size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(new byte[24]);
        writer.Write(Enumerable.Repeat((byte)0xFF, Size * Size * 4).ToArray());
        writer.Write(new byte[Size * 4]);
        return file.ToArray();
    }

    private static Widget Image(JsonObject settings, double width = 200, double height = 300)
    {
        settings["type"] = "image";
        settings["source"] ??= "game";
        var widget = WidgetFactory.Create(settings, Theme.From(new ThemeSettings()));
        widget.Width = width;
        widget.Height = height;
        widget.Measure(new Size(width, height));
        widget.Arrange(new Rect(0, 0, width, height));
        widget.UpdateLayout();
        return widget;
    }

    private static Border Picture(Widget widget) => widget.Children.OfType<Border>().First();

    private static MetricStore Playing(GameArt? art)
    {
        var store = new MetricStore();
        store.SetAttachment(GameArtProvider.Art, art);
        return store;
    }

    [Fact]
    public void The_game_s_picture_shows_and_goes_with_the_game()
    {
        Sta.Run(() =>
        {
            var widget = Image(new JsonObject());
            var store = Playing(new GameArt(Jpeg(60, 90), Jpeg(92, 43), Jpeg(192, 62), null));

            widget.Refresh(store, DateTime.Now);
            var brush = Assert.IsType<ImageBrush>(Picture(widget).Background);
            Assert.Equal(Visibility.Visible, widget.Visibility);

            // The same game a second later keeps its picture rather than decoding it again.
            widget.Refresh(store, DateTime.Now);
            Assert.Same(brush, Picture(widget).Background);

            store.SetAttachment(GameArtProvider.Art, null);
            widget.Refresh(store, DateTime.Now);
            Assert.Null(Picture(widget).Background);
            Assert.Equal(Visibility.Visible, widget.Visibility);
        });
    }

    [Theory]
    [InlineData("cover", 60, 90)]
    [InlineData("header", 92, 43)]
    [InlineData("hero", 192, 62)]
    public void It_shows_the_picture_asked_for(string art, int width, int height)
    {
        Sta.Run(() =>
        {
            var widget = Image(new JsonObject { ["art"] = art, ["fit"] = "fit" }, 400, 400);
            widget.Refresh(Playing(new GameArt(Jpeg(60, 90), Jpeg(92, 43), Jpeg(192, 62), null)), DateTime.Now);

            var bitmap = (BitmapSource)((ImageBrush)Picture(widget).Background).ImageSource;
            Assert.Equal((width, height), (bitmap.PixelWidth, bitmap.PixelHeight));
        });
    }

    [Fact]
    public void A_filled_picture_is_cropped_to_the_widget_s_shape_and_no_more()
    {
        Sta.Run(() =>
        {
            // A banner in a box of nearly its own shape: rounding the size up mustn't cut off its ends.
            var widget = Image(new JsonObject { ["art"] = "header", ["fit"] = "fill" }, 380, 178);
            widget.Refresh(Playing(new GameArt(null, Jpeg(920, 430), null, null)), DateTime.Now);

            var bitmap = (BitmapSource)((ImageBrush)Picture(widget).Background).ImageSource;
            Assert.Equal(380.0 / 178, (double)bitmap.PixelWidth / bitmap.PixelHeight, 0.02);
        });
    }

    [Fact]
    public void An_icon_is_shown_whole_whatever_the_fit()
    {
        Sta.Run(() =>
        {
            var widget = Image(new JsonObject { ["fit"] = "fill" });
            widget.Refresh(Playing(new GameArt(null, null, null, BitmapIcon())), DateTime.Now);

            var brush = Assert.IsType<ImageBrush>(Picture(widget).Background);
            Assert.Equal(Stretch.Uniform, brush.Stretch);
            Assert.Equal(16, ((BitmapSource)brush.ImageSource).PixelWidth);
        });
    }

    [Fact]
    public void It_can_hide_while_there_is_no_game_and_shows_faded_while_editing()
    {
        Sta.Run(() =>
        {
            var widget = Image(new JsonObject { ["hideNoGame"] = true });
            var hint = (TextBlock)widget.Children.OfType<Border>().Last().Child;
            Assert.Equal("No game in front", hint.Text);

            widget.Refresh(new MetricStore(), DateTime.Now);
            Assert.Equal(Visibility.Collapsed, widget.Visibility);
            widget.Editing = true;
            widget.Refresh(new MetricStore(), DateTime.Now);
            Assert.Equal(Visibility.Visible, widget.Visibility);
            Assert.True(widget.Opacity < 1);

            widget.Editing = false;
            widget.Refresh(Playing(new GameArt(Jpeg(60, 90), null, null, null)), DateTime.Now);
            Assert.Equal(Visibility.Visible, widget.Visibility);
        });
    }

    [Fact]
    public void A_file_image_ignores_the_game()
    {
        Sta.Run(() =>
        {
            var widget = Image(new JsonObject { ["source"] = "file" });
            widget.Refresh(Playing(new GameArt(Jpeg(60, 90), null, null, null)), DateTime.Now);
            Assert.Null(Picture(widget).Background);
        });
    }

    [Fact]
    public void Pictures_that_aren_t_pictures_are_ignored() =>
        Sta.Run(() => Assert.Null(BackgroundImage.Load([1, 2, 3], "fill", 100, 100)));

    [Fact]
    public void The_demo_game_has_its_name_and_a_cover_header_and_hero()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, new DateTime(2026, 10, 11, 21, 47, 0));
        store.TryGet(GameArtProvider.Name, out var name);
        Assert.Equal("Starfall Odyssey", name.Text);
        store.TryGet(GameArtProvider.AppId, out var appId);
        Assert.Equal("3141590", appId.Text);

        Sta.Run(() =>
        {
            var art = DemoArtwork.Game();
            Assert.Null(art.Icon);
            foreach (var (picture, wide) in new[] { (art.Cover!, false), (art.Header!, true), (art.Hero!, true) })
            {
                var decoded = BackgroundImage.Load(picture, "fit", 2000, 2000)!;
                Assert.Equal(wide, decoded.PixelWidth > decoded.PixelHeight);
            }
        });
    }
}
