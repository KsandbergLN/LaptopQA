using System.Windows;
using System.Windows.Threading;

namespace LaptopQA.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ErrorLog.Initialize();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        base.OnStartup(e);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.WriteException("Unhandled UI", "An unhandled UI error occurred.", e.Exception);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            ErrorLog.WriteException("Unhandled App", "An unhandled application error occurred.", ex);
        }
        else
        {
            ErrorLog.WriteActivity("Unhandled App", $"An unhandled application error occurred: {e.ExceptionObject}");
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ErrorLog.WriteException("Unhandled Task", "An unobserved background task error occurred.", e.Exception);
        e.SetObserved();
    }
}
