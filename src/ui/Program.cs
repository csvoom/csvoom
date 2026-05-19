using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Avalonia;

namespace CSVoom;

internal abstract class Program
{
    private static readonly object LogLock = new();

    private static readonly string ErrorLogPath = Path.Combine(
        AppContext.BaseDirectory,
        "errors.log");

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            WriteErrorLog("Unhandled exception", e.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, e) => { WriteErrorLog("Unobserved task exception", e.Exception); };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        WriteErrorLog("Exception thrown", e.Exception);
    }

    private static void WriteErrorLog(string title, object? error)
    {
        lock (LogLock)
        {
            File.AppendAllText(
                ErrorLogPath,
                $"""
                 [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {title}
                 {error}

                 ------------------------------------------------------------

                 """);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
#if DEBUG
        .WithDeveloperTools()
#endif
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}