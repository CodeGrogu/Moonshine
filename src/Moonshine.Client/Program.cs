using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Moonshine.UI;

namespace Moonshine;

public static partial class Program
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [STAThread]
    public static int Main(string[] args)
    {
        if (AttachConsole(AttachParentProcess))
        {
            var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdOut);
            var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(stdErr);
        }
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        try
        {
            File.AppendAllText(Path.Combine(baseDir, "moonshine_startup.log"), $"[INFO] Program.Main started with {args.Length} args at {DateTime.UtcNow:O}...\n");
        }
        // ALLOWED_EXCEPTION: Ignore startup log file write failures.
        catch (Exception)
        {
        }

        // Check if user requested CLI subcommands (host, stream, probe, acceptance, loopback, benchmark, help)
        if (args.Length > 0 && !args[0].StartsWith("-RegisterForSystem", StringComparison.OrdinalIgnoreCase))
        {
            return CliProgram.RunCliAsync(args).GetAwaiter().GetResult();
        }

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[CRASH CurrentDomain.UnhandledException]: {e.ExceptionObject}\n");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            File.AppendAllText(Path.Combine(baseDir, "moonshine_app.log"), $"[CRASH TaskScheduler.UnobservedTaskException]: {e.Exception}\n");
        };

        // WinUI 3 Desktop App Initialization
        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            Moonshine.UI.App.CurrentApp = new Moonshine.UI.App();
        });

        return 0;
    }
}
