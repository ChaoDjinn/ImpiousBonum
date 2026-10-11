using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Shell;

/// <summary>Finds a place by name (or takes coordinates) for the weather. Searching is the only time a name is sent anywhere.</summary>
public partial class WeatherLocationWindow : Window
{
    private CancellationTokenSource? _search;

    private WeatherLocationWindow(WeatherLocation? current)
    {
        InitializeComponent();
        QueryBox.Text = current?.Name.Split(',')[0] ?? string.Empty;
        Loaded += (_, _) =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        };
        Closed += (_, _) => _search?.Cancel();
    }

    /// <returns>The place picked, or null if cancelled.</returns>
    public static WeatherLocation? Ask(Window? owner, WeatherLocation? current)
    {
        var window = new WeatherLocationWindow(current);
        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return window.ShowDialog() == true ? window.Picked() : null;
    }

    private WeatherLocation? Picked()
    {
        if (Results.SelectedItem is ListBoxItem { Tag: WeatherPlace place })
            return new WeatherLocation(place.Description, place.Latitude, place.Longitude);
        if (OpenMeteo.ParseCoordinates(QueryBox.Text) is var (latitude, longitude))
            return new WeatherLocation(QueryBox.Text.Trim(), latitude, longitude);
        return null;
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        var coordinates = OpenMeteo.ParseCoordinates(QueryBox.Text) is not null;
        if (coordinates)
        {
            Results.Items.Clear();
            StatusText.Text = "These coordinates will be used as they are.";
        }
        OkButton.IsEnabled = coordinates || Results.SelectedItem is not null;
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        // Enter searches; picking a result and pressing Enter again uses it.
        if (e.Key == Key.Enter && OpenMeteo.ParseCoordinates(QueryBox.Text) is null && Results.SelectedItem is null)
        {
            e.Handled = true;
            OnSearch(sender, e);
        }
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text.Trim();
        if (query.Length < 2 || OpenMeteo.ParseCoordinates(query) is not null)
            return;

        _search?.Cancel();
        var search = _search = new CancellationTokenSource();
        Results.Items.Clear();
        StatusText.Text = "Searching…";
        SearchButton.IsEnabled = false;
        try
        {
            var places = await OpenMeteo.SearchAsync(query, search.Token);
            if (search.IsCancellationRequested)
                return;
            foreach (var place in places)
                Results.Items.Add(new ListBoxItem { Content = place.Description, Tag = place, ToolTip = Coordinates(place) });
            StatusText.Text = places.Count == 0 ? $"Nothing found for \"{query}\". Try a nearby town, or add the country." : "Pick your place.";
            if (places.Count > 0)
                Results.SelectedIndex = 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!search.IsCancellationRequested)
                StatusText.Text = "Couldn't search: check the internet connection, or type latitude, longitude instead.";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private static string Coordinates(WeatherPlace place) =>
        string.Create(CultureInfo.InvariantCulture, $"{place.Latitude:0.###}, {place.Longitude:0.###}");

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        OkButton.IsEnabled = Results.SelectedItem is not null || OpenMeteo.ParseCoordinates(QueryBox.Text) is not null;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Picked() is not null)
            DialogResult = true;
    }
}
