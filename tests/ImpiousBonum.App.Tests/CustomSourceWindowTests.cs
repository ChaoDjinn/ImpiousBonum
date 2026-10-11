using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.CustomData;

namespace ImpiousBonum.App.Tests;

public sealed class CustomSourceWindowTests
{
    [Fact]
    public void The_form_builds_a_web_source_and_round_trips_an_existing_one()
    {
        Sta.Run(() =>
        {
            var window = new CustomSourceWindow(null, ["taken"]);
            window.NameBox.Text = "outside";
            window.UrlBox.Text = "http://homeassistant.local:8123/api/states/sensor.outside";
            window.HeadersBox.Text = "Authorization: Bearer abc";
            window.FieldsBox.Text = "temp = state : celsius\nhumidity = attributes.humidity : percent";
            var source = window.Build(out var problem);
            Assert.Null(problem);
            Assert.Equal("Bearer abc", source!.Headers!["Authorization"]);
            Assert.Equal(2, source.Fields!.Count);
            Assert.Null(source.Command);
            window.Close();

            var again = new CustomSourceWindow(source, []);
            Assert.Equal(source.Key, again.Build(out _)!.Key);
            again.Close();
        });
    }

    [Fact]
    public void The_form_explains_what_is_missing()
    {
        Sta.Run(() =>
        {
            var window = new CustomSourceWindow(null, ["taken"]);
            window.NameBox.Text = "taken";
            window.CommandChoice.IsChecked = true;
            window.CommandBox.Text = "powershell";
            Assert.Null(window.Build(out var problem));
            Assert.Contains("already", problem);

            window.NameBox.Text = "status";
            window.EveryBox.Text = "1";
            Assert.Null(window.Build(out problem));
            Assert.Contains("every 5 seconds", problem);

            window.EveryBox.Text = "60";
            var source = window.Build(out problem)!;
            Assert.Null(problem);
            Assert.Equal("powershell", source.Command);
            Assert.Null(source.Url);
            Assert.Null(source.Headers);
            Assert.Equal(new CustomField("value"), Assert.Single(source.FieldsOrValue));
            window.Close();
        });
    }
}
