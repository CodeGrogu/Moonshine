using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moonshine.App;

namespace Moonshine;

public static class CliProgram
{
    public static async Task<int> RunCliAsync(string[] args)
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
}
