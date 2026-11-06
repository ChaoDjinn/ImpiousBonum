using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class VoiceChatProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 20, 0, 0, TimeSpan.Zero);

    private static AppSound Out(double peak, int process = 10148) => new(process, "Discord", SoundFlow.Out, true, peak);

    private static AppSound Mic(double peak, bool active = true) => new(10148, "Discord", SoundFlow.In, active, peak);

    private static VoiceState? Live(MetricStore store) => store.GetAttachment(VoiceChatProvider.Live) as VoiceState;

    private static double? Value(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    [Fact]
    public void Discord_s_readings_appear_once_it_makes_or_hears_sound()
    {
        var store = new MetricStore();
        using var voice = new VoiceChatProvider(_ => []);

        voice.Publish(store, [], Start);
        Assert.False(store.TryGet(VoiceChatProvider.InCall, out _));
        Assert.Null(Live(store));

        voice.Publish(store, [Out(0)], Start.AddSeconds(1));
        Assert.Equal(0, Value(store, VoiceChatProvider.InCall));
        Assert.Equal(new VoiceState(false, false, false, 0, 0), Live(store));
    }

    [Fact]
    public void In_a_call_someone_talking_and_you_light_up_and_stay_on_for_a_moment()
    {
        var store = new MetricStore();
        using var voice = new VoiceChatProvider(_ => []);

        voice.Publish(store, [Out(0.3), Mic(0.001)], Start);
        Assert.Equal(new VoiceState(true, true, false, 0.3, 0.001), Live(store));

        // Between words: still talking, for a moment.
        voice.Publish(store, [Out(0), Mic(0.2)], Start.AddMilliseconds(300));
        Assert.Equal(new VoiceState(true, true, true, 0, 0.2), Live(store));

        voice.Publish(store, [Out(0), Mic(0)], Start.AddMilliseconds(500));
        Assert.Equal(new VoiceState(true, false, true, 0, 0), Live(store));

        voice.Publish(store, [Out(0), Mic(0)], Start.AddMilliseconds(800));
        Assert.Equal(new VoiceState(true, false, false, 0, 0), Live(store));
    }

    [Fact]
    public void Outside_a_call_Discord_s_sounds_are_not_anyone_talking()
    {
        var store = new MetricStore();
        using var voice = new VoiceChatProvider(_ => []);

        // A message's ping, with the microphone closed: its meter, if Windows still lists it, isn't Discord hearing you.
        voice.Publish(store, [Out(0.4), Mic(0.3, active: false)], Start);

        Assert.Equal(new VoiceState(false, false, false, 0.4, 0), Live(store));
    }

    [Fact]
    public void The_loudest_stream_counts()
    {
        var store = new MetricStore();
        using var voice = new VoiceChatProvider(_ => []);

        voice.Publish(store, [Out(0.01, process: 16928), Out(0.25), Mic(0.05), Mic(0.002)], Start);

        Assert.Equal(new VoiceState(true, true, true, 0.25, 0.05), Live(store));
    }

    [Fact]
    public void The_readings_are_written_once_a_second_for_the_second_gone()
    {
        var store = new MetricStore();
        using var voice = new VoiceChatProvider(_ => []);

        voice.Publish(store, [Out(0), Mic(0)], Start);
        Assert.Equal(0, Value(store, VoiceChatProvider.Talking));

        // A word in the middle of the second, and quiet at its end: the reading says someone talked, as loud as it got.
        voice.Publish(store, [Out(0.3), Mic(0.08)], Start.AddMilliseconds(100));
        voice.Publish(store, [Out(0.1), Mic(0)], Start.AddMilliseconds(200));
        Assert.Equal(0, Value(store, VoiceChatProvider.Talking));
        voice.Publish(store, [Out(0), Mic(0)], Start.AddMilliseconds(1000));
        Assert.Equal(1, Value(store, VoiceChatProvider.InCall));
        Assert.Equal(1, Value(store, VoiceChatProvider.Talking));
        Assert.Equal(1, Value(store, VoiceChatProvider.You));
        Assert.Equal(30, Value(store, VoiceChatProvider.Level));
        Assert.Equal(8, Value(store, VoiceChatProvider.MicLevel));
        Assert.Equal(2, store.GetHistory(VoiceChatProvider.Level, 2).Count(v => !double.IsNaN(v)));

        // A quiet second after it.
        voice.Publish(store, [Out(0), Mic(0)], Start.AddMilliseconds(2000));
        Assert.Equal(0, Value(store, VoiceChatProvider.Talking));
        Assert.Equal(0, Value(store, VoiceChatProvider.Level));
    }

    [Fact]
    public void When_Discord_closes_its_readings_go_empty()
    {
        var store = new MetricStore();
        using var voice = new VoiceChatProvider(_ => []);
        voice.Publish(store, [Out(0.3), Mic(0.1)], Start);

        voice.Publish(store, [], Start.AddSeconds(1));

        Assert.Null(Live(store));
        Assert.True(store.TryGet(VoiceChatProvider.InCall, out var call));
        Assert.Null(call.Value);
        Assert.Null(Value(store, VoiceChatProvider.Talking));
    }

    [Fact]
    public void Discord_s_sound_is_looked_for_again_every_two_seconds()
    {
        var store = new MetricStore();
        var time = new FakeTime(Start);
        var asked = new List<bool>();
        using var voice = new VoiceChatProvider(lookAgain =>
        {
            asked.Add(lookAgain);
            return [Out(0)];
        }, time);

        for (var i = 0; i < 25; i++)
        {
            voice.SampleAsync(store, CancellationToken.None);
            time.Now += TimeSpan.FromMilliseconds(100);
        }

        Assert.Equal([0, 20], asked.Select((again, i) => (again, i)).Where(a => a.again).Select(a => a.i));
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
