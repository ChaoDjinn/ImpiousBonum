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
    public void Alerts_are_saved_and_unusable_ones_dropped()
    {
        var settings = new AppSettings { Alerts = [new AlertRule("gpu.hotspot", 95), new AlertRule("cpu.temp")] };
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        Assert.DoesNotContain("\"below\"", json);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)!;
        Assert.Equal(settings.Alerts, restored.Alerts);
    }
}
