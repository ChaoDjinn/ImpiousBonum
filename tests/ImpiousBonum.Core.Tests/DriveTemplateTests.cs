using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Tests;

public sealed class DriveTemplateTests
{
    [Theory]
    [InlineData("{free} free / {total}", "{disk.D.free} free / {disk.D.total}")]
    [InlineData("{letter}:/", "D:/")]
    [InlineData("{life:0} % life, {temp}", "{disk.D.life:0} % life, {disk.D.temp}")]
    [InlineData("{USEDPCT}", "{disk.D.usedPct}")]
    [InlineData("{disk.*.read} · {cpu.temp}", "{disk.D.read} · {cpu.temp}")]
    [InlineData("{label}", "{disk.D.label}")]
    public void Row_text_becomes_a_template_for_that_drive(string template, string expected) =>
        Assert.Equal(expected, DriveTemplate.Expand(template, "D"));

    [Theory]
    [InlineData("{disk.C.life:0}", "{life:0}")]
    [InlineData("{disk.C.usedpct}", "{usedPct}")]
    [InlineData("{disk.E.something}", "{disk.*.something}")]
    [InlineData("{cpu.load}", "{cpu.load}")]
    [InlineData("{disk.read}", "{disk.read}")]
    public void Picked_drive_metrics_follow_the_row(string picked, string expected) =>
        Assert.Equal(expected, DriveTemplate.ForRow(picked));
}
