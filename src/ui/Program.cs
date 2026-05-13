using Avalonia;
using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace CSVoom;

internal abstract class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        #if DEBUG
        Trace.Listeners.Add(new ConsoleTraceListener());

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine("Unhandled exception:");
            Console.Error.WriteLine(e.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine("Unobserved task exception:");
            Console.Error.WriteLine(e.Exception);
        };
        #endif
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        Console.Error.WriteLine("Exception thrown:");
        Console.Error.WriteLine(e.Exception);
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


