using System.Runtime.ExceptionServices;

namespace ImpiousBonum.App.Tests;

/// <summary>WPF elements must be created on a single-threaded apartment thread; xUnit's aren't.</summary>
internal static class Sta
{
    /// <summary>
    /// Room in the thread pool for the work tests start in the background, such as a photo frame decoding a photo: a
    /// test here holds a thread while it waits for its own, and once the pool's first threads are taken it adds more only
    /// slowly, about one a second, so on a CI machine with a couple of cores a decode once waited past 10 seconds.
    /// </summary>
    static Sta()
    {
        ThreadPool.GetMinThreads(out var workers, out var completions);
        ThreadPool.SetMinThreads(Math.Max(workers, 32), Math.Max(completions, 32));
    }

    public static void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
