using System.Windows;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.CustomData;

namespace ImpiousBonum.App.Settings;

/// <summary>Custom data: your own metrics from web addresses and commands, each source a card with its last status.</summary>
internal sealed class CustomDataPage(ISettingsHost host) : CardPage("Custom data",
    "Your own metrics from web addresses (JSON or text) and commands: Home Assistant sensors, a server's status, a script's output. Each field becomes a metric called custom.source.field that any widget can show.")
{
    private readonly List<(CustomSource Source, SettingsCard Card)> _cards = [];
    private DateTime _lastStatus;

    private AppSettings Settings => host.Settings;

    protected override void Build()
    {
        _cards.Clear();
        if (Settings.CustomData.Count == 0)
            Add(Ui.Note("No sources yet. Add a web address or a command to read."));
        foreach (var source in Settings.CustomData)
        {
            var card = Add(Ui.Card(source.IsWeb ? "" : "", source.Name, Describe(source),
                Ui.Row(Ui.Button("Edit…", () => Edit(source)), Ui.Button("Remove", () => Remove(source)))));
            _cards.Add((source, card));
        }
        Add(Ui.Card("", "Add a source", null, Ui.Button("Add…", () => Edit(null), accent: Settings.CustomData.Count == 0)));
        Add(Ui.Note("Sources are kept in settings.json on this PC, never in a layout, so a layout from someone else can't add one. "
            + "Headers such as access tokens are stored there as plain text."));
    }

    /// <summary>"cpu, ram from https://… · OK", with the source's latest status.</summary>
    private string Describe(CustomSource source)
    {
        var from = source.IsWeb ? source.Url : $"{source.Command} {source.Arguments}".Trim();
        var fields = source.Fields is { Count: > 0 } list ? string.Join(", ", list.Select(f => f.Name)) : "value";
        var status = host.CustomSourceStatus(source.StatusId) is { } text ? $" · {text}" : string.Empty;
        return $"{fields} from {from}{status}";
    }

    private void Edit(CustomSource? current)
    {
        var others = Settings.CustomData.Where(s => s != current).Select(s => s.Name).ToList();
        if (CustomSourceWindow.Ask(Window.GetWindow(this), current, others) is not { } edited)
            return;
        // A new list rather than a change to the old one: the sampler reads it from another thread.
        Settings.CustomData = current is null ? [.. Settings.CustomData, edited] : Settings.CustomData.Select(s => s == current ? edited : s).ToList();
        Save(current is null ? $"Added custom data source \"{edited.Name}\"" : $"Changed custom data source \"{edited.Name}\"");
    }

    private void Remove(CustomSource source)
    {
        Settings.CustomData = Settings.CustomData.Where(s => s != source).ToList();
        Save($"Removed custom data source \"{source.Name}\"");
    }

    private void Save(string log)
    {
        Settings.Save();
        AppLog.Info(log);
        Load();
    }

    /// <summary>Every couple of seconds, each source's latest status.</summary>
    public override void Refresh()
    {
        if (DateTime.UtcNow - _lastStatus < TimeSpan.FromSeconds(2))
            return;
        _lastStatus = DateTime.UtcNow;
        foreach (var (source, card) in _cards)
            card.Description = Describe(source);
    }
}

/// <summary>Alerts: rules for being told when a reading stays out of bounds, each a card.</summary>
internal sealed class AlertsPage(ISettingsHost host) : CardPage("Alerts",
    "Be told when a reading stays out of bounds, such as the GPU hot spot above 95 °C for 10 seconds: a Windows notification, a banner flashing on the dashboard, or both.")
{
    private AppSettings Settings => host.Settings;

    protected override void Build()
    {
        if (Settings.Alerts.Count == 0)
            Add(Ui.Note("No alerts yet. Add one to be told when a reading is out of bounds."));
        foreach (var rule in Settings.Alerts)
        {
            var does = string.Join(" and ", new[] { rule.Notify ? "notify" : null, rule.Flash ? "flash on the dashboard" : null }.OfType<string>());
            Add(Ui.Card("", rule.Describe(), string.IsNullOrWhiteSpace(rule.Message) ? $"Will {does}" : $"Will {does}: {rule.Message}",
                Ui.Row(Ui.Button("Edit…", () => Edit(rule)), Ui.Button("Remove", () => Change(Settings.Alerts.Where(r => !ReferenceEquals(r, rule)).ToList(), $"Removed alert: {rule.Describe()}")))));
        }
        Add(Ui.Card("", "Add an alert", null, Ui.Button("Add…", () => Edit(null), accent: Settings.Alerts.Count == 0)));
        Add(Ui.Note("Each alert goes off once, then again only after the reading has been back to normal for 30 seconds."));
    }

    private void Edit(Core.Alerts.AlertRule? current)
    {
        if (AlertRuleWindow.Ask(Window.GetWindow(this), current, host.Store) is not { } edited)
            return;
        Change(current is null ? [.. Settings.Alerts, edited] : Settings.Alerts.Select(r => ReferenceEquals(r, current) ? edited : r).ToList(),
            current is null ? $"Added alert: {edited.Describe()}" : $"Changed alert: {edited.Describe()}");
    }

    private void Change(List<Core.Alerts.AlertRule> alerts, string log)
    {
        Settings.Alerts = alerts;
        Settings.Save();
        host.AlertsChanged(log);
        Load();
    }
}
