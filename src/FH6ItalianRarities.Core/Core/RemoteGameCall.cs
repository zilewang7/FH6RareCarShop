using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FH6ItalianRarities.Core;

internal sealed class RemoteCallStateUnknownException(string message, Exception? inner = null)
    : TimeoutException(message, inner);

internal static class RemoteGameCall
{
    internal sealed record StubSelfTestResult(
        int SetterBytes,
        int RefreshBytes,
        int PredicateBytes);

    public static void InvokeSetter(
        SafeProcessHandle process,
        ulong setterFunction,
        ulong managerAddress,
        int targetId,
        CancellationToken cancellationToken,
        bool notifyBeforeChange = true,
        bool rebuildDependentState = true)
    {
        var exitCode = Execute(
            process,
            BuildSetterStub(
                setterFunction,
                managerAddress,
                targetId,
                notifyBeforeChange,
                rebuildDependentState),
            cancellationToken,
            TimeSpan.FromSeconds(30));
        EnsureZeroExitCode(exitCode, "setter");
    }

    public static bool InvokeBool(
        SafeProcessHandle process,
        ulong function,
        ulong thisAddress,
        CancellationToken cancellationToken)
    {
        var exitCode = Execute(
            process,
            BuildBoolStub(function, thisAddress),
            cancellationToken,
            TimeSpan.FromSeconds(30));
        if (exitCode > 1)
        {
            throw new InvalidOperationException(
                $"Remote predicate returned unexpected value 0x{exitCode:X8}.");
        }
        return exitCode != 0;
    }

    public static Task<uint> InvokeRefreshAsync(
        SafeProcessHandle process,
        ulong refreshFunction,
        ulong managerAddress,
        IProgress<GameProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            return ExecuteRefresh(
                process,
                BuildRefreshStub(refreshFunction, managerAddress),
                progress,
                cancellationToken);
        }, CancellationToken.None);

    internal static StubSelfTestResult ValidateOffline()
    {
        const ulong function = 0x1122334455667788;
        const ulong manager = 0x7766554433221100;
        const int target = 0x12345678;

        var setter = BuildSetterStub(function, manager, target, true, true);
        RequireStub(setter.Length == 50, "setter length");
        RequireStub(BitConverter.ToUInt64(setter, 6) == manager, "setter manager");
        RequireStub(BitConverter.ToInt32(setter, 15) == target, "setter target");
        RequireStub(BitConverter.ToInt32(setter, 21) == 1, "setter notify flag");
        RequireStub(BitConverter.ToInt32(setter, 27) == 1, "setter rebuild flag");
        RequireStub(BitConverter.ToUInt64(setter, 33) == function, "setter function");
        RequireStub(setter.AsSpan(41).SequenceEqual(
            new byte[] { 0xFF, 0xD0, 0x48, 0x83, 0xC4, 0x28, 0x33, 0xC0, 0xC3 }),
            "setter epilogue");

        var refresh = BuildRefreshStub(function, manager);
        RequireStub(refresh.Length == 38, "refresh length");
        RequireStub(BitConverter.ToUInt64(refresh, 6) == manager, "refresh manager");
        RequireStub(BitConverter.ToInt32(refresh, 15) == 1, "refresh force flag");
        RequireStub(BitConverter.ToUInt64(refresh, 21) == function, "refresh function");

        var predicate = BuildBoolStub(function, manager);
        RequireStub(predicate.Length == 34, "predicate length");
        RequireStub(BitConverter.ToUInt64(predicate, 6) == manager, "predicate this pointer");
        RequireStub(BitConverter.ToUInt64(predicate, 16) == function, "predicate function");
        RequireStub(predicate.AsSpan(24).SequenceEqual(
            new byte[] { 0xFF, 0xD0, 0x0F, 0xB6, 0xC0, 0x48, 0x83, 0xC4, 0x28, 0xC3 }),
            "predicate epilogue");

        return new StubSelfTestResult(setter.Length, refresh.Length, predicate.Length);
    }

    private static uint Execute(
        SafeProcessHandle process,
        byte[] code,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allocation = NativeMethods.VirtualAllocEx(
            process,
            0,
            (nuint)code.Length,
            NativeMethods.MemCommit | NativeMethods.MemReserve,
            NativeMethods.PageExecuteReadWrite);
        if (allocation == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed.");
        }

        var canFree = true;
        try
        {
            MemoryAccess.WriteBytes(process, checked((ulong)allocation), code);
            if (!NativeMethods.FlushInstructionCache(process, allocation, (nuint)code.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "FlushInstructionCache failed.");
            }

            using var thread = NativeMethods.CreateRemoteThread(
                process,
                0,
                0,
                allocation,
                0,
                0,
                out var threadId);
            if (thread.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread failed.");
            }

            canFree = false;
            var deadline = DateTime.UtcNow.Add(timeout);
            var cancellationRequested = false;
            while (DateTime.UtcNow < deadline)
            {
                var waitResult = NativeMethods.WaitForSingleObject(thread, 100);
                if (waitResult == NativeMethods.WaitObject0)
                {
                    canFree = true;
                    break;
                }
                if (waitResult != NativeMethods.WaitTimeout)
                {
                    var error = new Win32Exception(Marshal.GetLastWin32Error(),
                        $"Waiting for remote thread {threadId} failed.");
                    throw new RemoteCallStateUnknownException(error.Message, error);
                }
                cancellationRequested |= cancellationToken.IsCancellationRequested;
            }

            if (!canFree)
            {
                throw new RemoteCallStateUnknownException(
                    $"Remote game-call thread {threadId} did not finish within {timeout.TotalSeconds:0} seconds.");
            }
            if (!NativeMethods.GetExitCodeThread(thread, out var exitCode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeThread failed.");
            }
            if (exitCode != 0)
            {
                return exitCode;
            }
            if (cancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            return exitCode;
        }
        finally
        {
            if (canFree && !NativeMethods.VirtualFreeEx(process, allocation, 0, NativeMethods.MemRelease))
            {
                Infrastructure.AppLogger.Warn(
                    $"VirtualFreeEx failed at 0x{(ulong)allocation:X16}: {Marshal.GetLastWin32Error()}.");
            }
        }
    }

    private static uint ExecuteRefresh(
        SafeProcessHandle process,
        byte[] code,
        IProgress<GameProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allocation = NativeMethods.VirtualAllocEx(
            process,
            0,
            (nuint)code.Length,
            NativeMethods.MemCommit | NativeMethods.MemReserve,
            NativeMethods.PageExecuteReadWrite);
        if (allocation == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed.");
        }

        var canFree = true;
        try
        {
            MemoryAccess.WriteBytes(process, checked((ulong)allocation), code);
            if (!NativeMethods.FlushInstructionCache(process, allocation, (nuint)code.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "FlushInstructionCache failed.");
            }

            using var thread = NativeMethods.CreateRemoteThread(
                process, 0, 0, allocation, 0, 0, out var threadId);
            if (thread.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread failed.");
            }

            canFree = false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            while (true)
            {
                var waitResult = NativeMethods.WaitForSingleObject(thread, 200);
                if (waitResult == NativeMethods.WaitObject0)
                {
                    canFree = true;
                    break;
                }
                if (waitResult != NativeMethods.WaitTimeout)
                {
                    var error = new Win32Exception(Marshal.GetLastWin32Error(),
                        $"Waiting for remote refresh thread {threadId} failed.");
                    throw new RemoteCallStateUnknownException(error.Message, error);
                }

                // Once RefreshSlot has started, its code and temporary pool must stay alive until
                // the game releases the call. Cancellation is therefore deferred to the next safe point.
                if (watch.Elapsed - lastReport >= TimeSpan.FromSeconds(1))
                {
                    lastReport = watch.Elapsed;
                    progress?.Report(new GameProgress(
                        $"正在等待游戏重建购买交互（{watch.Elapsed.TotalSeconds:0} 秒）"));
                }
            }

            if (!NativeMethods.GetExitCodeThread(thread, out var exitCode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeThread failed.");
            }
            return exitCode;
        }
        finally
        {
            if (canFree && !NativeMethods.VirtualFreeEx(process, allocation, 0, NativeMethods.MemRelease))
            {
                Infrastructure.AppLogger.Warn(
                    $"VirtualFreeEx failed at 0x{(ulong)allocation:X16}: {Marshal.GetLastWin32Error()}.");
            }
        }
    }

    private static byte[] BuildSetterStub(
        ulong setterFunction,
        ulong managerAddress,
        int targetId,
        bool notifyBeforeChange,
        bool rebuildDependentState)
    {
        var code = new List<byte>
        {
            0x48, 0x83, 0xEC, 0x28,
            0x48, 0xB9
        };
        code.AddRange(BitConverter.GetBytes(managerAddress));
        code.Add(0xBA);
        code.AddRange(BitConverter.GetBytes(targetId));
        code.AddRange([0x41, 0xB8]);
        code.AddRange(BitConverter.GetBytes(notifyBeforeChange ? 1 : 0));
        code.AddRange([0x41, 0xB9]);
        code.AddRange(BitConverter.GetBytes(rebuildDependentState ? 1 : 0));
        code.AddRange([0x48, 0xB8]);
        code.AddRange(BitConverter.GetBytes(setterFunction));
        code.AddRange([
            0xFF, 0xD0,
            0x48, 0x83, 0xC4, 0x28,
            0x33, 0xC0,
            0xC3
        ]);
        return code.ToArray();
    }

    private static byte[] BuildRefreshStub(ulong refreshFunction, ulong managerAddress)
    {
        var code = new List<byte>
        {
            0x48, 0x83, 0xEC, 0x28,
            0x48, 0xB9
        };
        code.AddRange(BitConverter.GetBytes(managerAddress));
        code.AddRange([0xBA, 0x01, 0x00, 0x00, 0x00]);
        code.AddRange([0x48, 0xB8]);
        code.AddRange(BitConverter.GetBytes(refreshFunction));
        code.AddRange([
            0xFF, 0xD0,
            0x48, 0x83, 0xC4, 0x28,
            0x33, 0xC0,
            0xC3
        ]);
        return code.ToArray();
    }

    private static byte[] BuildBoolStub(ulong function, ulong thisAddress)
    {
        var code = new List<byte>
        {
            0x48, 0x83, 0xEC, 0x28,
            0x48, 0xB9
        };
        code.AddRange(BitConverter.GetBytes(thisAddress));
        code.AddRange([0x48, 0xB8]);
        code.AddRange(BitConverter.GetBytes(function));
        code.AddRange([
            0xFF, 0xD0,
            0x0F, 0xB6, 0xC0,
            0x48, 0x83, 0xC4, 0x28,
            0xC3
        ]);
        return code.ToArray();
    }

    private static void EnsureZeroExitCode(uint exitCode, string operation)
    {
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Remote {operation} stub returned 0x{exitCode:X8}.");
        }
    }

    private static void RequireStub(bool condition, string part)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Remote call stub self-test failed: {part}.");
        }
    }
}
