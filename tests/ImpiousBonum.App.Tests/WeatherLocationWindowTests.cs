using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Tests;

public sealed class WeatherLocationWindowTests
{
    private static readonly WeatherPlace WestDerby =
        new("West Derby", "England", "United Kingdom", 53.4325964, -2.9095592) { Areas = ["West Derby", "Liverpool"], CountryCode = "GB" };

    [Fact]
    public void Places_from_OpenStreetMap_are_credited_and_picked_by_their_coordinates()
    {
        Sta.Run(() =>
        {
            var searches = new List<string>();
            var fromOpenStreetMap = true;
            var window = new WeatherLocationWindow(null, (query, _) =>
            {
                searches.Add(query);
                return Task.FromResult(new PlaceSearchResults([WestDerby], fromOpenStreetMap));
            });

            // Typing searches nothing; Enter or Search does.
            window.QueryBox.Text = "West Derby, Liverpool";
            Assert.Empty(searches);
            window.SearchAsync().GetAwaiter().GetResult();
            Assert.Equal(["West Derby, Liverpool"], searches);

            var item = Assert.IsType<ListBoxItem>(Assert.Single(window.Results.Items));
            Assert.Equal("West Derby, Liverpool, England, United Kingdom", item.Content);
            Assert.Equal("53.433, -2.91", item.ToolTip);
            Assert.Equal(Visibility.Visible, window.Attribution.Visibility);
            Assert.Equal(new WeatherLocation("West Derby, Liverpool, England, United Kingdom", 53.4325964, -2.9095592), window.Picked());

            // Open-Meteo's own places need no credit to OpenStreetMap.
            fromOpenStreetMap = false;
            window.SearchAsync().GetAwaiter().GetResult();
            Assert.Equal(Visibility.Collapsed, window.Attribution.Visibility);
            Assert.Single(window.Results.Items);
            window.Close();
        });
    }

    [Fact]
    public void Coordinates_are_used_as_they_are_without_searching()
    {
        Sta.Run(() =>
        {
            var searches = 0;
            var window = new WeatherLocationWindow(null, (_, _) =>
            {
                searches++;
                return Task.FromResult(new PlaceSearchResults([WestDerby], true));
            });
            window.QueryBox.Text = "Liverpool";
            window.SearchAsync().GetAwaiter().GetResult();
            Assert.Equal(Visibility.Visible, window.Attribution.Visibility);

            // Switching to coordinates clears the places, and their credit with them.
            window.QueryBox.Text = "51.5, -0.12";
            window.SearchAsync().GetAwaiter().GetResult();
            Assert.Equal(1, searches);
            Assert.Empty(window.Results.Items);
            Assert.Equal(Visibility.Collapsed, window.Attribution.Visibility);
            Assert.Equal(new WeatherLocation("51.5, -0.12", 51.5, -0.12), window.Picked());
            Assert.True(window.OkButton.IsEnabled);
            window.Close();
        });
    }

    [Fact]
    public void Enter_searches_again_once_the_search_has_changed()
    {
        Sta.Run(() =>
        {
            var window = new WeatherLocationWindow(null, (_, _) => Task.FromResult(new PlaceSearchResults([WestDerby], false)));
            window.QueryBox.Text = "West Derby";
            Assert.True(window.EnterSearches);
            window.SearchAsync().GetAwaiter().GetResult();

            // With the places for what's typed listed, Enter takes the one picked.
            Assert.NotNull(window.Results.SelectedItem);
            Assert.False(window.EnterSearches);

            // Adding the town after a comma, as the hint says, makes Enter search again.
            window.QueryBox.Text = "West Derby, Liverpool";
            Assert.True(window.EnterSearches);
            window.SearchAsync().GetAwaiter().GetResult();
            Assert.False(window.EnterSearches);

            // Coordinates are used as they are, never searched for.
            window.QueryBox.Text = "53.43, -2.91";
            Assert.False(window.EnterSearches);
            window.Close();
        });
    }

    [Fact]
    public void Nothing_found_says_so_and_shows_no_credit()
    {
        Sta.Run(() =>
        {
            var window = new WeatherLocationWindow(null, (_, _) => Task.FromResult(new PlaceSearchResults([], true)));
            window.QueryBox.Text = "Nowhere at all";
            window.SearchAsync().GetAwaiter().GetResult();
            Assert.Empty(window.Results.Items);
            Assert.Equal(Visibility.Collapsed, window.Attribution.Visibility);
            Assert.Contains("Nothing found for \"Nowhere at all\"", window.StatusText.Text);
            Assert.Null(window.Picked());
            window.Close();
        });
    }
}
