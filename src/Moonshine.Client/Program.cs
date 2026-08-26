using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Moonshine.App;
using Moonshine.UI;

namespace Moonshine;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // If explicit CLI arguments or subcommands are provided (and not --ui), execute headless CLI runner
        bool forceUi = args.Any(a => a.Equals("--ui", StringComparison.OrdinalIgnoreCase) || a.Equals("-ui", StringComparison.OrdinalIgnoreCase));
        bool hasCliCommand = args.Length > 0 && !forceUi && !args[0].StartsWith('-');

        if (hasCliCommand || (args.Length > 0 && !forceUi && (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))))
        {
            return RunCliAsync(args).GetAwaiter().GetResult();
        }

        // Otherwise launch WinUI 3 graphical desktop experience
        return RunWinUi();
    }

    private static async Task<int> RunCliAsync(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var options = CliOptions.Parse(args);
        using var application = new MoonshineApplication();

        try
        {
            return await application.RunAsync(options, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        // ALLOWED_EXCEPTION: Report top-level CLI uncaught exception to stderr before process termination.
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[-] Fatal error: {ex.Message}");
            return 1;
        }
    }

    private static int RunWinUi()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            var context = new DispatcherQueueSynchronizationContext(dispatcherQueue);
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new Moonshine.UI.App();
        });
        return 0;
    }
}
