using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FH6ItalianRarities.Core;
using FH6ItalianRarities.Infrastructure;
using FH6ItalianRarities.Models;

namespace FH6ItalianRarities;

public partial class AutoPurchaseOverlayWindow : Window
{
    private readonly DispatcherTimer _trackingTimer;
    private readonly Stopwatch _elapsed = new();
    private HwndSource? _source;
    private nint _handle;
    private uint _gameProcessId;
    private bool _finished;
    private bool _closed;
    private long _dismissAt;

    public AutoPurchaseOverlayWindow(CarOption target, AutoPurchaseOptions options)
    {
        InitializeComponent();
        OverlayVehicleText.Text = $"{target.Year} {target.ChineseName}";
        OverlayOriginalNameText.Text = target.OriginalName;
        var limit = options.RepeatPurchase ? options.MaxRounds : 1;
        OverlayTargetText.Text = limit == 0 ? "无限" : $"{limit} 辆";
        OverlayTimingText.Text = $"Enter {options.EnterDuration.TotalSeconds:0.##} 秒 · " +
            $"间隔 {options.EnterInterval.TotalMilliseconds:0.##} ms · " +
            $"等待 {options.PurchaseDelay.TotalSeconds:0.##}/{options.RestoreDelay.TotalSeconds:0.##} 秒";
        _trackingTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _trackingTimer.Tick += OnTrackingTick;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) =>
        {
            _closed = true;
            _trackingTimer.Stop();
            _elapsed.Stop();
            if (_source is { IsDisposed: false }) _source.RemoveHook(OnWindowMessage);
        };
    }

    public void StartTracking(int processId)
    {
        if (_closed || _finished) return;
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        _gameProcessId = (uint)processId;
        // Create the HWND while hidden so the native click-through and no-activation
        // styles are installed before anything appears over the game.
        _handle = new WindowInteropHelper(this).EnsureHandle();
        _elapsed.Start();
        RefreshGameWindow();
        _trackingTimer.Start();
    }

    public void UpdateProgress(AutoPurchaseProgress progress)
    {
        if (_closed || _finished) return;
        UpdateCount(progress.CompletedRounds);
        UpdateStatus(progress.Message);
    }

    public void UpdateCount(int completed)
    {
        if (_closed || _finished) return;
        OverlayCompletedText.Text = $"{completed} 轮";
    }

    public void UpdateStatus(string message)
    {
        if (_closed || _finished) return;
        OverlayStatusText.Text = message;
    }

    public void Finish(string message, bool isError)
    {
        if (_closed || _finished) return;
        _finished = true;
        _elapsed.Stop();
        UpdateElapsed();
        OverlayStatusText.Text = message;
        OverlayStatusText.Foreground = new SolidColorBrush(isError
            ? Color.FromRgb(255, 192, 118)
            : Color.FromRgb(152, 225, 185));
        OverlayHotKeyText.Text = "已结束";
        OverlayFooterText.Text = "结果保留 8 秒";
        _dismissAt = Environment.TickCount64 + 8000;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        const int extendedStyle = -20;
        const long noActivate = 0x08000000;
        const long transparent = 0x00000020;
        const long toolWindow = 0x00000080;
        const long layered = 0x00080000;
        const long appWindow = 0x00040000;
        var styles = (GetWindowLongPtr(_handle, extendedStyle).ToInt64() |
            noActivate | transparent | toolWindow | layered) & ~appWindow;
        if (SetWindowLongPtr(_handle, extendedStyle, (nint)styles) == 0 && Marshal.GetLastWin32Error() != 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置悬浮窗的鼠标穿透属性。");
        }
        _source?.AddHook(OnWindowMessage);
    }

    private static nint OnWindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021) // WM_MOUSEACTIVATE
        {
            handled = true;
            return 3; // MA_NOACTIVATE
        }
        return 0;
    }

    private void OnTrackingTick(object? sender, EventArgs e)
    {
        if (_finished && Environment.TickCount64 >= _dismissAt)
        {
            Close();
            return;
        }
        try
        {
            UpdateElapsed();
            RefreshGameWindow();
        }
        catch (Exception exception)
        {
            // A display failure must not interrupt a purchase or its restore cleanup.
            AppLogger.Error("Automatic purchase overlay closed after a display failure", exception);
            Close();
        }
    }

    private void UpdateElapsed()
    {
        var elapsed = _elapsed.Elapsed;
        OverlayElapsedText.Text = string.Format(CultureInfo.InvariantCulture,
            "{0:00}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
    }

    private void RefreshGameWindow()
    {
        var gameWindow = GetForegroundWindow();
        GetWindowThreadProcessId(gameWindow, out var processId);
        if (processId != _gameProcessId || gameWindow == 0 || !IsWindowVisible(gameWindow) || IsIconic(gameWindow))
        {
            if (IsVisible) Hide();
            return;
        }
        if (!GetClientRect(gameWindow, out var client))
        {
            if (IsVisible) Hide();
            return;
        }
        var origin = new NativePoint { X = client.Left, Y = client.Top };
        if (!ClientToScreen(gameWindow, ref origin))
        {
            if (IsVisible) Hide();
            return;
        }
        var dpi = GetDpiForWindow(gameWindow);
        if (dpi == 0) dpi = 96;
        PositionOverGame(origin, client, dpi);
        if (!IsVisible)
        {
            Show();
            // WPF may resize for the destination monitor's DPI when shown.
            PositionOverGame(origin, client, dpi);
        }
    }

    private void PositionOverGame(NativePoint origin, NativeRect client, uint dpi)
    {
        var width = (int)Math.Ceiling(Width * dpi / 96d);
        var height = (int)Math.Ceiling(Height * dpi / 96d);
        if (GetWindowRect(_handle, out var bounds))
        {
            width = bounds.Right - bounds.Left;
            height = bounds.Bottom - bounds.Top;
        }
        var (x, y) = CalculatePosition(origin.X, origin.Y,
            client.Right - client.Left, client.Bottom - client.Top, width, height, dpi);
        const uint noSize = 0x0001;
        const uint noActivate = 0x0010;
        const uint noOwnerZOrder = 0x0200;
        if (!SetWindowPos(_handle, (nint)(-1), x, y, 0, 0, noSize | noActivate | noOwnerZOrder))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法跟随游戏窗口位置。");
        }
    }

    private static (int X, int Y) CalculatePosition(
        int left, int top, int gameWidth, int gameHeight, int overlayWidth, int overlayHeight, uint dpi)
    {
        var margin = (int)Math.Round(24 * dpi / 96d);
        return (left + Math.Min(margin, Math.Max(0, gameWidth - overlayWidth - margin)),
            top + Math.Min(margin, Math.Max(0, gameHeight - overlayHeight - margin)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
