using System.IO;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class WidgetDescriptorTests
{
    public static TheoryData<string> Types => new(WidgetFactory.Descriptors.Select(d => d.Type));

    [Theory]
    [MemberData(nameof(Types))]
    public void Widget_builds_and_refreshes_from_defaults_alone(string type)
    {
        // Constructing reads every setting the widget uses, so this also catches reads of undeclared settings.
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = type }, Theme.From(new ThemeSettings()));
            widget.Refresh(new MetricStore(), DateTime.Now);
        });
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Defaults_match_their_setting_kinds(string type)
    {
        var descriptor = WidgetFactory.Find(type)!;
        Assert.All(Flatten(descriptor.Settings), setting =>
        {
            switch (setting.Kind)
            {
                case SettingKind.Number:
                    Assert.True(setting.Default is null or double, $"{setting.Key}: numeric default expected");
                    Assert.NotNull(setting.Min);
                    Assert.NotNull(setting.Max);
                    if (setting.Default is double d)
                        Assert.InRange(d, setting.Min!.Value, setting.Max!.Value);
                    break;
                case SettingKind.Toggle:
                    Assert.IsType<bool>(setting.Default);
                    break;
                case SettingKind.Choice or SettingKind.Icon:
                    Assert.Contains((string)setting.Default!, setting.Choices!);
                    break;
                case SettingKind.Items:
                    Assert.NotEmpty(setting.ItemSettings!);
                    break;
                default:
                    Assert.True(setting.Default is null or string, $"{setting.Key}: text default expected");
                    break;
            }
        });
    }

    [Fact]
    public void Types_and_keys_are_unique()
    {
        Assert.Equal(WidgetFactory.Descriptors.Count, WidgetFactory.Descriptors.Select(d => d.Type).Distinct().Count());
        Assert.All(WidgetFactory.Descriptors, d =>
        {
            Assert.Equal(d.Settings.Count, d.Settings.Select(s => s.Key).Distinct().Count());
            Assert.DoesNotContain(d.Settings, s => WidgetDescriptor.GeometryKeys.Contains(s.Key));
        });
    }

    [Fact]
    public void Undeclared_settings_cannot_be_read()
    {
        var settings = new WidgetSettings(new JsonObject { ["fontSize"] = 10 }, TextWidget.Descriptor.Settings);
        Assert.Throws<InvalidOperationException>(() => settings.Number("fontsize"));
    }

    [Fact]
    public void Numbers_are_clamped_and_bad_values_fall_back_to_defaults()
    {
        var settings = new WidgetSettings(new JsonObject { ["fontSize"] = 100_000, ["uppercase"] = "yes" }, TextWidget.Descriptor.Settings);
        Assert.Equal(400, settings.Number("fontSize"));
        Assert.False(settings.Bool("uppercase"));
    }

    [Fact]
    public void Widget_reference_doc_is_up_to_date()
    {
        var path = Path.Combine(RepoRoot(), "docs", "widgets.md");
        Assert.True(File.Exists(path), "docs/widgets.md is missing. Run: ImpiousBonum.exe --widget-docs docs/widgets.md");
        Assert.Equal(Normalize(WidgetDocs.ToMarkdown()), Normalize(File.ReadAllText(path)));
    }

    private static IEnumerable<SettingDescriptor> Flatten(IEnumerable<SettingDescriptor> settings) =>
        settings.SelectMany(s => s.ItemSettings is null ? [s] : Flatten(s.ItemSettings).Prepend(s));

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ImpiousBonum.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
