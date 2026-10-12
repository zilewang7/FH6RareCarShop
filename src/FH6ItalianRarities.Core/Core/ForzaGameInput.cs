using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FH6ItalianRarities.Core;

public sealed class ForzaGameInput : IDisposable
{
    private readonly Process _process;
    private bool _enterPressed;

    public ForzaGameInput(int processId)
    {
        _process = Process.GetProcessById(processId);
        try
        {
            if (!string.Equals(_process.ProcessName, "forzahorizon6", StringComparison.OrdinalIgnoreCase))
            {
                throw new GameToolException("自动购买只能向 Forza Horizon 6 发送按键。");
            }
            // Retain a handle to this exact process lifetime, even if its PID is reused.
            _ = _process.Handle;
        }
        catch
        {
            _process.Dispose();
            throw;
        }
    }

    public void EnsureGameForeground()
    {
        if (_process.HasExited) throw new GameToolException("游戏已退出，自动购买已停止。");
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var processId);
        if (foreground == 0 || processId != _process.Id)
        {
            throw new GameToolException("游戏已失去前台焦点，自动购买已停止。请回到购买位置后重新启动。");
        }
        // Avoid combining Enter with a user's Alt+Tab, Windows, or other shortcut.
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                throw new GameToolException("检测到快捷键操作，自动购买已停止。");
            }
        }
    }

    public void EnterDown()
    {
        EnsureGameForeground();
        SendEnter(true);
        _enterPressed = true;
    }

    public void EnterUp()
    {
        if (!_enterPressed) return;
        // Key-up must also happen after focus loss or cancellation.
        SendEnter(false);
        _enterPressed = false;
    }

    private static void SendEnter(bool pressed)
    {
        var inputs = new[] { CreateEnterInput(pressed) };
        if (SendInput(1, inputs, Marshal.SizeOf<Input>()) != 1)
        {
            throw new GameToolException(
                "无法发送 Enter 按键，自动购买已停止。请检查工具与游戏的运行权限。",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private static Input CreateEnterInput(bool pressed) => new()
    {
        Type = 1,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput { ScanCode = 0x1C, Flags = pressed ? 0x0008u : 0x000Au }
        }
    };

    public void Dispose()
    {
        try { EnterUp(); }
        finally { _process.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
