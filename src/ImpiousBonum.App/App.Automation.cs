using System.IO;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.Logging;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Sessions;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App;

// What acts on readings and keys by itself: alerts, weather warnings, logging, session recaps and hotkeys.
public partial class App
{
    private readonly AlertMonitor _alerts = new();
    private AlertActions? _alertActions;

    /// <summary>
    /// Checks the alert rules each second: notifies and does what else an alert asks for when one goes off, and keeps
    /// the dashboard's banner current.
    /// </summary>
    private void UpdateAlerts()
    {
        if (_sampler is null || _window is null)
            return;
        foreach (var alert in _alerts.Update(_settings.Alerts, _sampler.Store, DateTime.UtcNow))
        {
            AppLog.Info(alert.Started ? $"Alert: {alert.Rule.Describe()}: {alert.Text}" : $"Alert over: {alert.Rule.Describe()}");
            if (!alert.Started)
                continue;
            if (alert.Rule.Notify)
                _tray?.ShowAlert(alert.Text);
            // Made-up readings mustn't run commands or tell a smart home anything.
            if (_demo)
                continue;
            _alertActions ??= new AlertActions(SwitchLayoutFromAlert, problem =>
            {
                AppLog.Warning($"Alert: {problem}");
                _tray?.ShowError("Impious Bonum", problem);
            });
            _alertActions.Run(alert.Rule, alert.Text, _sampler.Store);
        }
        _window.ShowAlerts(_alerts.Active.Where(rule => rule.Flash).Select(rule => (rule.Describe(), rule.Text(_sampler.Store))).ToList());
    }

    private string? SwitchLayoutFromAlert(string name)
    {
        if (_layouts?.Library.Find(name) is not { } found)
            return $"There's no layout called \"{name}\" any more. Change the alert on Impious Bonum's Alerts page.";
        SelectLayout(found);
        return null;
    }

    private readonly WarningNotifications _warningNotices = new();
    private object? _warningsSeen;

    /// <summary>Each tick: a notification of any new weather warning, if they're wanted. Clicking it opens the warning.</summary>
    private void UpdateWeatherWarnings()
    {
        if (_sampler is null || _demo || !_settings.WeatherWarnings || !_settings.WeatherWarningsNotify)
            return;
        var warnings = _sampler.Store.GetAttachment(WeatherWarningsProvider.Warnings) as IReadOnlyList<WeatherWarning>;
        // The list is only replaced on the weather's samples, so most ticks have nothing new to look at.
        if (ReferenceEquals(warnings, _warningsSeen))
            return;
        _warningsSeen = warnings;
        if (_warningNotices.Next(warnings) is not { } notice)
            return;
        // Not the headline: it says where you are, and the log goes with bug reports.
        AppLog.Info("Weather warning notified");
        _tray?.ShowWarning(notice.Title, notice.Message, notice.Link is { } link ? () => Ui.Open(link) : null);
    }

    /// <summary>Each tick: feeds the logger, writes a row when a period ends, and once a day removes old files.</summary>
    private void UpdateLogging()
    {
        var logging = _settings.Logging;
        if (!logging.Enabled || logging.Metrics.Count == 0 || _sampler is null)
        {
            _logger = null;
            return;
        }
        var now = DateTime.Now;
        _logger ??= new MetricLogger([.. logging.Metrics], TimeSpan.FromSeconds(Math.Max(1, logging.EverySeconds)));
        _csvLog ??= new CsvLog(AppPaths.LogsFolder);
        var store = _sampler.Store;
        if (_logger.Add(id => store.TryGet(id, out var sample) ? sample.Value : null, now) is not { } row)
            return;
        try
        {
            _csvLog.Append(_logger.Header, row, now);
            if (_prunedOn != now.Date)
            {
                _prunedOn = now.Date;
                if (CsvLog.Prune(AppPaths.LogsFolder, logging.KeepDays, now) is > 0 and var pruned)
                    AppLog.Info($"Logging: removed {pruned} old log file(s)");
            }
            _logFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Said once until it works again, not every period.
            if (!_logFailed)
                AppLog.Warning($"Couldn't write the log in {AppPaths.LogsFolder}: {ex.Message}");
            _logFailed = true;
        }
    }

    /// <summary>A game session ended: tell the user how it went and add it to sessions.csv.</summary>
    private void OnSessionEnded(SessionRecap recap)
    {
        AppLog.Info($"Session recap: {recap.Describe().ReplaceLineEndings("; ")}");
        try
        {
            SessionLog.Append(AppPaths.SessionsFile, recap);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"Couldn't add the session to {AppPaths.SessionsFile}: {ex.Message}");
        }
        _tray?.ShowInfo("Session recap", recap.Describe());
    }

    /// <summary>Registers the hotkeys from settings; any that can't be set are reported once.</summary>
    private void ApplyHotkeys()
    {
        if (_hotkeys is null)
            return;
        var problems = _hotkeys.Apply(_settings.Hotkeys);
        foreach (var problem in problems)
            AppLog.Warning($"Hotkey not set: {problem}");
        if (problems.Count > 0)
            _tray?.ShowError(problems.Count == 1 ? "A hotkey isn't working" : "Some hotkeys aren't working", string.Join("\n", problems));
    }

    private void RunHotkey(Hotkey hotkey)
    {
        // Settings have the latest: a layout may have been renamed since the keys were registered.
        hotkey = _settings.Hotkeys.FirstOrDefault(h => string.Equals(h.Keys, hotkey.Keys, StringComparison.OrdinalIgnoreCase)) ?? hotkey;
        switch (hotkey.Action)
        {
            case Hotkey.ShowHide:
                ShowOrHide();
                break;
            case Hotkey.NextPage:
                TurnPage(1);
                break;
            case Hotkey.PreviousPage:
                TurnPage(-1);
                break;
            case Hotkey.Edit:
                OpenEditor();
                break;
            case Hotkey.SwitchLayout when hotkey.Layout is { } name:
                if (_layouts?.Library.Find(name) is { } found)
                    SelectLayout(found);
                else
                    _tray?.ShowError("Impious Bonum", $"There's no layout called \"{name}\" any more. Change its hotkey on Impious Bonum's Hotkeys page.");
                break;
        }
    }
}
