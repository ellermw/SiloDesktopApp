using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SiloPlayer;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        StartupTiming.Start();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
