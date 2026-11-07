using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class SoundOutputsTests
{
    private static readonly SoundOutput Headphones = new("{0.0.0}.{a}", "Headphones", "Arctis Nova 7X", OutputKind.Headphones, Default: false);
    private static readonly SoundOutput Speakers = new("{0.0.0}.{b}", "Speakers", "Realtek(R) Audio", OutputKind.Speakers, Default: true);
    private static readonly SoundOutput SteamSpeakers = new("{0.0.0}.{c}", "Speakers", "Steam Streaming Speakers", OutputKind.Speakers, Default: false);
    private static readonly SoundOutput Tv = new("{0.0.0}.{d}", "Beyond TV", "AMD High Definition Audio Device", OutputKind.Display, Default: false);

    private static readonly IReadOnlyList<SoundOutput> Outputs = [Headphones, Speakers, SteamSpeakers, Tv];

    [Theory]
    [InlineData("4- Arctis Nova 7X", "Arctis Nova 7X")]
    [InlineData("4 - Beyond TV", "Beyond TV")]
    [InlineData("12- SAMSUNG", "SAMSUNG")]
    [InlineData("Realtek(R) Audio", "Realtek(R) Audio")]
    [InlineData("7.1 Surround - Speakers", "7.1 Surround - Speakers")]
    [InlineData("4-", "4-")]
    [InlineData(null, "")]
    public void The_numbers_Windows_puts_in_front_of_names_come_off(string? name, string expected) =>
        Assert.Equal(expected, SoundOutputs.Unnumbered(name));

    [Theory]
    [InlineData("Headphones", "{0.0.0}.{a}")]
    [InlineData("headphones (arctis nova 7x)", "{0.0.0}.{a}")]
    [InlineData("Arctis Nova 7X", "{0.0.0}.{a}")]
    [InlineData("{0.0.0}.{d}", "{0.0.0}.{d}")]
    [InlineData("Speakers", "{0.0.0}.{b}")]
    [InlineData("Steam", "{0.0.0}.{c}")]
    [InlineData("TV", "{0.0.0}.{d}")]
    [InlineData("Bluetooth", null)]
    public void An_output_is_named_by_its_id_its_names_or_part_of_them(string name, string? id) =>
        Assert.Equal(id, SoundOutputs.Pick(Outputs, name)?.Id);

    [Fact]
    public void Several_names_or_none_take_turns_from_the_one_playing()
    {
        // Speakers are playing: next comes the TV, of the two named.
        Assert.Equal(Tv, SoundOutputs.Pick(Outputs, "Speakers, Beyond TV"));
        // The one playing isn't named, so the first named; one that isn't on is passed over.
        Assert.Equal(Tv, SoundOutputs.Pick(Outputs, "Beyond TV, Headphones, Bluetooth"));
        // After the last named, the first again.
        IReadOnlyList<SoundOutput> onHeadphones = [Headphones with { Default = true }, Speakers with { Default = false }, SteamSpeakers, Tv];
        Assert.Equal(Tv, SoundOutputs.Pick(onHeadphones, "Beyond TV, Headphones"));
        Assert.Equal(SteamSpeakers, SoundOutputs.Pick(Outputs, ""));
        Assert.Null(SoundOutputs.Pick(Outputs, "Bluetooth, Nothing"));
    }

    [Fact]
    public void Outputs_are_read_only_while_wanted_and_their_names_remembered()
    {
        var store = new MetricStore();
        var time = new FakeTime(new DateTimeOffset(2026, 11, 7, 20, 0, 0, TimeSpan.Zero));
        var reads = 0;
        using var provider = new SoundProvider(_ => [], () => null, time, () =>
        {
            reads++;
            return Outputs;
        });

        provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0, reads);
        Assert.Null(store.GetAttachment(SoundProvider.Outputs));

        SoundProvider.WantOutputs(store, time.Now);
        provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, reads);
        Assert.Same(Outputs, store.GetAttachment(SoundProvider.Outputs));
        Assert.Equal(["Headphones (Arctis Nova 7X)", "Speakers (Realtek(R) Audio)", "Speakers (Steam Streaming Speakers)", "Beyond TV (AMD High Definition Audio Device)"],
            SoundProvider.OutputNames(store));

        time.Now += SoundProvider.WantedFor;
        provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, reads);
        Assert.Null(store.GetAttachment(SoundProvider.Outputs));
        // Names seen stay, for the picker, while the outputs aren't read.
        Assert.Equal(4, SoundProvider.OutputNames(store).Count);
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
