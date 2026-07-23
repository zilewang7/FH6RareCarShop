using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FH6ItalianRarities.Core;

internal static class MemoryAccess
{
    public static T Read<T>(SafeProcessHandle process, ulong address) where T : unmanaged
    {
        if (!TryRead(process, address, out T value))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"ReadProcessMemory failed at 0x{address:X16}");
        }
        return value;
    }

    public static bool TryRead<T>(SafeProcessHandle process, ulong address, out T value) where T : unmanaged
    {
        var buffer = new byte[Marshal.SizeOf<T>()];
        if (!NativeMethods.ReadProcessMemory(
                process,
                (nint)address,
                buffer,
                (nuint)buffer.Length,
                out var bytesRead) || bytesRead != (nuint)buffer.Length)
        {
            value = default;
            return false;
        }

        value = MemoryMarshal.Read<T>(buffer);
        return true;
    }

    public static byte[] ReadBytes(SafeProcessHandle process, ulong address, int count)
    {
        var buffer = new byte[count];
        if (!NativeMethods.ReadProcessMemory(
                process,
                (nint)address,
                buffer,
                (nuint)buffer.Length,
                out var bytesRead) || bytesRead != (nuint)buffer.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"ReadProcessMemory failed at 0x{address:X16}");
        }
        return buffer;
    }

    public static int[] ReadInt32Array(SafeProcessHandle process, ulong address, int count)
    {
        var buffer = ReadBytes(process, address, checked(count * sizeof(int)));
        var values = new int[count];
        Buffer.BlockCopy(buffer, 0, values, 0, buffer.Length);
        return values;
    }

    public static void WriteBytes(SafeProcessHandle process, ulong address, byte[] buffer)
    {
        if (!NativeMethods.WriteProcessMemory(
                process,
                (nint)address,
                buffer,
                (nuint)buffer.Length,
                out var bytesWritten) || bytesWritten != (nuint)buffer.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"WriteProcessMemory failed at 0x{address:X16}");
        }

        var verification = ReadBytes(process, address, buffer.Length);
        if (!buffer.AsSpan().SequenceEqual(verification))
        {
            throw new InvalidOperationException($"Write verification failed at 0x{address:X16}.");
        }
    }

    public static void Write<T>(SafeProcessHandle process, ulong address, T value) where T : unmanaged
    {
        var buffer = new byte[Marshal.SizeOf<T>()];
        MemoryMarshal.Write(buffer, in value);
        WriteBytes(process, address, buffer);
    }
}
