using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Energy;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.App.Tests;

/// <summary>The Energy page and its settings: the price, currency and allowance, and the grid's carbon intensity.</summary>
public sealed class EnergyPageTests
{
    private static readonly DateTime Evening = new(2026, 10, 11, 21, 47, 0);

    [Fact]
    public void Energy_starts_with_no_price_fifty_watts_for_everything_else_and_the_grid_off()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}", JsonDefaults.Options)!;
        Assert.Null(settings.EnergyPrice);
        Assert.Null(settings.EnergyCurrency);
        Assert.Equal(50, settings.EnergyOtherWatts);
        Assert.False(settings.GridCarbon);
        Assert.Equal(new EnergySettings(50, null, EnergyCost.DefaultSymbol), settings.EnergySettings());
        Assert.Null(settings.GridCarbonRequest());
    }

    [Fact]
    public void Energy_settings_are_saved_and_hand_edits_that_make_no_sense_are_left_out()
    {
        var settings = new AppSettings { EnergyPrice = 0.245, EnergyCurrency = "£", EnergyOtherWatts = 75, GridCarbon = true, GridCarbonArea = "LS1" };
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)!;

        Assert.Contains("\"energyPrice\": 0.245", json);
        Assert.Contains("\"gridCarbonArea\": \"LS1\"", json);
        Assert.Equal(new EnergySettings(75, 0.245, "£"), restored.EnergySettings());
        Assert.Equal(new GridCarbonRequest("LS1"), restored.GridCarbonRequest());

        var edited = new AppSettings { EnergyPrice = -1, EnergyCurrency = "  ", EnergyOtherWatts = -5, GridCarbon = true, GridCarbonArea = "my house" };
        Assert.Equal(new EnergySettings(0, null, EnergyCost.DefaultSymbol), edited.EnergySettings());
        Assert.Equal(new GridCarbonRequest(null), edited.GridCarbonRequest());
    }

    [Fact]
    public void Power_now_says_what_its_made_of_or_why_there_isnt_a_reading()
    {
        WithCulture(() =>
        {
            var settings = new EnergySettings(50, null, "£");
            var store = new MetricStore();
            foreach (var alias in SensorAliases.All)
                store.Register(alias.ToDefinition());

            Assert.Contains("isn't running", EnergyPage.PowerStatus(store, SensorHostProvider.NotRunning, settings));
            Assert.Contains("Install it on the Sensors page", EnergyPage.PowerStatus(null, SensorHostProvider.NotRunning, settings));
            Assert.Equal("No reading: the sensor service isn't reading the CPU's or the graphics card's power.", EnergyPage.PowerStatus(store, "Sensors running", settings));

            // Without the PawnIO driver, only the graphics card's.
            store.Set(SensorAliases.GpuPower, 287);
            Assert.Equal("337 W: CPU not read (it needs the PawnIO driver, as CPU temperatures do), graphics card 287 W and 50 W for everything else",
                EnergyPage.PowerStatus(store, "Sensors running", settings));

            store.Set(SensorAliases.CpuPower, 96.4);
            Assert.Equal("433 W: CPU 96 W, graphics card 287 W and 50 W for everything else", EnergyPage.PowerStatus(store, "Sensors running", settings));
        });
    }

    [Fact]
    public void The_page_shows_whats_been_used_and_the_grid_now()
    {
        WithCulture(() =>
        {
            Assert.Equal("Nothing counted yet", EnergyPage.UsedStatus(null));
            Assert.Equal("Waiting for the figures", EnergyPage.GridStatus(new MetricStore()));

            var store = new MetricStore();
            DemoReadings.Fill(store, Evening);
            Assert.Equal("1.92 kWh today (£0.48) · 38.6 kWh this month (£9.65)", EnergyPage.UsedStatus(store));
            Assert.Equal("Great Britain: 142 gCO₂/kWh (Moderate) · Lowest around 03:30", EnergyPage.GridStatus(store));

            // Without a price, just the energy.
            store.Set(EnergyProvider.CostToday, null);
            store.Set(EnergyProvider.CostMonth, null);
            Assert.Equal("1.92 kWh today · 38.6 kWh this month", EnergyPage.UsedStatus(store));
        });
    }

    [Fact]
    public void A_price_is_taken_as_typed_and_one_that_isnt_a_price_says_so()
    {
        Sta.Run(() => WithCulture(() =>
        {
            var (page, settings) = Page(new AppSettings { EnergyCurrency = "£" });
            var box = Box(page, "Price per kWh");
            Assert.Equal("", box.Text);

            box.Text = "24.5p";
            page.CommitPrice();
            Assert.Equal(0.245, settings.EnergyPrice);
            Assert.Equal("0.245", box.Text);

            box.Text = "cheap";
            page.CommitPrice();
            Assert.Equal(0.245, settings.EnergyPrice);
            Assert.StartsWith("That isn't a price.", Card(page, "Price per kWh").Description);

            // Empty leaves the costs out.
            box.Text = "";
            page.CommitPrice();
            Assert.Null(settings.EnergyPrice);
            Assert.Equal(EnergyPage.PriceNote, Card(page, "Price per kWh").Description);

            var currency = Box(page, "Currency symbol");
            currency.Text = " € ";
            page.CommitCurrency();
            Assert.Equal("€", settings.EnergyCurrency);
            currency.Text = "";
            page.CommitCurrency();
            Assert.Null(settings.EnergyCurrency);
        }));
    }

    [Fact]
    public void Everything_else_offers_the_watts_including_a_hand_edited_number()
    {
        Sta.Run(() =>
        {
            var (page, settings) = Page(new AppSettings());
            var choice = Logical<ComboBox>(page).Single(c => AutomationProperties.GetName(c) == "Everything else");
            Assert.Equal("50 W", choice.SelectedItem);

            choice.SelectedItem = "100 W";
            Assert.Equal(100, settings.EnergyOtherWatts);

            settings.EnergyOtherWatts = 60;
            page.Load();
            choice = Logical<ComboBox>(page).Single(c => AutomationProperties.GetName(c) == "Everything else");
            Assert.Equal("60 W", choice.SelectedItem);
        });
    }

    [Fact]
    public void Grid_carbon_is_off_until_turned_on_and_credits_neso_only_then()
    {
        Sta.Run(() =>
        {
            var (page, settings) = Page(new AppSettings());
            Assert.Contains("Grid carbon intensity", Headers(page));
            Assert.DoesNotContain("Your area", Headers(page));
            Assert.DoesNotContain(LogicalTexts(page), text => text.Contains("NESO", StringComparison.Ordinal));
            Assert.Contains("only for Great Britain", Card(page, "Grid carbon intensity").Description, StringComparison.OrdinalIgnoreCase);

            settings.GridCarbon = true;
            page.Load();
            Assert.Contains("Now", Headers(page));
            Assert.Contains("Your area", Headers(page));
            Assert.Contains(CarbonIntensity.Attribution + ".", LogicalTexts(page));
        });
    }

    [Fact]
    public void Only_a_postcodes_first_half_is_kept_and_northern_ireland_is_turned_away()
    {
        Sta.Run(() =>
        {
            var (page, settings) = Page(new AppSettings { GridCarbon = true });
            var box = Box(page, "Your area");

            box.Text = "ls1 4ap";
            page.CommitArea();
            Assert.Equal("LS1", settings.GridCarbonArea);
            Assert.Equal("LS1", box.Text);

            box.Text = "BT1 1AA";
            page.CommitArea();
            Assert.Equal("LS1", settings.GridCarbonArea);
            Assert.Contains("Northern Ireland", Card(page, "Your area").Description);

            box.Text = "Leeds";
            page.CommitArea();
            Assert.Equal("LS1", settings.GridCarbonArea);
            Assert.StartsWith("That isn't a postcode.", Card(page, "Your area").Description);

            // Empty is all of Great Britain.
            box.Text = "";
            page.CommitArea();
            Assert.Null(settings.GridCarbonArea);
            Assert.Equal(EnergyPage.AreaNote, Card(page, "Your area").Description);
        });
    }

    private static (EnergyPage Page, AppSettings Settings) Page(AppSettings settings)
    {
        var host = DispatchProxy.Create<ISettingsHost, EnergyHost>();
        ((EnergyHost)(object)host).Settings = settings;
        var page = new EnergyPage(host);
        page.Load();
        return (page, settings);
    }

    /// <summary>Money and times as in the UK, whatever the PC running the tests is set to.</summary>
    private static void WithCulture(Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>A settings host that keeps the energy settings in its settings, as the app does.</summary>
    public class EnergyHost : DispatchProxy
    {
        public AppSettings Settings { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_Settings":
                    return Settings;
                case nameof(ISettingsHost.SetEnergyPrice):
                    Settings.EnergyPrice = (double?)args![0];
                    return null;
                case nameof(ISettingsHost.SetEnergyCurrency):
                    Settings.EnergyCurrency = (string?)args![0];
                    return null;
                case nameof(ISettingsHost.SetEnergyOtherWatts):
                    Settings.EnergyOtherWatts = (int)args![0]!;
                    return null;
                case nameof(ISettingsHost.SetGridCarbon):
                    Settings.GridCarbon = (bool)args![0]!;
                    return null;
                case nameof(ISettingsHost.SetGridCarbonArea):
                    Settings.GridCarbonArea = (string?)args![0];
                    return null;
                default:
                    return targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
            }
        }
    }

    private static TextBox Box(DependencyObject page, string name) => Logical<TextBox>(page).Single(b => AutomationProperties.GetName(b) == name);

    private static SettingsCard Card(DependencyObject page, string header) => Logical<SettingsCard>(page).Single(c => c.Header == header);

    private static List<string?> Headers(DependencyObject root) => [.. Logical<SettingsCard>(root).Select(c => c.Header)];

    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is T match ? [match, .. Logical<T>(child)] : Logical<T>(child));

    /// <summary>Text in a page that hasn't been shown, so has no visual tree yet.</summary>
    private static List<string> LogicalTexts(DependencyObject root) =>
        [.. LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is TextBlock text ? [text.Text] : LogicalTexts(child))];
}
