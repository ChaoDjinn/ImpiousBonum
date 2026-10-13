using System.IO;
using System.Text.Json;
using System.Windows;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App;

// Where readings come from beyond the PC: the sensor service, weather, calendars, news, Claude Code and custom data.
public partial class App
{
    private string SensorStatus() =>
        _sampler is not null && _sampler.Store.TryGet(SensorHostProvider.Status, out var status) && status.Text is { } text
            ? text
            : SensorHostProvider.NotRunning;

    /// <summary>"Leeds: 12° Light rain", then the air quality and pollen and any warnings when they're on, or what's missing.</summary>
    private string WeatherStatus()
    {
        if (_settings.WeatherLocation is not { } place)
            return "No location set";
        var store = _sampler?.Store;
        if (store is null || !store.TryGet(WeatherProvider.Temperature, out var temperature) || temperature.Value is null)
            return $"{place.Name.Split(',')[0]}: waiting for the forecast";
        store.TryGet(WeatherProvider.Condition, out var condition);
        var weather = $"{place.Name.Split(',')[0]}: {MetricFormatter.Format(temperature)} {condition.Text}".TrimEnd();
        if (_settings.AirQuality)
            weather += AirQualityStatus(store);
        if (_settings.WeatherWarnings && store.TryGet(WeatherWarningsProvider.Count, out var warnings) && warnings.Value is double count && count > 0)
            weather += count == 1 ? " · 1 warning" : $" · {count} warnings";
        return weather;
    }

    /// <summary>
    /// "From the Met Office for Wales: none now", or with the most serious warning instead of "none now"; or why there
    /// are none, such as a place outside the UK and the US.
    /// </summary>
    private string WeatherWarningsStatus()
    {
        if (_settings.WeatherLocation is not { } location || _settings.WeatherWarningsPlace() is not { } place)
            return "Set a location first.";
        if (place.Source == WarningSource.None)
        {
            var only = $"Not available for {location.Name.Split(',')[0]} yet: only for places in the UK and the US.";
            // Without a country, the name or the coordinates have to say: a US place given as coordinates can't.
            return location.CountryCode is null ? only + " If it's in one of them, set the location again by searching for its name." : only;
        }

        var from = place.Source == WarningSource.MetOffice ? $"From the Met Office for {MetOfficeWarnings.Find(place.Area)?.Name}" : "From the National Weather Service";
        var store = _sampler?.Store;
        if (!_settings.WeatherWarnings)
            return from;
        if (store is null || !store.TryGet(WeatherWarningsProvider.Count, out var count) || count.Value is not double warnings)
            return $"{from}: waiting for the warnings";
        if (warnings == 0)
            return $"{from}: none now";
        store.TryGet(WeatherWarningsProvider.Headline, out var headline);
        return warnings == 1 ? $"{from}: {headline.Text}" : $"{from}: {headline.Text}, and {warnings - 1} more";
    }

    /// <summary>" · Air quality: Good · Pollen: Grass low", as far as there are readings.</summary>
    private static string AirQualityStatus(MetricStore store)
    {
        var status = "";
        if (store.TryGet(AirQualityProvider.Level, out var level) && level.Text is { } words)
            status += $" · Air quality: {words}";
        if (store.TryGet(AirQualityProvider.Pollen, out var pollen) && pollen.Text is { } summary)
            status += $" · Pollen: {summary}";
        return status;
    }

    /// <summary>The news widget's button in the editor.</summary>
    private void EditNews(Window? owner = null)
    {
        if (LinkListWindow.AskFeeds(_settings.NewsFeeds, owner) is { } feeds)
            SetNewsFeeds(feeds);
    }

    /// <summary>The list is replaced, not changed in place, since the sampler reads it.</summary>
    private void SetNewsFeeds(IReadOnlyList<NewsFeed> feeds)
    {
        _settings.NewsFeeds = [.. feeds];
        _settings.Save();
        AppLog.Info(feeds.Count == 0 ? "News feeds: none" : $"News feeds: {string.Join(", ", feeds.Select(f => f.Name))}");
    }

    /// <summary>The agenda widget's button in the editor.</summary>
    private void EditCalendars(Window? owner = null)
    {
        if (LinkListWindow.AskCalendars(_settings.Calendars, owner) is { } links)
            SetCalendars(links);
    }

    /// <summary>The list is replaced, not changed in place, since the sampler reads it.</summary>
    private void SetCalendars(IReadOnlyList<CalendarLink> links)
    {
        _settings.Calendars = [.. links];
        _settings.Save();
        AppLog.Info(links.Count == 0 ? "Calendars: none" : $"Calendars: {string.Join(", ", links.Select(l => l.Name))}");
    }

    /// <summary>The Weather page's Set location…, or adding a weather widget when there's no location yet.</summary>
    private void SetWeatherLocation(Window? owner)
    {
        if (WeatherLocationWindow.Ask(owner, _settings.WeatherLocation) is not { } place)
            return;
        _settings.WeatherLocation = place;
        // An area chosen for the last place is unlikely to suit this one.
        _settings.MetOfficeArea = AppSettings.MetOfficeAreaAuto;
        _settings.Save();
        AppLog.Info("Weather location set");
        _tray?.ShowInfo("Weather", $"Forecasts for {place.Name} will appear in a few seconds.");
    }

    private string ClaudeStatus() =>
        _sampler is not null && _sampler.Store.TryGet(ClaudeUsageProvider.Status, out var status) && status.Text is { } text
            ? text
            : "Not connected to Claude Code";

    private static bool IsClaudeCodeConnected()
    {
        try
        {
            var path = ClaudeCodeSetup.SettingsPath;
            return File.Exists(path) && ClaudeCodeSetup.Inspect(File.ReadAllText(path)).Kind == ClaudeCodeSetup.StatusLine.Ours;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The Claude usage widget has nothing to show until Claude Code's status line feeds it, and that step is easy to
    /// miss, so adding the widget offers it when it isn't set up yet.
    /// </summary>
    private void OfferClaudeCodeConnection(Window owner)
    {
        if (IsClaudeCodeConnected())
            return;
        var answer = MessageBox.Show(owner,
            "The Claude usage widget gets your plan usage from Claude Code's status line, which isn't set up yet.\n\n"
            + "Connect Claude Code now? This sets the status line in Claude Code's settings and keeps your other settings.\n\n"
            + "The numbers come from Claude Code in a terminal. The Claude Desktop app doesn't share usage with other apps, "
            + "though usage there still counts towards the bars.",
            "Impious Bonum", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Yes)
            ConnectClaudeCode(owner);
    }

    /// <summary>
    /// Sets Claude Code's status line to the sensor host's <c>claude-statusline</c> command, which feeds the Claude usage
    /// metrics. Asks before replacing a status line someone else set up, and keeps a copy of the file it changes.
    /// </summary>
    /// <param name="owner">The window to show the "replace your status line?" question over, if any.</param>
    private void ConnectClaudeCode(Window? owner = null)
    {
        var helper = ClaudeCodeSetup.HelperPath;
        if (!File.Exists(helper))
        {
            _tray?.ShowError("Couldn't connect Claude Code", $"{helper} is missing. Reinstalling Impious Bonum puts it back.");
            return;
        }

        var path = ClaudeCodeSetup.SettingsPath;
        try
        {
            var existing = File.Exists(path) ? File.ReadAllText(path) : null;
            var (kind, command) = ClaudeCodeSetup.Inspect(existing);
            if (kind == ClaudeCodeSetup.StatusLine.Other)
            {
                var question =
                    $"Claude Code already has a status line:\n\n{command}\n\nReplace it with Impious Bonum's? It shows the model, context use "
                    + "and your plan usage, and passes the usage to the dashboard.\n\nTo keep yours, choose No: the user guide's Claude usage "
                    + "section shows the line to add to your own script.";
                var answer = owner is null
                    ? MessageBox.Show(question, "Impious Bonum", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                    : MessageBox.Show(owner, question, "Impious Bonum", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                    return;
            }

            var updated = ClaudeCodeSetup.Apply(existing, ClaudeCodeSetup.Command(helper, ClaudeCodeSetup.GitBashInstalled()));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (existing is not null)
                File.WriteAllText(path + ".impiousbonum.bak", existing);
            File.WriteAllText(path, updated);
            AppLog.Info($"Set Claude Code's status line in {path}");
            _tray?.ShowInfo("Claude Code connected", "Usage appears after Claude Code's next reply in a terminal. If Claude Code is already open, restart it.");
        }
        catch (JsonException ex)
        {
            _tray?.ShowError("Couldn't read Claude Code's settings", $"{path}: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _tray?.ShowError("Couldn't update Claude Code's settings", ex.Message);
        }
    }

    private async Task ChangeSensorServiceAsync(bool install)
    {
        var error = await SensorServiceControl.RunAsync(install);
        AppLog.Info($"Sensor service {(install ? "install" : "removal")}: {error ?? "done"}");
        if (error is null)
            _tray?.ShowInfo("Impious Bonum", install ? "Sensor service installed. Temperatures will appear in a few seconds." : "Sensor service removed.");
        else if (error != "Cancelled.")
            _tray?.ShowError(install ? "Couldn't install the sensor service" : "Couldn't remove the sensor service", error);
    }
}
