using System.Windows;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Settings;

/// <summary>Sensors: the sensor service that reads temperatures, fans, power and frame rates, and whose frame rate counts.</summary>
internal sealed class SensorsPage(ISettingsHost host) : CardPage("Sensors",
    "Temperatures, fan speeds, power and frame rates come from the sensor service, which reads the hardware with the administrator rights the dashboard doesn't have. Loads, memory, network and drives work without it.")
{
    private SettingsCard? _service;
    private bool _busy;

    protected override void Build()
    {
        var installed = SensorServiceControl.IsInstalled;
        var buttons = Ui.Row(Ui.Button(installed ? "Update…" : "Install…", () => Change(true), accent: host.SensorServiceOutdated || !installed));
        if (installed)
            buttons.Children.Add(Ui.Button("Remove…", () => Change(false)));
        buttons.IsEnabled = !_busy;
        _service = Add(Ui.Card("", "Sensor service", Status(), buttons));
        Add(Ui.Note("Windows asks before the service is installed, updated or removed. Installed, it starts with Windows, so there's no prompt when you sign in."));

        var monitors = DisplayMonitor.GetAll();
        var sources = monitors.Select(m => ((string?)m.Id, $"Top app on {m.DisplayName}")).Prepend(((string?)null, "The app in front"));
        Add(Ui.Card("", "FPS from", "Which app's frame rate the FPS readings follow",
            Ui.Choice(sources, host.Settings.FpsMonitorId, "FPS from", host.SetFpsSource, width: 320)));
    }

    private string Status() => host.SensorServiceOutdated ? $"{host.SensorStatus} · from an older version of Impious Bonum: update it" : host.SensorStatus;

    private async void Change(bool install)
    {
        _busy = true;
        Load();
        try
        {
            await host.ChangeSensorServiceAsync(install);
        }
        finally
        {
            _busy = false;
            Load();
        }
    }

    public override void Refresh()
    {
        if (_service is not null)
            _service.Description = Status();
    }
}

/// <summary>Weather: where the forecast is for, in which units, and air quality and pollen if wanted.</summary>
internal sealed class WeatherPage(ISettingsHost host) : CardPage("Weather",
    "The weather widget's forecast, and air quality and pollen if you like, from Open-Meteo. Nothing is fetched until you set a location.")
{
    private SettingsCard? _location;

    protected override void Build()
    {
        var hasLocation = host.Settings.WeatherLocation is not null;
        _location = Add(Ui.Card("", "Location", host.WeatherStatus,
            Ui.Button(hasLocation ? "Change…" : "Set location…", () =>
            {
                if (Window.GetWindow(this) is { } owner)
                    host.SetWeatherLocation(owner);
                Load();
            }, accent: !hasLocation)));
        Add(Ui.Card("", "Units", "Metric also has rain in mm, visibility in km and pressure in hPa; imperial has inches, miles and inches of mercury",
            Ui.Choice([(AppSettings.WeatherUnitsAuto, "Automatic (your region)"), (AppSettings.WeatherUnitsMetric, "Metric (°C, km/h)"), (AppSettings.WeatherUnitsImperial, "Imperial (°F, mph)")],
                host.Settings.WeatherUnits.ToLowerInvariant(), "Units", host.SetWeatherUnits)));

        var airQuality = host.Settings.AirQuality;
        Add(Ui.Card("", "Air quality and pollen",
            "The air quality index, particles, ozone and nitrogen dioxide, and grass, tree and weed pollen (in Europe, in season). "
            + "Once an hour, the location's coordinates go to Open-Meteo.com for them.",
            Ui.Switch(airQuality, "Air quality and pollen", on =>
            {
                host.SetAirQuality(on);
                Load();
            })));
        if (airQuality)
        {
            Add(Ui.Card("", "Air quality index",
                "The European index goes from Good to Extremely poor (0 to 100 and over), the US index from Good to Hazardous (0 to 500). Automatic uses the European one in Europe.",
                Ui.Choice([(AppSettings.AirQualityScaleAuto, "Automatic (by location)"), (AppSettings.AirQualityScaleEuropean, "European"), (AppSettings.AirQualityScaleUs, "US")],
                    host.Settings.AirQualityScale.ToLowerInvariant(), "Air quality index", host.SetAirQualityScale)));
            // CAMS asks to be credited where its data is used, so only while it is.
            Add(Ui.Note(OpenMeteo.AirQualityAttribution + "."));
        }
        if (hasLocation)
        {
            Add(Ui.Card("", "Turn off weather", "Forget the location and stop fetching forecasts",
                Ui.Button("Turn off", () =>
                {
                    host.TurnOffWeather();
                    Load();
                })));
        }
    }

    public override void Refresh()
    {
        if (_location is not null)
            _location.Description = host.WeatherStatus;
    }
}

/// <summary>Claude usage: connecting Claude Code's status line, which feeds the Claude usage widget.</summary>
internal sealed class ClaudeUsagePage(ISettingsHost host) : CardPage("Claude usage",
    "The Claude usage widget shows how much of your plan you've used, which it gets from Claude Code's status line.")
{
    private SettingsCard? _status;

    protected override void Build()
    {
        var connected = host.ClaudeCodeConnected;
        _status = Add(Ui.Card("", "Claude Code", host.ClaudeStatus,
            Ui.Button(connected ? "Reconnect" : "Connect…", () =>
            {
                if (Window.GetWindow(this) is { } owner)
                    host.ConnectClaudeCode(owner);
                Load();
            }, accent: !connected)));
        Add(Ui.Note("Connecting sets Claude Code's status line and keeps your other settings. It needs Claude Code in a terminal: the "
            + "Claude Desktop app doesn't share usage with other apps, though usage there still counts towards the bars."));
    }

    public override void Refresh()
    {
        if (_status is not null)
            _status.Description = host.ClaudeStatus;
    }
}

/// <summary>General: starting with Windows, how the dashboard draws, and where its files are.</summary>
internal sealed class GeneralPage(ISettingsHost host) : CardPage("General", "How Impious Bonum starts and draws, and where it keeps its files.")
{
    protected override void Build()
    {
        Add(Ui.Card("", "Start with Windows", "Start Impious Bonum when you sign in",
            Ui.Switch(StartupRegistration.IsEnabled, "Start with Windows", StartupRegistration.SetEnabled)));
        Add(Ui.Card("", "Draw with the graphics card",
            "Off, the dashboards draw in software, which keeps the graphics driver out of the app and saves a lot of memory. They only redraw once a second. Takes effect when Impious Bonum next starts.",
            Ui.Switch(host.Settings.HardwareRendering, "Draw with the graphics card", host.SetHardwareRendering)));

        Add(Ui.Heading("Files"));
        Add(Ui.Card("", "Settings folder", AppPaths.DataDirectory, Ui.Button("Open", () => Ui.Open(AppPaths.DataDirectory))));
        Add(Ui.Card("", "Dashboard log", "What Impious Bonum has been doing, and any problems. Attach it to a problem report.",
            Ui.Button("Show", TrayIcon.ShowLog)));
    }
}
