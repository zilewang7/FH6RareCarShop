using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FH6ItalianRarities.Core;
using FH6ItalianRarities.Infrastructure;

namespace FH6ItalianRarities;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLogger.Initialize();
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        try
        {
            var selfTest = OfflineSelfTest.Run();
            if (e.Args.Any(argument =>
                    string.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                AppLogger.Info($"Self-test passed: {selfTest}.");
                Shutdown(0);
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            AppLogger.Error("Startup validation failed", exception);
            if (!e.Args.Any(argument =>
                    string.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    "启动自检失败。\n\n" + exception.Message,
                    "FH6 意大利奇珍",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            Shutdown(1);
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            "程序遇到未处理错误，已写入诊断日志。\n\n" + e.Exception.Message,
            "FH6 意大利奇珍",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
