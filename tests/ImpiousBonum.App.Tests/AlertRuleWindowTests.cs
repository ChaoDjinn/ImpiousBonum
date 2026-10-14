using System.Text.Json;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Alerts;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class AlertRuleWindowTests
{
    [Fact]
    public void The_form_builds_an_alert_and_shows_the_reading_now()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.Register(new MetricDefinition("gpu.hotspot", "GPU hot spot temperature", "Sensors", MetricUnit.Celsius));
            store.Set("gpu.hotspot", 71);
            var window = new AlertRuleWindow(null, store);
            window.MetricBox.Text = "gpu.hotspot";
            window.AboveBox.Text = "95";
            window.FlashCheck.IsChecked = true;

            Assert.Contains("71.0 °C", window.NowText.Text);
            Assert.Equal(new AlertRule("gpu.hotspot", 95, null, 10, null, true, true), window.Build(out var problem));
            Assert.Null(problem);

            window.AboveCheck.IsChecked = false;
            Assert.Null(window.Build(out problem));
            Assert.Contains("Above, Below", problem);

            window.BelowCheck.IsChecked = true;
            window.BelowBox.Text = "lots";
            Assert.Null(window.Build(out problem));
            Assert.Contains("number", problem);
            window.Close();

            var rule = new AlertRule("fps", null, 30, 5, "Low FPS: {fps}", Notify: false, Flash: true);
            var again = new AlertRuleWindow(rule, store);
            Assert.Equal(rule, again.Build(out _));
            again.Close();
        });
    }

    [Fact]
    public void The_form_sets_up_what_happens_when_it_goes_off()
    {
        Sta.Run(() =>
        {
            var window = new AlertRuleWindow(null, null, ["Main", "Gaming"]);
            window.MetricBox.Text = "gpu.hotspot";
            window.AboveBox.Text = "95";
            window.NotifyCheck.IsChecked = false;
            Assert.Null(window.Build(out var problem));
            Assert.Contains("at least one thing", problem);

            window.LayoutCheck.IsChecked = true;
            Assert.Null(window.Build(out problem));
            Assert.Contains("layout", problem);
            window.LayoutBox.SelectedItem = "Gaming";

            window.WebhookCheck.IsChecked = true;
            window.WebhookBox.Text = "ntfy.sh/my-pc";
            Assert.Null(window.Build(out problem));
            Assert.Contains("http", problem);
            window.WebhookBox.Text = "https://ntfy.sh/my-pc";
            window.WebhookTextCheck.IsChecked = true;

            window.CommandCheck.IsChecked = true;
            window.CommandBox.Text = "powershell.exe";
            window.ArgumentsBox.Text = @"-File C:\Scripts\fans.ps1";
            window.SoundCheck.IsChecked = true;

            var rule = window.Build(out problem)!;
            Assert.Null(problem);
            Assert.Equal(new AlertRule("gpu.hotspot", 95, Notify: false, Sound: "Exclamation", Layout: "Gaming",
                Command: "powershell.exe", Arguments: @"-File C:\Scripts\fans.ps1", Webhook: "https://ntfy.sh/my-pc", WebhookText: true), rule);

            // Unticking leaves the text behind but doesn't save it.
            window.CommandCheck.IsChecked = false;
            Assert.Null(window.Build(out _)!.Command);
            Assert.Null(window.Build(out _)!.Arguments);
            window.Close();

            // A layout that's been deleted since stays picked rather than quietly changing.
            var again = new AlertRuleWindow(rule with { Layout = "Old" }, null, ["Main"]);
            Assert.Equal(rule with { Layout = "Old" }, again.Build(out _));
            again.Close();
        });
    }

    [Fact]
    public void Actions_switch_layouts_run_commands_and_report_problems()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.Register(new MetricDefinition("gpu.hotspot", "GPU hot spot temperature", "Sensors", MetricUnit.Celsius));
            store.Set("gpu.hotspot", 97);
            var switched = new List<string>();
            var problems = new List<string>();
            var actions = new AlertActions(name =>
            {
                switched.Add(name);
                return name == "Gone" ? "There's no layout called \"Gone\"" : null;
            }, problems.Add);

            actions.Run(new AlertRule("gpu.hotspot", 95, Layout: " Gaming "), "Hot", store);
            actions.Run(new AlertRule("gpu.hotspot", 95, Layout: "Gone", Sound: @"C:\nowhere\alarm.wav"), "Hot", store);
            Assert.Equal(["Gaming", "Gone"], switched);
            Assert.Equal([@"There's no sound file C:\nowhere\alarm.wav.", "There's no layout called \"Gone\""], problems);

            // The alert's text goes in the environment, never the command line.
            var start = AlertActions.StartInfo(new AlertRule("gpu.hotspot", 95, Command: "%SystemRoot%\\System32\\cmd.exe", Arguments: "/c exit"), "Hot \" & del *", store);
            Assert.Equal(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\cmd.exe"), start.FileName);
            Assert.Equal("/c exit", start.Arguments);
            Assert.True(start.CreateNoWindow);
            Assert.Equal("Hot \" & del *", start.Environment[AlertRule.MessageVariable]);
            Assert.Equal("97", start.Environment[AlertRule.ValueVariable]);
        });
    }

    [Fact]
    public void Alerts_are_saved_and_unusable_ones_dropped()
    {
        var settings = new AppSettings
        {
            Alerts = [new AlertRule("gpu.hotspot", 95), new AlertRule("cpu.temp"), new AlertRule("fps", Below: 30, Notify: false, Layout: "Gaming", Webhook: "https://ntfy.sh/pc", WebhookText: true)],
        };
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        Assert.DoesNotContain("\"below\":null", json.Replace(" ", ""));
        Assert.DoesNotContain("\"command\"", json);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"webhookText\""));
        var restored = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)!;
        Assert.Equal(settings.Alerts, restored.Alerts);
    }
}
