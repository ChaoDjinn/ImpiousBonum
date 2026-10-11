using System.Windows;
using ImpiousBonum.Core.CustomData;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Shell;

/// <summary>Sets up one custom data source: where to read from, how often, and which fields to pick out, with a Test button.</summary>
public partial class CustomSourceWindow : Window
{
    private readonly IReadOnlyCollection<string> _otherNames;
    private CancellationTokenSource? _test;

    internal CustomSourceWindow(CustomSource? source, IReadOnlyCollection<string> otherNames)
    {
        InitializeComponent();
        _otherNames = otherNames;
        if (source is not null)
        {
            NameBox.Text = source.Name;
            WebChoice.IsChecked = source.IsWeb;
            CommandChoice.IsChecked = !source.IsWeb;
            UrlBox.Text = source.Url ?? string.Empty;
            HeadersBox.Text = CustomSourceText.FormatHeaders(source.Headers);
            CommandBox.Text = source.Command ?? string.Empty;
            ArgumentsBox.Text = source.Arguments ?? string.Empty;
            EveryBox.Text = source.Every.ToString();
            FieldsBox.Text = CustomSourceText.FormatFields(source.Fields);
        }
        Closed += (_, _) => _test?.Cancel();
        Update();
    }

    /// <returns>The source as saved, or null if cancelled.</returns>
    public static CustomSource? Ask(Window? owner, CustomSource? source, IReadOnlyCollection<string> otherNames)
    {
        var window = new CustomSourceWindow(source, otherNames) { Owner = owner };
        if (owner is null)
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true ? window.Build(out _) : null;
    }

    /// <summary>The source as filled in, or null with <paramref name="problem"/> saying what's wrong.</summary>
    internal CustomSource? Build(out string? problem)
    {
        var fields = CustomSourceText.ParseFields(FieldsBox.Text, out problem);
        if (fields is null)
            return null;
        var web = WebChoice.IsChecked == true;
        var headers = web ? CustomSourceText.ParseHeaders(HeadersBox.Text, out problem) : null;
        if (problem is not null)
            return null;
        if (!int.TryParse(EveryBox.Text.Trim(), out var every) || every < CustomSource.MinimumEvery)
        {
            problem = $"Read it every {CustomSource.MinimumEvery} seconds or more.";
            return null;
        }

        var source = new CustomSource(
            NameBox.Text.Trim(),
            web ? UrlBox.Text.Trim() : null,
            web ? null : CommandBox.Text.Trim(),
            web || string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim(),
            headers is { Count: > 0 } ? headers : null,
            every,
            fields.Count > 0 ? fields : null);
        problem = source.Problem();
        if (problem is null && _otherNames.Contains(source.Name, StringComparer.OrdinalIgnoreCase))
            problem = $"There's already a source called \"{source.Name}\".";
        return problem is null ? source : null;
    }

    private void Update()
    {
        if (OkButton is null)
            return;
        var web = WebChoice.IsChecked == true;
        UrlBox.IsEnabled = HeadersBox.IsEnabled = web;
        CommandBox.IsEnabled = ArgumentsBox.IsEnabled = !web;
        Build(out var problem);
        ProblemText.Text = problem ?? string.Empty;
        OkButton.IsEnabled = TestButton.IsEnabled = problem is null;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Update();

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        if (Build(out _) is not { } source)
            return;
        _test?.Cancel();
        var test = _test = new CancellationTokenSource();
        TestButton.IsEnabled = false;
        TestStatus.Text = "Reading…";
        TestResults.Visibility = Visibility.Collapsed;
        try
        {
            var (readings, status) = await CustomDataProvider.TestAsync(source, test.Token);
            if (test.IsCancellationRequested)
                return;
            TestStatus.Text = status == "OK" ? "It worked. These are the metrics it gives:" : status;
            if (readings.Count > 0)
            {
                TestResults.Text = string.Join(Environment.NewLine, readings.Select(r => $"{source.MetricId(r.Field)} = {Show(r)}"));
                TestResults.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private static string Show(CustomReading reading) =>
        reading.Text ?? (reading.Value is double value
            ? MetricFormatter.Format(new MetricSample(new MetricDefinition("", "", "", CustomField.ParseUnit(reading.Field.Unit) ?? MetricUnit.None), value, null))
            : "(not found)");

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Build(out _) is not null)
            DialogResult = true;
    }
}
