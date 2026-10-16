using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

/// <summary>The photo frame: the order photos come in, finding and decoding them, the slideshow, and leaving the folder out of shared layouts.</summary>
public sealed class PhotoFrameTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 16, 21, 47, 0);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public PhotoFrameTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ---- The order -----------------------------------------------------------------------------------

    private static PhotoFile Photo(string name, int day = 1) => new(@"C:\Photos\" + name, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));

    private static List<string> Shown(PhotoAlbum album, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => Path.GetFileName(album.Advance()!.Path))];

    [Fact]
    public void By_name_goes_from_a_to_z_as_explorer_sorts_and_comes_round_again()
    {
        var album = new PhotoAlbum(PhotoAlbum.ByName);
        album.Update([Photo("img10.jpg"), Photo("IMG2.jpg"), Photo("beach.jpg")]);
        Assert.Equal(["beach.jpg", "IMG2.jpg", "img10.jpg", "beach.jpg"], Shown(album, 4));
    }

    [Fact]
    public void By_date_goes_from_oldest_to_newest()
    {
        var album = new PhotoAlbum(PhotoAlbum.ByDate);
        album.Update([Photo("a.jpg", day: 3), Photo("b.jpg", day: 1), Photo("c.jpg", day: 2)]);
        Assert.Equal(["b.jpg", "c.jpg", "a.jpg"], Shown(album, 3));
    }

    [Fact]
    public void A_shuffle_shows_every_photo_before_any_comes_round_again_and_never_one_twice_running()
    {
        var album = new PhotoAlbum(PhotoAlbum.Shuffle, new Random(5));
        album.Update([.. Enumerable.Range(1, 6).Select(i => Photo($"{i}.jpg"))]);

        var shown = Shown(album, 60);
        Assert.All(shown.Chunk(6), pass => Assert.Equal(6, pass.Distinct().Count()));
        Assert.All(shown.Zip(shown.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));
        Assert.True(shown.Chunk(6).Select(pass => string.Join(",", pass)).Distinct().Count() > 1, "Every time round was in the same order.");
    }

    [Fact]
    public void The_photo_to_decode_ahead_is_the_one_shown_next()
    {
        var album = new PhotoAlbum(PhotoAlbum.Shuffle, new Random(1));
        album.Update([Photo("a.jpg"), Photo("b.jpg"), Photo("c.jpg")]);
        for (var i = 0; i < 10; i++)
        {
            var next = album.Next;
            Assert.Equal(next, album.Next);
            Assert.Equal(next, album.Advance());
            Assert.Equal(next, album.Current);
        }
    }

    [Fact]
    public void Photos_that_cannot_be_shown_are_passed_over_until_their_file_changes()
    {
        var album = new PhotoAlbum(PhotoAlbum.ByName);
        album.Update([Photo("a.jpg"), Photo("b.jpg"), Photo("c.jpg")]);
        album.Fail(Photo("b.jpg"));
        Assert.Equal(["a.jpg", "c.jpg", "a.jpg", "c.jpg"], Shown(album, 4));

        // Copied in again, so it's a new file to the album and gets another go.
        album.Update([Photo("a.jpg"), Photo("b.jpg", day: 2), Photo("c.jpg")]);
        Assert.Equal(["a.jpg", "b.jpg"], Shown(album, 2));
    }

    [Fact]
    public void An_album_with_nothing_that_can_be_shown_is_empty_until_its_photos_get_another_go()
    {
        var album = new PhotoAlbum(PhotoAlbum.Shuffle);
        Assert.True(album.IsEmpty);
        album.Update([Photo("a.jpg")]);
        Assert.False(album.IsEmpty);

        album.Fail(Photo("a.jpg"));
        Assert.True(album.IsEmpty);
        Assert.Null(album.Advance());

        album.Retry();
        Assert.Equal(Photo("a.jpg"), album.Advance());
        // Only one: it stays on show.
        Assert.Equal(Photo("a.jpg"), album.Next);
    }

    [Fact]
    public void A_new_listing_keeps_the_photo_on_show_and_fits_new_ones_in_by_name()
    {
        var album = new PhotoAlbum(PhotoAlbum.ByName);
        album.Update([Photo("a.jpg"), Photo("c.jpg"), Photo("e.jpg")]);
        album.Advance();
        album.Advance();

        album.Update([Photo("a.jpg"), Photo("b.jpg"), Photo("c.jpg"), Photo("d.jpg")]);
        Assert.Equal(Photo("c.jpg"), album.Current);
        Assert.Equal(["d.jpg", "a.jpg", "b.jpg", "c.jpg"], Shown(album, 4));
    }

    [Fact]
    public void A_new_listing_in_a_shuffle_shows_new_photos_this_time_round_and_never_ones_that_have_gone()
    {
        var album = new PhotoAlbum(PhotoAlbum.Shuffle, new Random(3));
        List<PhotoFile> photos = [Photo("a.jpg"), Photo("b.jpg"), Photo("c.jpg"), Photo("d.jpg")];
        album.Update(photos);
        var first = album.Advance()!;
        var gone = album.Next!;

        album.Update([.. photos.Where(p => p != gone), Photo("new.jpg")]);
        var rest = Shown(album, 3);
        Assert.Equal(3, rest.Distinct().Count());
        Assert.Contains("new.jpg", rest);
        Assert.DoesNotContain(Path.GetFileName(gone.Path), rest);
        Assert.DoesNotContain(Path.GetFileName(first.Path), rest);
    }

    [Fact]
    public void An_album_carries_on_from_the_photo_another_frame_had_on_show()
    {
        var album = new PhotoAlbum(PhotoAlbum.ByName);
        album.StartFrom(Photo("b.jpg"));
        album.Update([Photo("a.jpg"), Photo("b.jpg"), Photo("c.jpg")]);
        Assert.Equal(["c.jpg", "a.jpg", "b.jpg"], Shown(album, 3));

        var shuffled = new PhotoAlbum(PhotoAlbum.Shuffle, new Random(2));
        shuffled.StartFrom(Photo("b.jpg"));
        shuffled.Update([Photo("a.jpg"), Photo("b.jpg"), Photo("c.jpg")]);
        Assert.DoesNotContain("b.jpg", Shown(shuffled, 2));
    }

    // ---- Finding and decoding ------------------------------------------------------------------------

    private string Png(string name, int width = 8, int height = 8)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)0xFF, width * height * 4).ToArray(), width * 4);
        return Save(name, new PngBitmapEncoder(), bitmap);
    }

    /// <summary>
    /// A JPEG <paramref name="width"/> × <paramref name="height"/> as stored, red in its top-left quarter and blue
    /// elsewhere, with an EXIF orientation and the date it was taken.
    /// </summary>
    private string Jpeg(string name, int width, int height, ushort orientation = 1, string? taken = null)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = x < width / 2 && y < height / 2;
                pixels[(y * width + x) * 4] = red ? (byte)0 : (byte)0xFF;
                pixels[(y * width + x) * 4 + 2] = red ? (byte)0xFF : (byte)0;
                pixels[(y * width + x) * 4 + 3] = 0xFF;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);
        if (taken is not null)
            metadata.SetQuery("/app1/ifd/exif/{ushort=36867}", taken);
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0), null, metadata, null));
        var path = Path.Combine(_root, name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private string Save(string name, BitmapEncoder encoder, BitmapSource bitmap)
    {
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    [Fact]
    public void The_folder_is_listed_for_pictures_with_or_without_its_subfolders()
    {
        Png("a.png");
        Png(Path.Combine("Holiday", "b.png"));
        File.SetAttributes(Png("thumbnail.png"), FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "Not a picture");

        Assert.Equal(["a.png", "b.png"], PhotoAlbum.List(_root, subfolders: true)!.Select(p => Path.GetFileName(p.Path)).Order());
        Assert.Equal(["a.png"], PhotoAlbum.List(_root, subfolders: false)!.Select(p => Path.GetFileName(p.Path)));
        Assert.Null(PhotoAlbum.List(Path.Combine(_root, "Missing"), subfolders: true));
    }

    /// <summary>Where the red quarter of <see cref="Jpeg"/> ends up once the photo's turned the right way up.</summary>
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, true)]
    [InlineData(4, false, true)]
    [InlineData(5, false, false)]
    [InlineData(6, true, false)]
    [InlineData(7, true, true)]
    [InlineData(8, false, true)]
    public void Photos_are_turned_the_way_the_camera_says_is_up(ushort orientation, bool right, bool bottom)
    {
        Sta.Run(() =>
        {
            var photo = PhotoAlbum.Decode(Jpeg("photo.jpg", 64, 32, orientation), "fit", 1000, 1000)!;
            var sideways = orientation >= 5;
            Assert.Equal(sideways ? (32, 64) : (64, 32), (photo.Picture.PixelWidth, photo.Picture.PixelHeight));

            var pixels = new FormatConvertedBitmap(photo.Picture, PixelFormats.Bgra32, null, 0);
            var x = right ? pixels.PixelWidth - 4 : 3;
            var y = bottom ? pixels.PixelHeight - 4 : 3;
            var pixel = new byte[4];
            pixels.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            Assert.True(pixel[2] > 0xC0 && pixel[0] < 0x40, $"Orientation {orientation}: expected red at ({x}, {y}), found #{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}.");
        });
    }

    [Fact]
    public void Photos_are_decoded_at_about_the_size_they_are_shown_with_when_they_were_taken()
    {
        Sta.Run(() =>
        {
            // Stored 1600 × 1200 on its side, so it shows 1200 wide and 1600 tall.
            var path = Jpeg("sideways.jpg", 1600, 1200, orientation: 6, taken: "2025:07:03 09:45:00");

            var fit = PhotoAlbum.Decode(path, "fit", 300, 200)!;
            Assert.Equal((150, 200), (fit.Picture.PixelWidth, fit.Picture.PixelHeight));
            var fill = PhotoAlbum.Decode(path, "fill", 300, 200)!;
            Assert.Equal((300, 200), (fill.Picture.PixelWidth, fill.Picture.PixelHeight));

            Assert.Equal(new DateTime(2025, 7, 3, 9, 45, 0), fit.Taken);
            Assert.True(fit.Picture.IsFrozen);
            Assert.Null(PhotoAlbum.Decode(Png("plain.png"), "fill", 300, 200)!.Taken);
        });
    }

    [Fact]
    public void Files_that_are_not_pictures_after_all_decode_to_nothing()
    {
        Sta.Run(() =>
        {
            var broken = Path.Combine(_root, "broken.jpg");
            File.WriteAllBytes(broken, [0xFF, 0xD8, 0xFF, 1, 2, 3]);
            Assert.Null(PhotoAlbum.Decode(broken, "fill", 300, 200));
            Assert.Null(PhotoAlbum.Decode(Path.Combine(_root, "gone.jpg"), "fill", 300, 200));
            // A bitmap has no metadata to ask about, but shows all the same.
            Assert.NotNull(PhotoAlbum.Decode(Save("old.bmp", new BmpBitmapEncoder(), BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgr32, null, new byte[64], 16)), "fill", 300, 200));
        });
    }

    // ---- The widget ------------------------------------------------------------------------------------

    private static PhotoFrameWidget Frame(JsonObject settings)
    {
        settings["type"] = "photos";
        var frame = (PhotoFrameWidget)WidgetFactory.Create(settings, Theme.From(new ThemeSettings()));
        frame.Width = 256;
        frame.Height = 192;
        frame.Measure(new Size(256, 192));
        frame.Arrange(new Rect(0, 0, 256, 192));
        frame.UpdateLayout();
        return frame;
    }

    /// <summary>Refreshes the frame at <paramref name="now"/>, then waits while it lists the folder and decodes photos.</summary>
    private static void Settle(PhotoFrameWidget frame, DateTime now, MetricStore? store = null)
    {
        frame.Refresh(store ?? new MetricStore(), now);
        var waiting = Stopwatch.StartNew();
        while (true)
        {
            var done = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(() => done.Continue = false, DispatcherPriority.ContextIdle);
            Dispatcher.PushFrame(done);
            if (!frame.Busy)
                return;
            if (waiting.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The photo frame was still busy after 10 seconds.");
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void The_frame_shows_each_photo_in_turn_and_decodes_only_the_next_one_ahead()
    {
        Sta.Run(() =>
        {
            var first = Jpeg("a.jpg", 1600, 1200);
            File.WriteAllBytes(Path.Combine(_root, "b.jpg"), [1, 2, 3]);
            var third = Png("c.png");
            var frame = Frame(new JsonObject { ["folder"] = _root, ["order"] = "name", ["seconds"] = 10 });

            Settle(frame, Start);
            Assert.Equal(first, frame.ShownPath);
            // The broken one is passed over.
            Assert.Equal(third, frame.NextPath);
            // Decoded at about the size it's shown, not the 1600 × 1200 it is.
            var picture = (BitmapSource)Descendants<Border>(frame).Select(b => b.Background).OfType<ImageBrush>().Single().ImageSource;
            Assert.InRange(picture.PixelWidth, 256, 300);

            Settle(frame, Start.AddSeconds(9));
            Assert.Equal(first, frame.ShownPath);
            Settle(frame, Start.AddSeconds(10));
            Assert.Equal(third, frame.ShownPath);
            Assert.Equal(first, frame.NextPath);
        });
    }

    [Fact]
    public void A_tap_moves_on_straight_away()
    {
        Sta.Run(() =>
        {
            var first = Png("a.png");
            var second = Png("b.png");
            var frame = Frame(new JsonObject { ["folder"] = _root, ["order"] = "name" });
            Settle(frame, Start);
            Assert.Equal(first, frame.ShownPath);

            Descendants<TapTarget>(frame).Single().Tap();
            Settle(frame, Start);
            Assert.Equal(second, frame.ShownPath);
        });
    }

    [Fact]
    public void Nothing_moves_on_or_is_read_while_the_dashboard_is_hidden()
    {
        Sta.Run(() =>
        {
            var first = Png("a.png");
            var second = Png("b.png");
            var frame = Frame(new JsonObject { ["folder"] = _root, ["order"] = "name", ["seconds"] = 10, ["fade"] = 0 });
            var window = new Window
            {
                Content = frame, SizeToContent = SizeToContent.WidthAndHeight, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };
            window.Show();
            Settle(frame, Start);
            Assert.Equal(first, frame.ShownPath);

            window.Hide();
            frame.Refresh(new MetricStore(), Start.AddHours(1));
            Assert.False(frame.Busy);
            Assert.Equal(first, frame.ShownPath);

            window.Show();
            Settle(frame, Start.AddHours(1));
            Assert.Equal(second, frame.ShownPath);
            window.Close();
        });
    }

    [Fact]
    public void A_frame_built_again_carries_on_with_the_photo_on_show()
    {
        Sta.Run(() =>
        {
            Png("a.png");
            Png("b.png");
            Png("c.png");
            var settings = new JsonObject { ["folder"] = _root };
            var first = Frame(settings);
            Settle(first, Start);
            Settle(first, Start.AddSeconds(30));

            // Shown as soon as it has a size, before it's refreshed, so there's no flash of an empty box.
            var again = Frame((JsonObject)settings.DeepClone());
            Assert.NotNull(first.ShownPath);
            Assert.Equal(first.ShownPath, again.ShownPath);
        });
    }

    [Fact]
    public void Without_a_folder_a_snapshot_shows_the_demo_photo_and_the_editor_asks_for_one()
    {
        Sta.Run(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            var store = new MetricStore();
            DemoReadings.Fill(store, Start);

            var snapshot = Frame(new JsonObject { ["caption"] = "date" });
            snapshot.Refresh(store, Start);
            Assert.Equal("Lakeside at dusk.jpg", snapshot.ShownPath);
            Assert.Contains("14 August 2026", Texts(snapshot));

            var editing = Frame(new JsonObject());
            editing.Editing = true;
            editing.Refresh(store, Start);
            Assert.Null(editing.ShownPath);
            Assert.Equal(["Choose a folder"], Texts(editing));
        });
    }

    [Fact]
    public void While_editing_a_frame_with_nothing_to_show_says_why()
    {
        Sta.Run(() =>
        {
            var missing = Frame(new JsonObject { ["folder"] = Path.Combine(_root, "Missing") });
            missing.Editing = true;
            Settle(missing, Start);
            Assert.Equal(["Folder not found"], Texts(missing));

            var empty = Frame(new JsonObject { ["folder"] = _root });
            empty.Editing = true;
            Settle(empty, Start);
            Assert.Equal(["No pictures in this folder"], Texts(empty));
        });
    }

    // ---- Sharing -------------------------------------------------------------------------------------

    [Fact]
    public void Exported_layouts_leave_the_folder_out()
    {
        var layout = new LayoutDocument { Widgets = [new JsonObject { ["type"] = "photos", ["folder"] = _root, ["seconds"] = 20 }] };
        using var stream = new MemoryStream();
        Assert.Empty(LayoutPackage.Write(layout, "Frame", stream));
        // The layout itself still has it.
        Assert.Equal(_root, layout.Widgets[0].GetString("folder"));

        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("layout.json")!.Open());
        var json = reader.ReadToEnd();
        Assert.DoesNotContain(Path.GetFileName(_root), json);
        var widget = JsonNode.Parse(json)!["widgets"]![0]!.AsObject();
        Assert.Null(widget["folder"]);
        Assert.Equal(20, widget.GetDouble("seconds", 0));
    }

    [Fact]
    public void Shared_layouts_that_name_a_folder_have_it_dropped_when_they_are_opened()
    {
        // Made by hand: a folder that would be on someone else's PC, or anywhere on the network.
        var layout = """{ "widgets": [ { "type": "photos", "folder": "\\\\server\\share\\photos", "seconds": 20 } ] }""";

        var bare = Gallery.Open(new GalleryEntry("Shared", "shared.json", null, null, 0, 0), Encoding.UTF8.GetBytes(layout));
        Assert.Null(bare.Layout.Widgets[0]["folder"]);
        Assert.Equal(20, bare.Layout.Widgets[0].GetDouble("seconds", 0));

        using var package = new MemoryStream();
        using (var zip = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("layout.json").Open());
            writer.Write(layout);
        }
        package.Position = 0;
        Assert.Null(LayoutPackage.Read(package, "Shared").Layout.Widgets[0]["folder"]);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------

    private static List<string> Texts(DependencyObject root) =>
        [.. Descendants<TextBlock>(root).Where(IsShown).Select(t => t.Text).Where(t => t.Length > 0)];

    /// <summary>Visible as far as its own and its parents' settings go: off screen, nothing is ever <c>IsVisible</c>.</summary>
    private static bool IsShown(DependencyObject element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible })
                return false;
        }
        return true;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var deeper in Descendants<T>(child))
                yield return deeper;
        }
    }
}
