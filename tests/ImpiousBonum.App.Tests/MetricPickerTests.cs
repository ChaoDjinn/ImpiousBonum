using ImpiousBonum.App.Editor;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class MetricPickerTests
{
    private static MetricStore Store()
    {
        var store = new MetricStore();
        store.Register(new MetricDefinition("cpu.load", "CPU load", "CPU", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition("mem.used", "RAM used", "Memory", MetricUnit.Bytes));
        store.Register(new MetricDefinition("hw/amdcpu/0/temperature/2", "Core (Tctl/Tdie) (Temperature)", "AMD Ryzen 7 7700X", MetricUnit.Celsius));
        store.Set("cpu.load", 11.84);
        store.Set("mem.used", 20_079_470_182);
        return store;
    }

    public static TheoryData<MetricUnit> Units => new(Enum.GetValues<MetricUnit>());

    [Theory]
    [MemberData(nameof(Units))]
    public void Every_unit_offers_the_plain_format_first(MetricUnit unit)
    {
        var formats = MetricPickerWindow.FormatsFor(unit);
        Assert.NotEmpty(formats);
        Assert.Equal(string.Empty, formats[0].Spec);
        Assert.Equal(formats.Count, formats.Select(f => f.Spec).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Units))]
    public void Every_format_spec_parses_and_renders(MetricUnit unit)
    {
        var sample = new MetricSample(new MetricDefinition("x", "x", "x", unit), 1234.5, unit == MetricUnit.Text ? "text" : null);
        Assert.All(MetricPickerWindow.FormatsFor(unit), f => Assert.NotEmpty(MetricFormatter.Format(sample, FormatSpec.Parse(f.Spec))));
    }

    [Fact]
    public void Placeholders_include_the_spec_only_when_there_is_one()
    {
        Assert.Equal("{cpu.load}", MetricPickerWindow.Placeholder("cpu.load", ""));
        Assert.Equal("{mem.used:N0 MB}", MetricPickerWindow.Placeholder("mem.used", "N0 MB"));
    }

    [Fact]
    public void Search_matches_all_terms_across_name_id_and_category() => Sta.Run(() =>
    {
        var picker = new MetricPickerWindow(Store(), placeholder: true, currentId: null);

        picker.SearchBox.Text = "ryzen tctl";
        var match = Assert.Single(picker.MetricList.Items.Cast<MetricPickerWindow.MetricRow>());
        Assert.Equal("hw/amdcpu/0/temperature/2", match.Id);

        picker.SearchBox.Text = "";
        Assert.Equal(3, picker.MetricList.Items.Count);
    });

    [Fact]
    public void Friendly_metrics_are_listed_before_raw_sensors() => Sta.Run(() =>
    {
        var picker = new MetricPickerWindow(Store(), placeholder: true, currentId: null);
        var ids = picker.MetricList.Items.Cast<MetricPickerWindow.MetricRow>().Select(r => r.Id).ToList();
        Assert.Equal("hw/amdcpu/0/temperature/2", ids[^1]);
    });

    [Fact]
    public void Template_fields_get_a_placeholder_and_metric_fields_a_bare_id() => Sta.Run(() =>
    {
        var forTemplate = new MetricPickerWindow(Store(), placeholder: true, currentId: null);
        forTemplate.MetricList.SelectedItem = forTemplate.MetricList.Items.Cast<MetricPickerWindow.MetricRow>().First(r => r.Id == "mem.used");
        forTemplate.FormatList.SelectedItem = forTemplate.FormatList.Items.Cast<MetricPickerWindow.FormatOption>().First(o => o.Spec == "N0 MB");
        Assert.Equal("{mem.used:N0 MB}", forTemplate.ResultText.Text);

        var forMetric = new MetricPickerWindow(Store(), placeholder: false, currentId: "cpu.load");
        Assert.Equal("cpu.load", forMetric.ResultText.Text);
    });

    [Fact]
    public void Format_choices_preview_the_live_value() => Sta.Run(() =>
    {
        var picker = new MetricPickerWindow(Store(), placeholder: true, currentId: null);
        picker.MetricList.SelectedItem = picker.MetricList.Items.Cast<MetricPickerWindow.MetricRow>().First(r => r.Id == "cpu.load");
        var examples = picker.FormatList.Items.Cast<MetricPickerWindow.FormatOption>().Select(o => o.Example).ToList();
        Assert.Contains(examples, e => e.StartsWith("11") && e.EndsWith("%"));
        Assert.Contains("12 %", examples);
    });
}
