using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FH6ItalianRarities.Core;

namespace FH6ItalianRarities.Infrastructure;

internal sealed class AutomationStopHotKey : IDisposable
{
    private const int HotKeyId = 0x6F08;
    private readonly nint _window;
    private readonly HwndSource _source;
    private readonly Action _stop;
    private bool _disposed;

    public AutomationStopHotKey(Window window, Action stop)
    {
        _window = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_window)
            ?? throw new GameToolException("无法注册停止热键，自动购买未启动。");
        _stop = stop;
        if (!RegisterHotKey(_window, HotKeyId, 0x4000, 0x77))
        {
            throw new GameToolException("F8 停止热键已被其他程序占用，自动购买未启动。请释放 F8 后再试。");
        }
        _source.AddHook(OnWindowMessage);
    }

    private nint OnWindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == HotKeyId)
        {
            handled = true;
            _stop();
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHotKey(_window, HotKeyId);
        _source.RemoveHook(OnWindowMessage);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
