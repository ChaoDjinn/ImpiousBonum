using System.IO;
using System.Windows.Input;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class HotkeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public HotkeyTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("Ctrl+Alt+D", true)]
    [InlineData("Win+F1", true)]
    [InlineData("Alt+Space", true)]
    [InlineData("F13", true)]
    [InlineData("Shift+F24", true)]
    [InlineData("A", false)]
    [InlineData("Shift+A", false)]
    [InlineData("F5", false)]
    [InlineData("Enter", false)]
    public void Hotkeys_need_a_modifier_unless_they_are_f13_to_f24(string keys, bool allowed)
    {
        var chord = KeyChord.Parse(keys, out _)!;
        Assert.Equal(allowed, Hotkey.Problem(chord) is null);
    }

    [Fact]
    public void Chords_become_registerhotkey_flags_that_dont_repeat()
    {
        Assert.Equal((0x4000u | 0x2 | 0x4, 0x74u), Hotkey.ToNative(KeyChord.Parse("Ctrl+Shift+F5", out _)!));
        Assert.Equal((0x4000u | 0x1 | 0x8, (uint)'D'), Hotkey.ToNative(KeyChord.Parse("Win+Alt+D", out _)!));
    }

    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.D, "Ctrl+Alt+D")]
    [InlineData(ModifierKeys.Control, Key.D5, "Ctrl+5")]
    [InlineData(ModifierKeys.Windows | ModifierKeys.Shift, Key.F5, "Shift+Win+F5")]
    [InlineData(ModifierKeys.None, Key.F13, "F13")]
    [InlineData(ModifierKeys.Alt, Key.Home, "Alt+Home")]
    public void Key_presses_read_as_text_that_parses_back(ModifierKeys modifiers, Key key, string expected)
    {
        var text = Hotkey.Text(modifiers, key);
        Assert.Equal(expected, text);
        Assert.NotNull(KeyChord.Parse(text, out _));
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LWin)]
    [InlineData(Key.System)]
    public void Modifiers_alone_arent_a_hotkey_yet(Key key) => Assert.Null(Hotkey.Text(ModifierKeys.Control, key));

    [Fact]
    public void The_page_offers_every_action_and_layout_and_refuses_clashes()
    {
        Sta.Run(() =>
        {
            var current = new List<Hotkey>
            {
                new(Hotkey.ShowHide, "Ctrl+Alt+D"),
                new(Hotkey.SwitchLayout, "Ctrl+Alt+1", "Gone"),
            };
            var page = new HotkeysPage(null);
            page.Fill(current, ["Default", "Gaming"]);
            Assert.Equal(current, page.Hotkeys());
            Assert.Null(page.Problem());

            page.Set(new Hotkey(Hotkey.SwitchLayout, "", "gaming"), "Ctrl+Alt+2");
            page.Set(new Hotkey(Hotkey.SwitchLayout, "", "Gone"), "");
            Assert.Equal(
                [new Hotkey(Hotkey.ShowHide, "Ctrl+Alt+D"), new Hotkey(Hotkey.SwitchLayout, "Ctrl+Alt+2", "Gaming")],
                page.Hotkeys());

            page.Set(new Hotkey(Hotkey.NextPage, ""), "ctrl+alt+d");
            Assert.Contains("both", page.Problem());

            page.Set(new Hotkey(Hotkey.NextPage, ""), "Shift+N");
            Assert.Contains("Ctrl, Alt or Win", page.Problem());
        });
    }

    [Fact]
    public void Hotkeys_are_saved_and_follow_a_renamed_layout()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var settings = new AppSettings { Hotkeys = [new Hotkey(Hotkey.SwitchLayout, "Ctrl+Alt+G", "Gaming"), new Hotkey(Hotkey.ShowHide, "F13")] };
            settings.Save();
            Assert.Equal(settings.Hotkeys, AppSettings.Load().Hotkeys);

            using var store = new LayoutStore(settings);
            store.Save("Gaming", new LayoutDocument());
            store.Rename("Gaming", "Games");
            Assert.Equal(new Hotkey(Hotkey.SwitchLayout, "Ctrl+Alt+G", "Games"), AppSettings.Load().Hotkeys[0]);
        });
    }

    [Fact]
    public void Hotkeys_that_cant_be_set_say_why()
    {
        Sta.Run(() =>
        {
            using var hotkeys = new GlobalHotkeys();
            var problems = hotkeys.Apply([new Hotkey(Hotkey.ShowHide, "Q"), new Hotkey(Hotkey.NextPage, "Ctrl+Nonsense")]);
            Assert.Equal(2, problems.Count);
            Assert.Contains("Show or hide the dashboard", problems[0]);
            Assert.Contains("isn't a key name", problems[1]);
        });
    }
}
