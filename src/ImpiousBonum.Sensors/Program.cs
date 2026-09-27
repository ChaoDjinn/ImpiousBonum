using System.ServiceProcess;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Sensors;

// Started by the Service Control Manager.
if (!Environment.UserInteractive)
{
    Log.UseFile();
    ServiceBase.Run(new SensorService());
    return 0;
}

switch (args.FirstOrDefault()?.ToLowerInvariant())
{
    case "install":
        return ServiceInstaller.Install();

    case "uninstall":
        return ServiceInstaller.Uninstall();

    case "list":
        return ListSensors();

    case null or "run":
        return await RunInConsoleAsync();

    default:
        Console.WriteLine("""
            Impious Bonum sensor host

              run         Serve sensors to the dashboard from this console (Ctrl+C to stop). Default.
              list        Print every sensor and the aliases it resolves, then exit.
              install     Install and start the Windows service (admin).
              uninstall   Stop and remove the Windows service (admin).

            Without admin rights, CPU and motherboard sensors are unavailable; GPU sensors usually still work.
            """);
        return 1;
}

static async Task<int> RunInConsoleAsync()
{
    using var stopped = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stopped.Cancel();
    };

    await using var host = new SensorHost();
    host.Start();
    Log.Info("Serving sensors. Press Ctrl+C to stop.");
    try
    {
        await Task.Delay(Timeout.Infinite, stopped.Token);
    }
    catch (OperationCanceledException)
    {
    }
    return 0;
}

static int ListSensors()
{
    using var collector = new SensorCollector();
    collector.Open();
    collector.Update();
    // Load and rate sensors need two readings.
    Thread.Sleep(1000);
    collector.Update();

    var values = collector.ReadValues();
    Console.WriteLine(collector.Status);
    foreach (var group in collector.Catalog.GroupBy(s => s.Category))
    {
        Console.WriteLine();
        Console.WriteLine(group.Key);
        foreach (var sensor in group)
        {
            var sample = new MetricSample(sensor.ToDefinition(), values.GetValueOrDefault(sensor.Id), null);
            Console.WriteLine($"  {MetricFormatter.Format(sample),14}  {sensor.Id,-40} {sensor.Name}");
        }
    }
    return 0;
}
