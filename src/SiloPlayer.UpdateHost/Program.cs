using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SiloPlayer.UpdateHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options=HostOptions.Parse(args);
            if(options.Worker)return UpdateWorker.Run(options);
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_=>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                new UpdateApplication(options);
            });
            return Environment.ExitCode;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
