using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FH6ItalianRarities.Core;

internal static class NativeMethods
{
    public const uint ProcessVmOperation = 0x0008;
    public const uint ProcessCreateThread = 0x0002;
    public const uint ProcessVmRead = 0x0010;
    public const uint ProcessVmWrite = 0x0020;
    public const uint ProcessQueryInformation = 0x0400;
    public const uint ProcessQueryLimitedInformation = 0x1000;

    public const uint MemCommit = 0x1000;
    public const uint MemReserve = 0x2000;
    public const uint MemRelease = 0x8000;
    public const uint MemPrivate = 0x20000;

    public const uint PageNoAccess = 0x01;
    public const uint PageReadOnly = 0x02;
    public const uint PageReadWrite = 0x04;
    public const uint PageWriteCopy = 0x08;
    public const uint PageExecute = 0x10;
    public const uint PageExecuteRead = 0x20;
    public const uint PageExecuteReadWrite = 0x40;
    public const uint PageExecuteWriteCopy = 0x80;
    public const uint PageGuard = 0x100;

    public const uint WaitObject0 = 0;
    public const uint WaitTimeout = 258;

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryBasicInformation64
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public uint AllocationProtect;
        public uint Alignment1;
        public ulong RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint Alignment2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        byte[] buffer,
        nuint size,
        out nuint bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint VirtualAllocEx(
        SafeProcessHandle process,
        nint address,
        nuint size,
        uint allocationType,
        uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualFreeEx(
        SafeProcessHandle process,
        nint address,
        nuint size,
        uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeWaitHandle CreateRemoteThread(
        SafeProcessHandle process,
        nint threadAttributes,
        nuint stackSize,
        nint startAddress,
        nint parameter,
        uint creationFlags,
        out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeThread(SafeWaitHandle thread, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FlushInstructionCache(
        SafeProcessHandle process,
        nint baseAddress,
        nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nuint VirtualQueryEx(
        SafeProcessHandle process,
        nint address,
        out MemoryBasicInformation64 buffer,
        nuint length);
}
