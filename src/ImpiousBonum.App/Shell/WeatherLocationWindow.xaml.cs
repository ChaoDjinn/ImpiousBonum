using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ImpiousBonum.App.Settings;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Finds a place by name (or takes coordinates) for the weather. Searching is the only time a name is sent anywhere: to
/// Open-Meteo, and to OpenStreetMap only when Open-Meteo doesn't find it (see <see cref="PlaceSearch"/>).
/// </summary>
public partial class WeatherLocationWindow : Window
{
    private readonly Func<string, CancellationToken, Task<PlaceSearchResults>> _find;
    private CancellationTokenSource? _search;

    /// <summary>What the places listed were found for, so Enter after changing it searches again.</summary>
    private string? _searched;

    /// <param name="find">Searches for places; <see cref="PlaceSearch"/> unless a test swaps it.</param>
    internal WeatherLocationWindow(WeatherLocation? current, Func<string, CancellationToken, Task<PlaceSearchResults>>? find = null)
    {
        InitializeComponent();
        _find = find ?? PlaceSearch.SearchAsync;
        var credit = new Hyperlink(new Run(OpenStreetMap.Attribution)) { NavigateUri = new Uri(OpenStreetMap.CopyrightPage) };
        credit.Click += (_, _) => Ui.Open(OpenStreetMap.CopyrightPage);
        Attribution.Inlines.Add(credit);
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

    internal WeatherLocation? Picked()
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
            Attribution.Visibility = Visibility.Collapsed;
            StatusText.Text = "These coordinates will be used as they are.";
        }
        OkButton.IsEnabled = coordinates || Results.SelectedItem is not null;
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && EnterSearches)
        {
            e.Handled = true;
            OnSearch(sender, e);
        }
    }

    /// <summary>
    /// Enter in the box searches, unless the places listed are for what's typed: then it uses the one picked. So adding a
    /// town after a comma and pressing Enter searches again rather than taking the first place from before.
    /// </summary>
    internal bool EnterSearches =>
        OpenMeteo.ParseCoordinates(QueryBox.Text) is null && (Results.SelectedItem is null || QueryBox.Text.Trim() != _searched);

    private async void OnSearch(object sender, RoutedEventArgs e) => await SearchAsync();

    /// <summary>Searches for what's typed. Only Enter and Search do, never typing: OpenStreetMap asks for that.</summary>
    internal async Task SearchAsync()
    {
        var query = QueryBox.Text.Trim();
        if (query.Length < 2 || OpenMeteo.ParseCoordinates(query) is not null)
            return;

        _search?.Cancel();
        var search = _search = new CancellationTokenSource();
        _searched = query;
        Results.Items.Clear();
        Attribution.Visibility = Visibility.Collapsed;
        StatusText.Text = "Searching…";
        SearchButton.IsEnabled = false;
        try
        {
            var found = await _find(query, search.Token);
            if (search.IsCancellationRequested)
                return;
            foreach (var place in found.Places)
                Results.Items.Add(new ListBoxItem { Content = place.Description, Tag = place, ToolTip = Coordinates(place) });
            if (found.Places.Count == 0)
            {
                StatusText.Text = $"Nothing found for \"{query}\". Check the spelling, or try a nearby town.";
                return;
            }
            Attribution.Visibility = found.FromOpenStreetMap ? Visibility.Visible : Visibility.Collapsed;
            // Open-Meteo knows fewer suburbs and villages: with the town after a comma, OpenStreetMap is asked when it has none there.
            StatusText.Text = found.FromOpenStreetMap || query.Contains(',') ? "Pick your place." : "Pick your place. Not there? Add its town or county after a comma.";
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
