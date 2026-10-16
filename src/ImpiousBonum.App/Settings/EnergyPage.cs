using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ImpiousBonum.App.Controls;
using ImpiousBonum.Core.Energy;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.App.Settings;

/// <summary>
/// Energy: the PC's power now and the energy it's used, the price and currency for what that costs, and the allowance
/// for the parts nothing measures; and Great Britain's grid carbon intensity, if wanted, for the whole of it or a region.
/// </summary>
internal sealed class EnergyPage(ISettingsHost host) : CardPage("Energy",
    "What your PC is using and what it costs, from the power the sensor service reads. Turn on the grid carbon intensity to see how clean Great Britain's electricity is, and when it'll be cleanest.")
{
    /// <summary>The choices for the parts nothing measures.</summary>
    internal static readonly int[] OtherWatts = [0, 10, 25, 50, 75, 100, 150];

    internal const string PriceNote = "What a kWh costs you, such as 0.25, or 24.5p in pence or cents. Leave it empty to leave the costs out.";

    internal const string AreaNote =
        "The first half of your postcode, such as LS1, for your region's figures rather than all of Great Britain's. Only that half is kept, and sent with each request.";

    private SettingsCard? _power;
    private SettingsCard? _used;
    private SettingsCard? _price;
    private SettingsCard? _grid;
    private SettingsCard? _area;
    private TextBox? _priceBox;
    private TextBox? _currencyBox;
    private TextBox? _areaBox;

    protected override void Build()
    {
        var settings = host.Settings;
        _power = Add(Ui.Card("", "Power now", PowerStatus(host.Store, host.SensorStatus, settings.EnergySettings())));
        _used = Add(Ui.Card("", "Used so far", UsedStatus(host.Store)));

        _priceBox = Box(settings.EnergyPrice?.ToString(CultureInfo.CurrentCulture), "e.g. 0.25", "Price per kWh", CommitPrice);
        _price = Add(Ui.Card("", "Price per kWh", PriceNote, _priceBox));
        _currencyBox = Box(settings.EnergyCurrency, EnergyCost.DefaultSymbol, "Currency symbol", CommitCurrency, width: 80);
        _currencyBox.MaxLength = 5;
        Add(Ui.Card("", "Currency symbol", $"Shown with the costs. Leave it empty for your region's ({EnergyCost.DefaultSymbol}).", _currencyBox));
        int[] watts = OtherWatts.Contains(settings.EnergyOtherWatts) ? OtherWatts : [.. OtherWatts.Append(settings.EnergyOtherWatts).Order()];
        Add(Ui.Card("", "Everything else",
            "Added for the parts the sensor service can't measure: the motherboard, memory, drives and fans. About 50 W suits most desktops. Monitors and speakers aren't counted.",
            Ui.Choice(watts.Select(w => (w, $"{w} W")), settings.EnergyOtherWatts, "Everything else", host.SetEnergyOtherWatts, width: 120)));
        Add(Ui.Note("Energy is only counted while Impious Bonum is running: time it isn't, or the PC is asleep, isn't counted. "
            + "Today's total starts again at midnight, and the month's on the 1st. Standing charges aren't included."));

        Add(Ui.Heading("Grid carbon intensity"));
        var on = settings.GridCarbon;
        Add(Ui.Card("", "Grid carbon intensity",
            "How much CO₂ Great Britain's electricity is giving off now, and when it'll be lowest in the next 24 hours. Only for Great Britain. "
            + "The figures come from carbonintensity.org.uk every 30 minutes, and nothing about you is sent unless you give your area.",
            Ui.Switch(on, "Grid carbon intensity", turnedOn =>
            {
                host.SetGridCarbon(turnedOn);
                Load();
            })));
        (_grid, _area, _areaBox) = (null, null, null);
        if (!on)
            return;
        _grid = Add(Ui.Card("", "Now", GridStatus(host.Store)));
        _areaBox = Box(settings.GridCarbonArea, "All of Great Britain", "Your area", CommitArea, width: 180);
        _area = Add(Ui.Card("", "Your area", AreaNote, _areaBox));
        // NESO asks to be credited where its figures are used, so only while they are.
        Add(Ui.Note(CarbonIntensity.Attribution + "."));
    }

    /// <summary>A box whose text is taken when Enter is pressed, when it loses focus, or when the page is left.</summary>
    private static TextBox Box(string? text, string placeholder, string name, Action commit, double width = 120)
    {
        var box = new TextBox { Text = text ?? "", Width = width };
        Placeholder.SetText(box, placeholder);
        AutomationProperties.SetName(box, name);
        box.LostFocus += (_, _) => commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                commit();
        };
        return box;
    }

    /// <summary>Takes the price typed in: empty is no price, and something that isn't one says what's wanted instead.</summary>
    internal void CommitPrice()
    {
        if (_priceBox is null || _price is null)
            return;
        var text = _priceBox.Text.Trim();
        var price = EnergyCost.ParsePrice(text, host.Settings.EnergySettings().Currency);
        if (text.Length > 0 && price is null)
        {
            _price.Description = "That isn't a price. Type what a kWh costs, such as 0.25, or 24.5p in pence.";
            return;
        }
        _price.Description = PriceNote;
        // As it's kept: 24.5p shows as 0.245.
        _priceBox.Text = price?.ToString(CultureInfo.CurrentCulture) ?? "";
        if (price != host.Settings.EnergyPrice)
            host.SetEnergyPrice(price);
    }

    internal void CommitCurrency()
    {
        if (_currencyBox is null)
            return;
        var symbol = _currencyBox.Text.Trim();
        var currency = symbol.Length == 0 ? null : symbol;
        if (currency != host.Settings.EnergyCurrency)
            host.SetEnergyCurrency(currency);
    }

    /// <summary>Takes the area typed in, keeping only a postcode's first half: empty is all of Great Britain.</summary>
    internal void CommitArea()
    {
        if (_areaBox is null || _area is null)
            return;
        var text = _areaBox.Text.Trim();
        var area = CarbonIntensity.Area(text);
        if (text.Length > 0 && area is null)
        {
            _area.Description = "That isn't a postcode. Type its first half, such as LS1, or leave it empty for all of Great Britain.";
            return;
        }
        if (area is not null && CarbonIntensity.InNorthernIreland(area))
        {
            _area.Description = "Northern Ireland's grid isn't part of Great Britain's, so there are no figures for it. Leave it empty for Great Britain's.";
            return;
        }
        _area.Description = AreaNote;
        _areaBox.Text = area ?? "";
        if (area != host.Settings.GridCarbonArea)
            host.SetGridCarbonArea(area);
    }

    /// <summary>
    /// "433 W: CPU 96 W, graphics card 287 W and 50 W for everything else", or why there's no reading: the sensor service
    /// isn't running, or it isn't reading either part's power.
    /// </summary>
    internal static string PowerStatus(MetricStore? store, string sensorStatus, EnergySettings settings)
    {
        var cpu = Reading(store, SensorAliases.CpuPower);
        var gpu = Reading(store, SensorAliases.GpuPower);
        if (EnergyProvider.TotalPower(cpu, gpu, settings.OtherWatts) is not { } total)
        {
            return sensorStatus == SensorHostProvider.NotRunning
                ? "No reading: the CPU's and graphics card's power come from the sensor service, which isn't running. Install it on the Sensors page."
                : "No reading: the sensor service isn't reading the CPU's or the graphics card's power.";
        }
        var parts = string.Join(", ",
            cpu is { } c ? $"CPU {Watts(c)}" : "CPU not read (it needs the PawnIO driver, as CPU temperatures do)",
            gpu is { } g ? $"graphics card {Watts(g)}" : "graphics card not read");
        return $"{Watts(total)}: {parts} and {Watts(settings.OtherWatts)} for everything else";
    }

    /// <summary>"1.92 kWh today (£0.48) · 38.6 kWh this month (£9.65)", without the costs when there's no price.</summary>
    internal static string UsedStatus(MetricStore? store)
    {
        if (store is null || !store.TryGet(EnergyProvider.Today, out var today) || today.Value is null || !store.TryGet(EnergyProvider.Month, out var month))
            return "Nothing counted yet";
        string Used(MetricSample energy, string when, string cost) =>
            store.TryGet(cost, out var money) && money.Value is not null
                ? $"{MetricFormatter.Format(energy)} {when} ({MetricFormatter.Format(money)})"
                : $"{MetricFormatter.Format(energy)} {when}";
        return $"{Used(today, "today", EnergyProvider.CostToday)} · {Used(month, "this month", EnergyProvider.CostMonth)}";
    }

    /// <summary>"Great Britain: 142 gCO₂/kWh (Moderate) · Lowest around 03:30", or that it's waiting for them.</summary>
    internal static string GridStatus(MetricStore? store)
    {
        if (store is null || !store.TryGet(GridCarbonProvider.Intensity, out var intensity) || intensity.Value is null)
            return "Waiting for the figures";
        string? Text(string id) => store.TryGet(id, out var sample) ? sample.Text : null;
        var status = $"{Text(GridCarbonProvider.Region) ?? CarbonForecast.GreatBritain}: {MetricFormatter.Format(intensity)}";
        if (Text(GridCarbonProvider.Index) is { } level)
            status += $" ({level})";
        if (Text(GridCarbonProvider.Best) is { } best)
            status += $" · {best}";
        return status;
    }

    private static double? Reading(MetricStore? store, string id) =>
        store is not null && store.TryGet(id, out var sample) && sample.Value is double value && double.IsFinite(value) ? value : null;

    private static string Watts(double watts) => string.Create(CultureInfo.CurrentCulture, $"{watts:0} W");

    public override void Refresh()
    {
        if (_power is not null)
            _power.Description = PowerStatus(host.Store, host.SensorStatus, host.Settings.EnergySettings());
        if (_used is not null)
            _used.Description = UsedStatus(host.Store);
        if (_grid is not null)
            _grid.Description = GridStatus(host.Store);
    }

    /// <summary>Whatever's been typed and not yet taken is taken now.</summary>
    public override void Left()
    {
        CommitPrice();
        CommitCurrency();
        CommitArea();
    }
}
