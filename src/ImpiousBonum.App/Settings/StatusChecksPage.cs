using System.Windows;
using System.Windows.Automation;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.StatusChecks;

namespace ImpiousBonum.App.Settings;

/// <summary>Status checks: your own servers and services to keep an eye on, each a card saying how it is now.</summary>
internal sealed class StatusChecksPage(ISettingsHost host) : CardPage("Status checks",
    "Whether your own servers and services are up: a NAS, Pi-hole, Plex, the router or a website. The status widget shows them all, and each has readings any widget can show, such as status.nas.up and status.nas.ms.")
{
    private readonly List<(StatusCheck Check, SettingsCard Card)> _cards = [];
    private DateTime _lastStatus;

    /// <summary>Asks for a check, new (null) or to change: the status check window, unless a test swaps it.</summary>
    internal Func<Window?, StatusCheck?, IReadOnlyCollection<string>, StatusCheck?> AskCheck { get; set; } = StatusCheckWindow.Ask;

    /// <summary>Asks for an alert, starting from the one given: the alert window, unless a test swaps it.</summary>
    internal Func<Window?, AlertRule, AlertRule?> AskAlert { get; set; } = (owner, rule) => AlertRuleWindow.Ask(owner, rule, host.Store, host.Layouts.List());

    private AppSettings Settings => host.Settings;

    protected override void Build()
    {
        _cards.Clear();
        if (Settings.StatusChecks.Count == 0)
            Add(Ui.Note("No checks yet. Add one to see whether it's up."));
        foreach (var check in Settings.StatusChecks)
        {
            var alert = Ui.Button("Alert…", () => AddAlert(check));
            alert.ToolTip = "Be told when it goes down";
            // One edited by hand into something that can't be checked has no readings to watch.
            alert.IsEnabled = check.Problem() is null;
            AutomationProperties.SetName(alert, $"Alert me when {check.Name} goes down");
            var edit = Ui.Button("Edit…", () => Edit(check));
            AutomationProperties.SetName(edit, $"Edit {check.Name}");
            var remove = Ui.Button("Remove", () => Change(Settings.StatusChecks.Where(c => c != check).ToList(), $"Removed status check \"{check.Name}\""));
            AutomationProperties.SetName(remove, $"Remove {check.Name}");
            // A globe for a web address, a network cable for a ping.
            var card = Add(Ui.Card(check.IsWeb ? "" : "", check.Name, Describe(check), Ui.Row(alert, edit, remove)));
            _cards.Add((check, card));
        }
        Add(Ui.Card("", "Add a check", null, Ui.Button("Add…", () => Edit(null), accent: Settings.StatusChecks.Count == 0)));
        Add(Ui.Note("Nothing is checked until you add a check, and only the addresses you add here are contacted: a ping, or a request for the page, as often as you say. "
            + "One that fails is checked again 10 seconds later, and only counts as down if that fails too. Checks are kept in settings.json on this PC, never in a layout."));
        Add(Ui.Note("Alert… sets up an alert for when one goes down: a notification, a banner on the dashboard until it's back, a sound and more. "
            + "The status widget's Checks can show some and not others."));
    }

    /// <summary>"Ping nas.local every 60 seconds · Up for 3 h, answering in 4 ms", with how it is now.</summary>
    internal string Describe(StatusCheck check)
    {
        if (check.Problem() is { } problem)
            return $"Not checked: {problem}";
        var every = $"{(check.IsWeb ? "" : "Ping ")}{check.Address.Trim()} every {check.Every} seconds";
        if (check.IsWeb && !string.IsNullOrWhiteSpace(check.Expect))
            every += $", which must say \"{check.Expect.Trim()}\"";
        var status = (host.Store?.GetAttachment(StatusProvider.Checks) as IReadOnlyList<CheckStatus>)
            ?.FirstOrDefault(s => MetricIdPart.From(s.Name).Equals(check.Slug, StringComparison.OrdinalIgnoreCase));
        return status switch
        {
            null => every,
            { Up: null } => $"{every} · Checking…",
            { Up: true, Milliseconds: { } ms } => $"{every} · Up for {status.Since}, answering in {MetricFormatter.Format(ms, MetricUnit.Milliseconds)}",
            { Up: true } => $"{every} · Up for {status.Since}",
            _ => $"{every} · Down for {status.Since}: {status.Detail}",
        };
    }

    private void Edit(StatusCheck? current)
    {
        var others = Settings.StatusChecks.Where(c => c != current).Select(c => c.Name).ToList();
        if (AskCheck(Window.GetWindow(this), current, others) is not { } edited)
            return;
        // A new list rather than a change to the old one: the sampler reads it from another thread.
        Change(current is null ? [.. Settings.StatusChecks, edited] : Settings.StatusChecks.Select(c => c == current ? edited : c).ToList(),
            current is null ? $"Added status check \"{edited.Name}\"" : $"Changed status check \"{edited.Name}\"");
    }

    /// <summary>Not the address: it can say where your servers are, and the log goes with bug reports.</summary>
    private void Change(List<StatusCheck> checks, string log)
    {
        Settings.StatusChecks = checks;
        Settings.Save();
        AppLog.Info(log);
        Load();
    }

    /// <summary>An alert for when <paramref name="check"/> goes down, set up from <see cref="DownAlert"/> in the alert window.</summary>
    private void AddAlert(StatusCheck check)
    {
        if (AskAlert(Window.GetWindow(this), DownAlert(check)) is not { } rule)
            return;
        Settings.Alerts = [.. Settings.Alerts, rule];
        Settings.Save();
        host.AlertsChanged($"Added alert: {rule.Describe()}");
    }

    /// <summary>
    /// A notification as soon as the check is down. It needs no time to wait: a check is only down once it has failed
    /// twice in a row.
    /// </summary>
    internal static AlertRule DownAlert(StatusCheck check) =>
        new(check.IdFor(StatusCheck.Up), Below: 1, For: 0, Message: $"{check.Name.Trim()} is down");

    /// <summary>Every couple of seconds, how each check is now.</summary>
    public override void Refresh()
    {
        if (DateTime.UtcNow - _lastStatus < TimeSpan.FromSeconds(2))
            return;
        _lastStatus = DateTime.UtcNow;
        foreach (var (check, card) in _cards)
            card.Description = Describe(check);
    }
}
