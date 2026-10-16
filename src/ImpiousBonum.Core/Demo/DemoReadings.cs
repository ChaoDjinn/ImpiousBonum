using ImpiousBonum.Core.Calendar;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.News;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Sensors;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Demo;

/// <summary>
/// Made-up readings for every widget, with two minutes of history behind each graph: a gaming PC part-way through an
/// evening's play, with music on and the sensor service installed. For screenshots in the docs (<c>--snapshot … --demo</c>),
/// so they look like a real dashboard without showing anyone's own PC, music, calendar or apps. Always the same, so
/// screenshots can be made again and match.
/// </summary>
public static class DemoReadings
{
    /// <summary>How many past readings each graph gets: <see cref="MetricStore.DefaultHistoryLength"/>, a full graph.</summary>
    private const int History = MetricStore.DefaultHistoryLength;

    private const long GiB = 1L << 30;

    /// <summary>
    /// A frozen moment of music for the audio visualiser (<c>double[]</c> levels from 0 to 1, bass first), since a
    /// snapshot can't listen to anything.
    /// </summary>
    public const string Spectrum = "demo.spectrum";

    /// <summary>Fills <paramref name="store"/> with the demo readings, as they'd be at <paramref name="now"/>.</summary>
    /// <param name="artwork">The album art for what's playing (PNG or JPEG bytes), if any.</param>
    public static void Fill(MetricStore store, DateTime now, byte[]? artwork = null)
    {
        var random = new Random(1977);

        // CPU: 16 threads, busy but not flat out.
        store.Register(new MetricDefinition(CpuProvider.Load, "CPU load", "CPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(CpuProvider.Threads, "CPU logical processors", "CPU", MetricUnit.None));
        store.Register(new MetricDefinition(CpuProvider.Clock, "CPU clock (effective)", "CPU", MetricUnit.Megahertz));
        Series(store, CpuProvider.Load, random, 34, 22, 0.12);
        store.Set(CpuProvider.Threads, 16);
        Series(store, CpuProvider.Clock, random, 4870, 90, 0.2);
        for (var core = 0; core < 16; core++)
        {
            store.Register(new MetricDefinition(CpuProvider.CoreLoad(core), $"CPU {core} load", "CPU cores", MetricUnit.Percent, 100));
            // The game's main threads work hardest; the rest tick over.
            Series(store, CpuProvider.CoreLoad(core), random, core is 0 or 2 or 5 ? 78 : core % 3 == 0 ? 41 : 17, 12, 0.3);
        }

        // Memory: 32 GB, about half used.
        store.Register(new MetricDefinition(MemoryProvider.Used, "RAM used", "Memory", MetricUnit.Bytes, 32 * GiB));
        store.Register(new MetricDefinition(MemoryProvider.Total, "RAM total", "Memory", MetricUnit.Bytes));
        store.Register(new MetricDefinition(MemoryProvider.Available, "RAM available", "Memory", MetricUnit.Bytes, 32 * GiB));
        store.Register(new MetricDefinition(MemoryProvider.Load, "RAM load", "Memory", MetricUnit.Percent, 100));
        Series(store, MemoryProvider.Used, random, 17.6 * GiB, 0.4 * GiB, 0.02);
        store.Set(MemoryProvider.Total, 32 * GiB);
        store.Set(MemoryProvider.Available, 14.4 * GiB);
        store.Set(MemoryProvider.Load, 55);

        // Graphics card: busy with a game.
        store.Register(new MetricDefinition(GpuProvider.Name, "GPU name", "GPU", MetricUnit.Text));
        store.Register(new MetricDefinition(GpuProvider.Load, "GPU load", "GPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(GpuProvider.VramUsed, "GPU memory used", "GPU", MetricUnit.Bytes, 16 * GiB));
        store.Register(new MetricDefinition(GpuProvider.VramTotal, "GPU memory total", "GPU", MetricUnit.Bytes));
        store.Register(new MetricDefinition(GpuProvider.VramUsedPercent, "GPU memory used %", "GPU", MetricUnit.Percent, 100));
        store.SetText(GpuProvider.Name, "Graphics card");
        Series(store, GpuProvider.Load, random, 88, 10, 0.12);
        Series(store, GpuProvider.VramUsed, random, 9.4 * GiB, 0.1 * GiB, 0.01);
        store.Set(GpuProvider.VramTotal, 16 * GiB);
        store.Set(GpuProvider.VramUsedPercent, 59);

        // The sensor service's readings: temperatures, power and a steady 144 FPS with the odd dip.
        foreach (var alias in SensorAliases.All)
            store.Register(alias.ToDefinition());
        Series(store, SensorAliases.CpuTemperature, random, 64, 2.5, 0.06);
        Series(store, SensorAliases.CpuPower, random, 96, 12, 0.2);
        Series(store, SensorAliases.GpuTemperature, random, 68, 1.5, 0.04);
        Series(store, SensorAliases.GpuHotSpot, random, 81, 2, 0.05);
        Series(store, SensorAliases.GpuPower, random, 287, 18, 0.15);
        Series(store, SensorAliases.GpuFan, random, 1480, 40, 0.05);
        var fps = Series(store, SensorAliases.FramesPerSecond, random, 144, 6, 0.4, dips: true);
        store.SetText(SensorAliases.FramesPerSecondApp, "StarfallOdyssey");
        foreach (var frames in fps)
            store.Set(SensorAliases.FrameTime, 1000 / frames);
        Series(store, SensorAliases.MaxFrameTime, random, 8.6, 1.4, 0.5);
        Series(store, SensorAliases.OnePercentLow, random, 118, 6, 0.1);
        Series(store, SensorAliases.PointOnePercentLow, random, 97, 8, 0.1);

        // Network and drives.
        store.Register(new MetricDefinition(NetworkProvider.Download, "Download", "Network", MetricUnit.BytesPerSecond));
        store.Register(new MetricDefinition(NetworkProvider.Upload, "Upload", "Network", MetricUnit.BytesPerSecond));
        store.Register(new MetricDefinition(PingProvider.Latency, "Ping", "Network", MetricUnit.Milliseconds));
        Series(store, NetworkProvider.Download, random, 2.6e6, 1.4e6, 0.6, floor: 40e3);
        Series(store, NetworkProvider.Upload, random, 310e3, 160e3, 0.6, floor: 8e3);
        Series(store, PingProvider.Latency, random, 12, 2, 0.4, floor: 8);

        store.Register(new MetricDefinition(DriveProvider.Letters, "Drive letters", "Disk", MetricUnit.Text));
        store.SetText(DriveProvider.Letters, "C,D,E");
        Drive(store, "C", "Windows", 1000, 412);
        Drive(store, "D", "Games", 2000, 1384);
        Drive(store, "E", "Media", 4000, 1720);
        store.Register(new MetricDefinition(DiskActivityProvider.ReadTotal, "Disk read (all drives)", "Disk", MetricUnit.BytesPerSecond));
        store.Register(new MetricDefinition(DiskActivityProvider.WriteTotal, "Disk write (all drives)", "Disk", MetricUnit.BytesPerSecond));
        Series(store, DiskActivityProvider.ReadTotal, random, 48e6, 30e6, 0.7, floor: 0);
        Series(store, DiskActivityProvider.WriteTotal, random, 6e6, 5e6, 0.7, floor: 0);
        foreach (var (letter, active) in new[] { ("C", 6.0), ("D", 23.0), ("E", 1.0) })
        {
            var category = $"Drive {letter}:";
            store.Register(new MetricDefinition(DiskActivityProvider.Read(letter), $"{letter}: read", category, MetricUnit.BytesPerSecond));
            store.Register(new MetricDefinition(DiskActivityProvider.Write(letter), $"{letter}: write", category, MetricUnit.BytesPerSecond));
            store.Register(new MetricDefinition(DiskActivityProvider.Active(letter), $"{letter}: active time", category, MetricUnit.Percent, 100));
            Series(store, DiskActivityProvider.Active(letter), random, active, active, 0.5, floor: 0);
            Series(store, DiskActivityProvider.Read(letter), random, active * 2e6, active * 1.5e6, 0.6, floor: 0);
            Series(store, DiskActivityProvider.Write(letter), random, active * 0.3e6, active * 0.3e6, 0.6, floor: 0);
        }

        store.Register(new MetricDefinition(SystemProvider.Uptime, "Time since Windows started", "System", MetricUnit.Text));
        store.SetText(SystemProvider.Uptime, "2d 7h");

        // The busiest apps.
        new ProcessProvider(() => []).Initialize(store);
        store.SetAttachment(ProcessProvider.Apps, (IReadOnlyList<AppUsage>)
        [
            new("Starfall Odyssey", 3, 21.4, (long)(6.8 * GiB)),
            new("Firefox", 14, 3.1, (long)(2.1 * GiB)),
            new("Discord", 6, 1.2, 640L << 20),
            new("Music", 2, 0.6, 310L << 20),
            new("Impious Bonum", 2, 0.3, 74L << 20),
            new("Windows Explorer", 1, 0.2, 180L << 20),
        ]);

        // What's playing.
        foreach (var (id, name, unit) in new[]
        {
            (MediaProvider.Title, "Now playing: title", MetricUnit.Text), (MediaProvider.Artist, "Now playing: artist", MetricUnit.Text),
            (MediaProvider.Album, "Now playing: album", MetricUnit.Text), (MediaProvider.App, "Now playing: app", MetricUnit.Text),
            (MediaProvider.Status, "Now playing: status", MetricUnit.Text), (MediaProvider.Position, "Now playing: position", MetricUnit.Text),
            (MediaProvider.Duration, "Now playing: length", MetricUnit.Text),
        })
        {
            store.Register(new MetricDefinition(id, name, "Media", unit));
        }
        store.Register(new MetricDefinition(MediaProvider.Progress, "Now playing: progress", "Media", MetricUnit.Percent, 100));
        var nowUtc = now.ToUniversalTime();
        MediaProvider.Publish(store, new MediaSnapshot("Music", "Neon Skyline", "The Night Shift", "After Hours Radio", MediaPlaybackStatus.Playing,
            TimeSpan.FromSeconds(97), TimeSpan.FromSeconds(244), nowUtc, artwork is not null, artwork), nowUtc);
        store.SetAttachment(MediaProvider.Artwork, artwork);

        // The weather in a made-up town, and the forecast.
        var weather = new WeatherProvider(() => null, (_, _) => Task.FromException<WeatherReport>(new InvalidOperationException("Demo")));
        weather.Initialize(store);
        // Three hours back to 48 ahead, like Open-Meteo's. The pressure falls ahead of the rain due in nine hours or so.
        var hours = Enumerable.Range(-3, 52).Select(h =>
        {
            var time = now.Date.AddHours(now.Hour + h);
            var daytime = time.Hour is >= 7 and < 20;
            return new WeatherPeriod(time, 14 + 3 * Math.Sin((time.Hour - 9) / 24.0 * 2 * Math.PI), null, h < 5 ? 2 : h < 9 ? 3 : 61,
                daytime, h < 9 ? 10 : 55)
            {
                Precipitation = h < 9 ? 0 : 0.6, Wind = 11 + h % 4, Gusts = 22 + h % 5, Uv = daytime ? 2 : 0, Pressure = 1016 - 0.5 * h,
            };
        }).ToList();
        int[] codes = [2, 61, 3, 0, 1, 80, 2, 3, 61, 63, 2, 1, 0, 3, 80, 2];
        var days = Enumerable.Range(0, 16).Select(d => new WeatherPeriod(now.Date.AddDays(d), 16 + d % 3, 9 + d % 2, codes[d], true, d == 1 ? 70 : 20)
        {
            Precipitation = d == 1 ? 6.2 : 0.4, Wind = 14 + d % 4, Gusts = 30 + d % 6, Uv = 3, Daylight = TimeSpan.FromMinutes(649 - 4 * d),
        }).ToList();
        // A shower in about 40 minutes, a quarter of an hour at a time from the one that ended last, as Open-Meteo gives them.
        var quarter = now.Date.AddMinutes(Math.Floor(now.TimeOfDay.TotalMinutes / 15) * 15);
        double[] shower = [0, 0, 0, 0, 0.2, 0.7, 1.4, 0.6, 0.1, 0, 0, 0];
        var report = new WeatherReport(WeatherUnits.Metric, now, 14, 13, 72, 11, 225, 2, now.Hour is >= 7 and < 20,
            hours, days, now.Date.AddHours(7.2), now.Date.AddHours(18.6))
        {
            Gusts = 24, Uv = now.Hour is >= 7 and < 20 ? 2 : 0, Pressure = 1016, DewPoint = 9, Visibility = 18000, Clouds = 45, Precipitation = 0,
            QuarterHours = [.. shower.Select((amount, i) => new QuarterHour(quarter.AddMinutes(15 * i), amount))],
        };
        weather.Publish(store, report, new WeatherRequest("Harbourton", 50.6, -2.0, WeatherUnits.Metric), now);

        // Its air: fair, with the last of the season's grass pollen.
        var air = new AirQualityProvider(() => null);
        air.Initialize(store);
        air.Publish(store, new AirQualityReport
        {
            EuropeanAqi = 24, UsAqi = 31, Pm25 = 5.2, Pm10 = 9.6, Ozone = 54, NitrogenDioxide = 11.3,
            Alder = 0, Birch = 0, Olive = 0, Grass = 6.4, Mugwort = 0.3, Ragweed = 0,
        }, new AirQualityRequest(50.6, -2.0, AqiScale.Automatic));

        // Harbourton is on the south coast, where the Met Office has a yellow warning out for tomorrow's wind.
        new WeatherWarningsProvider(() => null).Initialize(store);
        var tomorrowMorning = new DateTimeOffset(now.Date.AddDays(1).AddHours(6));
        WeatherWarningsProvider.Publish(store,
        [
            new WeatherWarning("demo-wind", "Yellow warning of wind affecting South West England", WarningLevel.Minor, "Yellow",
                tomorrowMorning, tomorrowMorning.AddHours(15), MetOfficeWarnings.WarningsPage, WarningSource.MetOffice),
        ], new DateTimeOffset(now));

        // Today's calendar.
        new CalendarProvider(() => []).Initialize(store);
        // An evening's worth: something later on, then tomorrow's. Times are from the start of the day, so the
        // agenda reads the same whenever the screenshot is made (it's drawn at 21:47).
        var day = now.Date;
        IReadOnlyList<CalendarEvent> events =
        [
            new("Team stand-up", day.AddHours(9.5), day.AddHours(9.75), false, "Video call", "Work"),
            new("Call with Sam", day.AddHours(22.25), day.AddHours(22.75), false, null, "Home"),
            new("Bins out", day.AddDays(1), day.AddDays(2), true, null, "Home"),
            new("Dentist", day.AddDays(1).AddHours(9), day.AddDays(1).AddHours(9.5), false, "High Street", "Home"),
            new("Design review", day.AddDays(1).AddHours(14), day.AddDays(1).AddHours(15), false, "Room 4", "Work"),
            new("Five-a-side", day.AddDays(1).AddHours(19), day.AddDays(1).AddHours(20), false, "Leisure centre", "Home"),
        ];
        store.SetAttachment(CalendarProvider.Events, events);
        var next = events.First(e => e.Start > now);
        store.SetText(CalendarProvider.NextTitle, next.Title);
        store.SetText(CalendarProvider.NextTime, CalendarProvider.When(next.Start, now));
        store.SetText(CalendarProvider.NextLocation, next.Location);
        store.Set(CalendarProvider.NextMinutes, Math.Round((next.Start - now).TotalMinutes));
        store.Set(CalendarProvider.Today, events.Count(e => e.Start.Date == now.Date && e.Start > now));

        // Headlines.
        new NewsProvider(() => []).Initialize(store);
        IReadOnlyList<Headline> headlines =
        [
            new("New telescope spots the most distant galaxy yet", null, now.AddMinutes(-18), "Science Daily"),
            new("City council approves riverside cycle path", null, now.AddMinutes(-42), "Local News"),
            new("Indie studio's space sim tops the charts", null, now.AddHours(-2), "Games Weekly"),
            new("Heatwave expected to break by the weekend", null, now.AddHours(-3), "Local News"),
        ];
        store.SetAttachment(NewsProvider.Headlines, headlines);
        store.SetAttachment(NewsProvider.ByFeed, headlines.GroupBy(h => h.Source).Select(g => new FeedHeadlines(g.Key, [.. g])).ToList());
        store.SetText(NewsProvider.Title, headlines[0].Title);
        store.SetText(NewsProvider.Source, headlines[0].Source);

        // A game session part-way through, for session recaps.
        new SessionProvider(() => null).Initialize(store);
        store.SetText(SessionProvider.Game, "Starfall Odyssey");
        store.SetText(SessionProvider.Playing, "Playing");
        store.Set(SessionProvider.Minutes, 47);
        store.Set(SessionProvider.Average("fps"), 141);
        store.Set(SessionProvider.Min("fps"), 96);
        store.Set(SessionProvider.Max("fps"), 165);

        // Claude plan usage.
        new ClaudeUsageProvider(string.Empty).Initialize(store);
        store.Set(ClaudeUsageProvider.Session, 38);
        store.SetText(ClaudeUsageProvider.SessionResets, "in 2 h 10 min");
        store.Set(ClaudeUsageProvider.Week, 61);
        store.SetText(ClaudeUsageProvider.WeekResets, "Thursday");
        store.SetText(ClaudeUsageProvider.Status, ClaudeUsageProvider.Connected);

        // Added later than the rest, so the readings above keep their made-up values from screenshot to screenshot.

        // The PC is on Wi-Fi.
        foreach (var definition in new MetricDefinition[]
        {
            new(NetworkProvider.Adapter, "Network adapter", "Network", MetricUnit.Text),
            new(NetworkProvider.Address, "Local IP address", "Network", MetricUnit.Text),
            new(NetworkProvider.DownloadTotal, "Downloaded since the adapter connected", "Network", MetricUnit.Bytes),
            new(NetworkProvider.UploadTotal, "Uploaded since the adapter connected", "Network", MetricUnit.Bytes),
            new(NetworkProvider.IdFor("Wi-Fi", NetworkProvider.AdapterDown)!, "Wi-Fi: download", "Network: Wi-Fi", MetricUnit.BytesPerSecond),
            new(NetworkProvider.IdFor("Wi-Fi", NetworkProvider.AdapterUp)!, "Wi-Fi: upload", "Network: Wi-Fi", MetricUnit.BytesPerSecond),
            new(NetworkProvider.IdFor("Wi-Fi", NetworkProvider.AdapterSpeed)!, "Wi-Fi: link speed", "Network: Wi-Fi", MetricUnit.Text),
            new(NetworkProvider.IdFor("Wi-Fi", NetworkProvider.AdapterAddress)!, "Wi-Fi: IP address", "Network: Wi-Fi", MetricUnit.Text),
            new(WifiProvider.Signal, "Wi-Fi signal", "Network", MetricUnit.Percent, 100),
            new(WifiProvider.Rssi, "Wi-Fi signal strength in dBm", "Network", MetricUnit.None, Format: "0"),
            new(WifiProvider.Network, "Wi-Fi network", "Network", MetricUnit.Text),
        })
        {
            store.Register(definition);
        }
        store.SetText(NetworkProvider.Adapter, "Wi-Fi");
        store.SetText(NetworkProvider.Address, "192.168.1.42");
        store.SetText(NetworkProvider.IdFor("Wi-Fi", NetworkProvider.AdapterAddress)!, "192.168.1.42");
        store.SetText(NetworkProvider.IdFor("Wi-Fi", NetworkProvider.AdapterSpeed)!, "1.2 Gbps");
        store.Set(NetworkProvider.DownloadTotal, 14.2 * GiB);
        store.Set(NetworkProvider.UploadTotal, 1.3 * GiB);
        foreach (var value in store.GetHistory(NetworkProvider.Download, History))
            store.Set(NetworkProvider.IdFor("Wi-Fi", NetworkProvider.AdapterDown)!, value);
        foreach (var value in store.GetHistory(NetworkProvider.Upload, History))
            store.Set(NetworkProvider.IdFor("Wi-Fi", NetworkProvider.AdapterUp)!, value);
        var signal = Series(store, WifiProvider.Signal, random, 82, 4, 0.2);
        foreach (var percent in signal)
            store.Set(WifiProvider.Rssi, Math.Round(percent / 2 - 100));
        store.SetText(WifiProvider.Network, "Harbourton-5G");

        // A moment of the music for the audio visualiser, which a snapshot can't hear: strong bass, tailing off to the treble.
        var spectrum = new double[64];
        for (var i = 0; i < spectrum.Length; i++)
            spectrum[i] = Math.Clamp(0.8 - 0.5 * i / spectrum.Length + 0.12 * Math.Sin(i * 0.9) + (random.NextDouble() - 0.5) * 0.2, 0.05, 1);
        store.SetAttachment(Spectrum, spectrum);

        // A headset microphone, live, with Discord using it; the camera's off.
        using var privacy = new PrivacyProvider(() => null, _ => null);
        privacy.Publish(store, new MicrophoneState("Headset microphone", Muted: false),
            [new ConsentEntry(@"C:#Users#Sam#AppData#Local#Discord#app-1.0.9260#Discord.exe", Packaged: false, Start: 2, Stop: 0)],
            [new ConsentEntry("Microsoft.WindowsCamera_8wekyb3d8bbwe", Packaged: true, Start: 1, Stop: 2)]);
    }

    /// <summary>
    /// Two minutes of a reading wandering around <paramref name="mean"/> (each step keeps <paramref name="smoothing"/> of
    /// the last change), ending at its latest value. With <paramref name="dips"/>, the odd sharp drop, like frame rates.
    /// Returns the readings, oldest first.
    /// </summary>
    private static List<double> Series(MetricStore store, string id, Random random, double mean, double spread, double smoothing, bool dips = false, double floor = 0)
    {
        var values = new List<double>(History);
        var value = mean;
        var drift = 0.0;
        for (var i = 0; i < History; i++)
        {
            drift = drift * (1 - smoothing) + (random.NextDouble() - 0.5) * spread * smoothing * 2;
            value = mean + drift + (random.NextDouble() - 0.5) * spread * 0.6;
            if (dips && random.NextDouble() < 0.03)
                value -= spread * 4;
            value = Math.Max(floor, value);
            values.Add(value);
            store.Set(id, value);
        }
        return values;
    }

    private static void Drive(MetricStore store, string letter, string label, double totalGb, double usedGb)
    {
        var category = $"Drive {letter}:";
        var total = totalGb * 1e9;
        var used = usedGb * 1e9;
        store.Register(new MetricDefinition(DriveProvider.Free(letter), $"{letter}: free", category, MetricUnit.Bytes, total));
        store.Register(new MetricDefinition(DriveProvider.Used(letter), $"{letter}: used", category, MetricUnit.Bytes, total));
        store.Register(new MetricDefinition(DriveProvider.Total(letter), $"{letter}: total", category, MetricUnit.Bytes));
        store.Register(new MetricDefinition(DriveProvider.UsedPercent(letter), $"{letter}: used %", category, MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(DriveProvider.Label(letter), $"{letter}: label", category, MetricUnit.Text));
        store.Set(DriveProvider.Free(letter), total - used);
        store.Set(DriveProvider.Used(letter), used);
        store.Set(DriveProvider.Total(letter), total);
        store.Set(DriveProvider.UsedPercent(letter), used / total * 100);
        store.SetText(DriveProvider.Label(letter), label);
    }
}
