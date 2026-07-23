using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FH6ItalianRarities.Core;

internal sealed record RefreshLocation(ulong Address, string DiscoveryMethod);

internal static class RefreshLocator
{
    private const int BufferSize = 4 * 1024 * 1024;

    public static RefreshLocation Find(
        SafeProcessHandle process,
        ulong moduleBase,
        int moduleSize,
        CancellationToken cancellationToken)
    {
        var knownAddress = moduleBase + GameLayout.KnownRefreshRva;
        if (MatchesAt(process, knownAddress))
        {
            return new RefreshLocation(knownAddress, "已验证 RVA");
        }

        var matches = ScanExecutableModule(process, moduleBase, moduleSize, cancellationToken);
        if (matches.Count != 1)
        {
            throw new GameToolException(
                "当前游戏版本的交互刷新函数无法安全定位，重购功能已停用。普通车辆切换仍可单独检查。",
                $"Refresh signature matches: {matches.Count}. " +
                string.Join(", ", matches.Select(address => $"0x{address:X16}")));
        }

        return new RefreshLocation(matches[0], "代码特征扫描");
    }

    public static bool MatchesAt(SafeProcessHandle process, ulong address)
    {
        try
        {
            var bytes = MemoryAccess.ReadBytes(process, address, GameLayout.RefreshSignature.Length);
            return Matches(bytes, 0);
        }
        catch
        {
            return false;
        }
    }

    private static List<ulong> ScanExecutableModule(
        SafeProcessHandle process,
        ulong moduleBase,
        int moduleSize,
        CancellationToken cancellationToken)
    {
        var results = new HashSet<ulong>();
        var moduleEnd = moduleBase + checked((ulong)moduleSize);
        var buffer = new byte[BufferSize];
        var address = moduleBase;
        while (address < moduleEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NativeMethods.VirtualQueryEx(
                    process,
                    (nint)address,
                    out var region,
                    (nuint)Marshal.SizeOf<NativeMethods.MemoryBasicInformation64>()) == 0)
            {
                break;
            }

            var regionStart = Math.Max(address, region.BaseAddress);
            var rawRegionEnd = region.BaseAddress > ulong.MaxValue - region.RegionSize
                ? ulong.MaxValue
                : region.BaseAddress + region.RegionSize;
            var regionEnd = Math.Min(moduleEnd, rawRegionEnd);
            if (IsExecutable(region))
            {
                ScanRegion(process, regionStart, regionEnd, buffer, results, cancellationToken);
            }

            if (regionEnd <= address)
            {
                break;
            }
            address = regionEnd;
        }

        return results.Order().ToList();
    }

    private static void ScanRegion(
        SafeProcessHandle process,
        ulong start,
        ulong end,
        byte[] buffer,
        ISet<ulong> results,
        CancellationToken cancellationToken)
    {
        for (var current = start; current < end;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = end - current;
            var requested = checked((int)Math.Min((ulong)buffer.Length, remaining));
            if (NativeMethods.ReadProcessMemory(
                    process,
                    (nint)current,
                    buffer,
                    (nuint)requested,
                    out var bytesRead) && bytesRead >= (nuint)GameLayout.RefreshSignature.Length)
            {
                var count = checked((int)bytesRead);
                var search = 0;
                while (search <= count - GameLayout.RefreshSignature.Length)
                {
                    var relative = buffer.AsSpan(search, count - search).IndexOf((byte)0x48);
                    if (relative < 0)
                    {
                        break;
                    }
                    var index = search + relative;
                    if (Matches(buffer, index))
                    {
                        results.Add(current + checked((ulong)index));
                    }
                    search = index + 1;
                }
            }

            var overlap = GameLayout.RefreshSignature.Length - 1;
            var advance = remaining > (ulong)requested && requested > overlap
                ? requested - overlap
                : requested;
            current += checked((ulong)advance);
        }
    }

    private static bool Matches(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + GameLayout.RefreshSignature.Length > bytes.Length)
        {
            return false;
        }

        for (var index = 0; index < GameLayout.RefreshSignature.Length; index++)
        {
            var expected = GameLayout.RefreshSignature[index];
            if (expected.HasValue && bytes[offset + index] != expected.Value)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsExecutable(NativeMethods.MemoryBasicInformation64 region)
    {
        if (region.State != NativeMethods.MemCommit ||
            (region.Protect & (NativeMethods.PageGuard | NativeMethods.PageNoAccess)) != 0)
        {
            return false;
        }

        const uint executable =
            NativeMethods.PageExecute |
            NativeMethods.PageExecuteRead |
            NativeMethods.PageExecuteReadWrite |
            NativeMethods.PageExecuteWriteCopy;
        return (region.Protect & executable) != 0;
    }
}
