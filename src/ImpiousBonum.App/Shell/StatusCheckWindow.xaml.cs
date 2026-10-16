using System.Globalization;
using System.Windows;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.StatusChecks;

namespace ImpiousBonum.App.Shell;

/// <summary>Sets up one status check: its name, the address to check, a word the page must say and how often, with a Check now button.</summary>
public partial class StatusCheckWindow : Window
{
    private readonly IReadOnlyCollection<string> _otherNames;
    private CancellationTokenSource? _test;

    /// <param name="otherNames">The other checks' names, which this one's can't share (nor their ids).</param>
    internal StatusCheckWindow(StatusCheck? check, IReadOnlyCollection<string> otherNames)
    {
        InitializeComponent();
        _otherNames = otherNames;
        if (check is not null)
        {
            NameBox.Text = check.Name;
            AddressBox.Text = check.Address;
            ExpectBox.Text = check.Expect ?? string.Empty;
            AnyCertificateCheck.IsChecked = check.AnyCertificate;
            EveryBox.Text = check.Every.ToString(CultureInfo.CurrentCulture);
        }
        Closed += (_, _) => _test?.Cancel();
        Update();
    }

    /// <returns>The check as saved, or null if cancelled.</returns>
    public static StatusCheck? Ask(Window? owner, StatusCheck? check, IReadOnlyCollection<string> otherNames)
    {
        var window = new StatusCheckWindow(check, otherNames) { Owner = owner };
        if (owner is null)
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true ? window.Build(out _) : null;
    }

    /// <summary>The check as filled in, or null with <paramref name="problem"/> saying what's wrong.</summary>
    internal StatusCheck? Build(out string? problem)
    {
        var address = AddressBox.Text.Trim();
        var web = StatusCheck.IsWebAddress(address);
        // Not a number is too few seconds, which the check says after anything wrong with the name or address.
        if (!int.TryParse(EveryBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var every))
            every = 0;

        var check = new StatusCheck(
            NameBox.Text.Trim(),
            address,
            web && ExpectBox.Text.Trim() is { Length: > 0 } expect ? expect : null,
            every,
            IsHttps(address) && AnyCertificateCheck.IsChecked == true);
        problem = check.Problem();
        if (problem is null && _otherNames.Any(other => MetricIdPart.From(other).Equals(check.Slug, StringComparison.OrdinalIgnoreCase)))
        {
            problem = _otherNames.Contains(check.Name, StringComparer.OrdinalIgnoreCase)
                ? $"There's already a check called \"{check.Name}\"."
                : $"Another check's name gives the same readings, {check.IdFor(StatusCheck.Up)}: call this one something else.";
        }
        return problem is null ? check : null;
    }

    private static bool IsHttps(string address) => address.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private void Update()
    {
        // TextChanged fires while the window is still being built, before every control exists; the constructor calls
        // this once it's ready.
        if (!IsInitialized)
            return;
        var address = AddressBox.Text.Trim();
        ExpectBox.IsEnabled = StatusCheck.IsWebAddress(address);
        AnyCertificateCheck.IsEnabled = IsHttps(address);
        Build(out var problem);
        ProblemText.Text = problem ?? string.Empty;
        OkButton.IsEnabled = TestButton.IsEnabled = problem is null;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Update();

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        if (Build(out _) is not { } check)
            return;
        _test?.Cancel();
        var test = _test = new CancellationTokenSource();
        TestButton.IsEnabled = false;
        TestStatus.Text = check.IsWeb ? "Asking for the page…" : "Pinging…";
        try
        {
            var result = await StatusProvider.TestAsync(check, test.Token);
            if (test.IsCancellationRequested)
                return;
            TestStatus.Text = Describe(result);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    /// <summary>"Up: it answered in 4 ms", or "Down: No answer".</summary>
    internal static string Describe(CheckResult result) =>
        result.Up ? $"Up: it answered in {MetricFormatter.Format(result.Milliseconds, MetricUnit.Milliseconds)}" : $"Down: {result.Detail}";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Build(out _) is not null)
            DialogResult = true;
    }
}
