using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Tests;

public sealed class LayoutValidatorTests
{
    private static IReadOnlyList<string> Validate(params JsonObject[] widgets) =>
        LayoutValidator.Validate(new LayoutDocument { Widgets = [.. widgets] });

    private static JsonObject Widget(string type, params (string Key, JsonNode? Value)[] settings)
    {
        var widget = new JsonObject { ["type"] = type, ["x"] = 0, ["y"] = 0, ["width"] = 100, ["height"] = 50 };
        foreach (var (key, value) in settings)
            widget[key] = value;
        return widget;
    }

    [Fact]
    public void The_default_layout_is_clean() =>
        Assert.Empty(LayoutValidator.Validate(LayoutStore.LoadDefault()));

    [Fact]
    public void Unknown_type_suggests_the_closest()
    {
        var issue = Assert.Single(Validate(Widget("grpah")));
        Assert.Contains("did you mean 'graph'", issue);
    }

    [Fact]
    public void Wrong_case_key_suggests_the_right_one()
    {
        var issue = Assert.Single(Validate(Widget("text", ("fontsize", 20))));
        Assert.Contains("unknown setting 'fontsize' (did you mean 'fontSize'?)", issue);
    }

    [Fact]
    public void Wrong_types_ranges_and_choices_are_reported()
    {
        var issues = Validate(Widget("text", ("fontSize", "big"), ("align", "middle"), ("uppercase", 1), ("color", "orangey")));
        Assert.Equal(4, issues.Count);
        Assert.Contains(issues, i => i.Contains("'fontSize' should be a number"));
        Assert.Contains(issues, i => i.Contains("'align' should be one of left, center, right"));
        Assert.Contains(issues, i => i.Contains("'uppercase' should be true or false"));
        Assert.Contains(issues, i => i.Contains("'color' should be foreground, secondary, accent"));

        var range = Assert.Single(Validate(Widget("text", ("fontSize", 5000))));
        Assert.Contains("between 6 and 400", range);
    }

    [Fact]
    public void List_items_are_checked_too()
    {
        var rows = new JsonArray(new JsonObject { ["label"] = "Ping", ["txt"] = "{net.ping}" });
        var issue = Assert.Single(Validate(Widget("rows", ("rows", rows))));
        Assert.Contains("rows[1]: unknown setting 'txt' (did you mean 'text'?)", issue);
    }

    [Fact]
    public void Missing_geometry_is_reported()
    {
        var widget = Widget("clock");
        widget.Remove("width");
        Assert.Contains(Validate(widget), i => i.Contains("missing 'width'"));
    }
}
