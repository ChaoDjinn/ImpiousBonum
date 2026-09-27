using System.ServiceProcess;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Sensors;

internal sealed class SensorService : ServiceBase
{
    private SensorHost? _host;

    public SensorService()
    {
        ServiceName = SensorProtocol.ServiceName;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        Log.Info("Service starting.");
        _host = new SensorHost();
        _host.Start();
    }

    protected override void OnStop()
    {
        Log.Info("Service stopping.");
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _host = null;
    }

    protected override void OnShutdown() => OnStop();
}
