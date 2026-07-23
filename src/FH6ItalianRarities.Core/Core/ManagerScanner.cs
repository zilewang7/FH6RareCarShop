using System.Diagnostics;
using System.Runtime.InteropServices;
using FH6ItalianRarities.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace FH6ItalianRarities.Core;

internal sealed record ManagerScanResult(IReadOnlyList<ManagerCandidate> Candidates, string DiscoveryMethod);

internal static class ManagerScanner
{
    private const ulong MaximumUserAddress = 0x0000800000000000;
    private const int BufferSize = 4 * 1024 * 1024;
    private const int ChunkOverlap = 64;

    public static ManagerScanResult Find(
        SafeProcessHandle process,
        ulong moduleBase,
        int moduleSize,
        SlotDefinition[] definitions,
        IProgress<GameProgress>? progress,
        CancellationToken cancellationToken)
    {
        var profiles = GameLayout.ManagerVtableProfiles
            .Select(profile => new RuntimeManagerProfile(
                profile,
                moduleBase + profile.VtableRva,
                moduleBase + profile.SecondVtableRva,
                definitions.ToDictionary(
                    definition => definition.Slot,
                    _ => new Dictionary<ulong, ManagerCandidate>())))
            .ToArray();
        var poolAddresses = definitions.ToDictionary(
            definition => definition.Slot,
            _ => new HashSet<ulong>());
        var poolPatterns = definitions.ToDictionary(
            definition => definition.Slot,
            CreatePoolPattern);

        var buffer = new byte[BufferSize];
        var scanned = 0L;
        var reportWatch = Stopwatch.StartNew();
        var allocationGroups = EnumerateWritablePrivateRegions(process, cancellationToken)
            .GroupBy(region => region.AllocationBase)
            .Select(group => new AllocationGroup(
                group.Key,
                group.OrderBy(region => region.BaseAddress).ToArray(),
                group.Aggregate(0UL, (total, region) => AddClamped(total, region.RegionSize))))
            .OrderByDescending(group => group.Regions.Length)
            .ThenByDescending(group => group.TotalBytes)
            .ThenBy(group => group.AllocationBase)
            .ToArray();
        var largestGroup = allocationGroups.FirstOrDefault();
        AppLogger.Info(
            $"Manager scan plan: groups={allocationGroups.Length}; " +
            $"regions={allocationGroups.Sum(group => group.Regions.Length)}; " +
            $"bytes={allocationGroups.Aggregate(0UL, (total, group) => AddClamped(total, group.TotalBytes))}; " +
            $"largest=0x{largestGroup?.AllocationBase ?? 0:X16}/" +
            $"{largestGroup?.Regions.Length ?? 0}/{largestGroup?.TotalBytes ?? 0}.");

        var visitedGroups = 0;
        foreach (var group in allocationGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            visitedGroups++;
            foreach (var region in group.Regions)
            {
                var regionBase = region.BaseAddress;
                var nextAddress = AddClamped(regionBase, region.RegionSize);
                for (var current = regionBase; current < nextAddress;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var remaining = nextAddress - current;
                    var requested = checked((int)Math.Min((ulong)buffer.Length, remaining));
                    if (ReadChunk(process, current, buffer, requested, out var count))
                    {
                        scanned += count;
                        foreach (var profile in profiles)
                        {
                            ScanKnownVtable(
                                process,
                                buffer,
                                count,
                                current,
                                moduleBase,
                                moduleSize,
                                profile.Vtable,
                                profile.SecondVtable,
                                definitions,
                                profile.Matches);
                        }
                        ScanPoolArrays(buffer, count, current, definitions, poolPatterns, poolAddresses);
                    }

                    ReportProgress(progress, reportWatch, scanned, "正在定位活动展位（优先游戏主堆）");
                    current = Advance(current, requested, remaining);
                }
            }

            var resolvedProfiles = profiles
                .Where(profile => definitions.All(
                    definition => profile.Matches[definition.Slot].Count == 1))
                .ToArray();
            if (resolvedProfiles.Length == 1)
            {
                var resolvedProfile = resolvedProfiles[0];
                var resolved = definitions
                    .Select(definition => resolvedProfile.Matches[definition.Slot].Values.Single())
                    .ToArray();
                LogPrimaryScan(
                    scanned,
                    visitedGroups,
                    group,
                    profiles,
                    definitions,
                    poolAddresses);
                return new ManagerScanResult(
                    resolved,
                    $"{resolvedProfile.Profile.Name} 已验证 RVA/vtable");
            }
        }

        LogPrimaryScan(
            scanned,
            visitedGroups,
            null,
            profiles,
            definitions,
            poolAddresses);
        progress?.Report(new GameProgress("正在执行兼容性回退定位", scanned));
        var orderedRegions = allocationGroups.SelectMany(group => group.Regions).ToArray();
        var fallbackMatches = FindManagerReferences(
            process,
            moduleBase,
            moduleSize,
            definitions,
            poolAddresses,
            orderedRegions,
            progress,
            cancellationToken,
            ref scanned,
            out var referenceCounts,
            out var rejectionSamples);

        var fallbackSummary = string.Join(
            Environment.NewLine,
            definitions.Select(definition =>
            {
                var slot = definition.Slot;
                var strictCount = profiles.Sum(profile => profile.Matches[slot].Count);
                var samples = rejectionSamples[slot].Count == 0
                    ? "none"
                    : string.Join(" | ", rejectionSamples[slot]);
                return $"Slot {slot}: strict={strictCount}, " +
                       $"fallback={fallbackMatches[slot].Count}, " +
                       $"poolArrays={poolAddresses[slot].Count} " +
                       $"[{FormatAddresses(poolAddresses[slot])}], " +
                       $"poolRefs={referenceCounts[slot]}, rejects={samples}.";
            }));
        AppLogger.Info($"Manager compatibility fallback completed: totalBytes={scanned}." +
                       Environment.NewLine + fallbackSummary);

        var result = new List<ManagerCandidate>();
        foreach (var definition in definitions)
        {
            var strict = profiles
                .SelectMany(profile => profile.Matches[definition.Slot].Values)
                .DistinctBy(candidate => candidate.Address)
                .ToArray();
            if (strict.Length == 1)
            {
                result.Add(strict[0]);
                continue;
            }

            var fallback = fallbackMatches[definition.Slot].Values.ToArray();
            if (fallback.Length != 1)
            {
                throw new GameToolException(
                    "无法安全识别三个活动展位。请确认已进入“意大利奇珍”场地，并等待车辆列表完全显示后重试。",
                    fallbackSummary);
            }
            result.Add(fallback[0]);
        }

        return new ManagerScanResult(result.OrderBy(candidate => candidate.Definition.Slot).ToArray(),
            "池数组引用回退");
    }

    private static void ScanKnownVtable(
        SafeProcessHandle process,
        byte[] buffer,
        int count,
        ulong bufferAddress,
        ulong moduleBase,
        int moduleSize,
        ulong expectedVtable,
        ulong expectedSecondVtable,
        SlotDefinition[] definitions,
        IReadOnlyDictionary<int, Dictionary<ulong, ManagerCandidate>> matches)
    {
        var alignedCount = count - count % sizeof(ulong);
        var qwords = MemoryMarshal.Cast<byte, ulong>(buffer.AsSpan(0, alignedCount));
        var searchIndex = 0;
        while (searchIndex < qwords.Length)
        {
            var relativeIndex = qwords[searchIndex..].IndexOf(expectedVtable);
            if (relativeIndex < 0)
            {
                break;
            }

            var matchIndex = searchIndex + relativeIndex;
            var candidateAddress = bufferAddress + checked((ulong)(matchIndex * sizeof(ulong)));
            foreach (var definition in definitions)
            {
                var candidate = ManagerCandidate.TryRead(
                    process,
                    candidateAddress,
                    moduleBase,
                    moduleSize,
                    definition,
                    expectedVtable,
                    expectedSecondVtable);
                if (candidate is not null)
                {
                    matches[definition.Slot][candidate.Address] = candidate;
                }
            }

            searchIndex = matchIndex + 1;
        }
    }

    private static void ScanPoolArrays(
        byte[] buffer,
        int count,
        ulong bufferAddress,
        SlotDefinition[] definitions,
        IReadOnlyDictionary<int, PoolPattern> patterns,
        IReadOnlyDictionary<int, HashSet<ulong>> matches)
    {
        var span = buffer.AsSpan(0, count);
        foreach (var definition in definitions)
        {
            var pattern = patterns[definition.Slot];
            var searchOffset = 0;
            while (searchOffset <= span.Length - pattern.AnchorBytes.Length)
            {
                var relative = span[searchOffset..].IndexOf(pattern.AnchorBytes);
                if (relative < 0)
                {
                    break;
                }

                var anchorOffset = searchOffset + relative;
                var anchorAddress = bufferAddress + checked((ulong)anchorOffset);
                if ((anchorAddress & (sizeof(int) - 1)) == 0)
                {
                    for (var anchorIndex = 0; anchorIndex < pattern.IdCount; anchorIndex++)
                    {
                        var candidateOffset = anchorOffset - anchorIndex * sizeof(int);
                        if (candidateOffset < 0 ||
                            candidateOffset + pattern.ByteCount > span.Length ||
                            !MatchesPool(span, candidateOffset, pattern))
                        {
                            continue;
                        }

                        matches[definition.Slot].Add(
                            bufferAddress + checked((ulong)candidateOffset));
                    }
                }
                searchOffset = anchorOffset + 1;
            }
        }
    }

    private static Dictionary<int, Dictionary<ulong, ManagerCandidate>> FindManagerReferences(
        SafeProcessHandle process,
        ulong moduleBase,
        int moduleSize,
        SlotDefinition[] definitions,
        IReadOnlyDictionary<int, HashSet<ulong>> poolAddresses,
        IReadOnlyList<WritableMemoryRegion> regions,
        IProgress<GameProgress>? progress,
        CancellationToken cancellationToken,
        ref long scanned,
        out Dictionary<int, int> referenceCounts,
        out Dictionary<int, List<string>> rejectionSamples)
    {
        var matches = definitions.ToDictionary(
            definition => definition.Slot,
            _ => new Dictionary<ulong, ManagerCandidate>());
        referenceCounts = definitions.ToDictionary(definition => definition.Slot, _ => 0);
        rejectionSamples = definitions.ToDictionary(
            definition => definition.Slot,
            _ => new List<string>());
        var seenReferences = definitions.ToDictionary(
            definition => definition.Slot,
            _ => new HashSet<ulong>());
        var poolToDefinitions = new Dictionary<ulong, List<SlotDefinition>>();
        foreach (var definition in definitions)
        {
            foreach (var poolAddress in poolAddresses[definition.Slot])
            {
                if (!poolToDefinitions.TryGetValue(poolAddress, out var owners))
                {
                    owners = [];
                    poolToDefinitions[poolAddress] = owners;
                }
                owners.Add(definition);
            }
        }

        if (poolToDefinitions.Count == 0)
        {
            return matches;
        }

        var buffer = new byte[BufferSize];
        var reportWatch = Stopwatch.StartNew();
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regionBase = region.BaseAddress;
            var nextAddress = AddClamped(regionBase, region.RegionSize);
            for (var current = regionBase; current < nextAddress;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = nextAddress - current;
                var requested = checked((int)Math.Min((ulong)buffer.Length, remaining));
                if (ReadChunk(process, current, buffer, requested, out var count))
                {
                    scanned += count;
                    var alignedCount = count - count % sizeof(ulong);
                    var qwords = MemoryMarshal.Cast<byte, ulong>(buffer.AsSpan(0, alignedCount));
                    for (var index = 0; index < qwords.Length; index++)
                    {
                        var poolAddress = qwords[index];
                        if (!poolToDefinitions.TryGetValue(poolAddress, out var owners))
                        {
                            continue;
                        }

                        var pointerAddress = current + checked((ulong)(index * sizeof(ulong)));
                        if (pointerAddress < GameLayout.PoolStartOffset)
                        {
                            continue;
                        }
                        var candidateAddress = pointerAddress - GameLayout.PoolStartOffset;
                        foreach (var definition in owners)
                        {
                            if (!seenReferences[definition.Slot].Add(pointerAddress))
                            {
                                continue;
                            }
                            referenceCounts[definition.Slot]++;
                            var candidate = ManagerCandidate.TryRead(
                                process,
                                candidateAddress,
                                moduleBase,
                                moduleSize,
                                definition,
                                out var rejectionReason);
                            if (candidate is not null && candidate.PoolStart == poolAddress)
                            {
                                matches[definition.Slot][candidate.Address] = candidate;
                            }
                            else if (rejectionSamples[definition.Slot].Count < 6)
                            {
                                var nearby = DescribePoolReference(
                                    process,
                                    pointerAddress,
                                    poolAddress,
                                    moduleBase,
                                    moduleSize,
                                    definition);
                                rejectionSamples[definition.Slot].Add(
                                    $"pool 0x{poolAddress:X16} ref 0x{pointerAddress:X16} " +
                                    $"manager 0x{candidateAddress:X16}: {rejectionReason}; {nearby}");
                            }
                        }
                    }
                }

                ReportProgress(progress, reportWatch, scanned, "正在验证展位引用");
                current = Advance(current, requested, remaining);
            }
        }

        return matches;
    }

    private static string DescribePoolReference(
        SafeProcessHandle process,
        ulong pointerAddress,
        ulong poolAddress,
        ulong moduleBase,
        int moduleSize,
        SlotDefinition definition)
    {
        const ulong lookBehind = 0x800;
        try
        {
            if (!TryQuery(process, pointerAddress, out var region))
            {
                return "nearby region unavailable";
            }

            var regionEnd = AddClamped(region.BaseAddress, region.RegionSize);
            var requestedStart = pointerAddress > lookBehind
                ? pointerAddress - lookBehind
                : 0;
            var start = Math.Max(region.BaseAddress, requestedStart);
            var end = Math.Min(regionEnd, AddClamped(pointerAddress, 3 * sizeof(ulong)));
            if (end <= start || end - start > int.MaxValue)
            {
                return "nearby range invalid";
            }

            var bytes = MemoryAccess.ReadBytes(process, start, checked((int)(end - start)));
            var span = bytes.AsSpan();
            var pointerIndex = checked((int)(pointerAddress - start));
            if (pointerIndex + 3 * sizeof(ulong) > span.Length)
            {
                return "nearby vector truncated";
            }

            var poolEnd = MemoryMarshal.Read<ulong>(span.Slice(pointerIndex + sizeof(ulong)));
            var poolCapacity = MemoryMarshal.Read<ulong>(span.Slice(pointerIndex + 2 * sizeof(ulong)));
            var expectedEnd = poolAddress + checked((ulong)(definition.PoolIds.Length * sizeof(int)));
            var vectorValid = poolEnd == expectedEnd &&
                              poolCapacity >= poolEnd &&
                              poolCapacity - poolAddress <= 4096;
            var currentId = pointerIndex >= 2 * sizeof(ulong)
                ? MemoryMarshal.Read<int>(span.Slice(pointerIndex - 2 * sizeof(ulong)))
                : int.MinValue;
            var pendingId = pointerIndex >= sizeof(ulong)
                ? MemoryMarshal.Read<int>(span.Slice(pointerIndex - sizeof(ulong)))
                : int.MinValue;
            definition.ModelByAftermarketId.TryGetValue(currentId, out var expectedModel);

            var moduleEnd = moduleBase + checked((ulong)moduleSize);
            var strong = new List<string>();
            var other = new List<string>();
            for (var index = 0; index + 2 * sizeof(ulong) <= pointerIndex; index += sizeof(ulong))
            {
                var candidateAddress = start + checked((ulong)index);
                if ((candidateAddress & 0xF) != 0)
                {
                    continue;
                }

                var vtable = MemoryMarshal.Read<ulong>(span.Slice(index));
                var secondVtable = MemoryMarshal.Read<ulong>(span.Slice(index + sizeof(ulong)));
                if (vtable < moduleBase || vtable >= moduleEnd ||
                    secondVtable < moduleBase || secondVtable >= moduleEnd ||
                    index + GameLayout.CarModelIdOffset + sizeof(int) > span.Length)
                {
                    continue;
                }

                var modelId = MemoryMarshal.Read<int>(
                    span.Slice(index + GameLayout.CarModelIdOffset));
                var description =
                    $"base=0x{candidateAddress:X16}/poolOff=0x{pointerAddress - candidateAddress:X}" +
                    $"/vt=0x{vtable - moduleBase:X}/vt2=0x{secondVtable - moduleBase:X}" +
                    $"/model={modelId}";
                if (expectedModel != 0 && modelId == expectedModel)
                {
                    strong.Add(description);
                }
                else if (other.Count < 4)
                {
                    other.Add(description);
                }
            }

            var candidates = strong.Count > 0 ? strong : other;
            return $"nearby vector={(vectorValid ? "valid" : "invalid")}" +
                   $"({poolEnd:X16}/{poolCapacity:X16}), current={currentId}, pending={pendingId}, " +
                   $"objectHints=[{string.Join(", ", candidates.Take(8))}]";
        }
        catch (Exception exception)
        {
            return $"nearby probe failed ({exception.GetType().Name}: {exception.Message})";
        }
    }

    internal static IReadOnlyList<int> FindPoolOffsetsForTest(
        byte[] buffer,
        SlotDefinition definition)
    {
        var matches = new Dictionary<int, HashSet<ulong>>
        {
            [definition.Slot] = []
        };
        ScanPoolArrays(
            buffer,
            buffer.Length,
            0,
            [definition],
            new Dictionary<int, PoolPattern>
            {
                [definition.Slot] = CreatePoolPattern(definition)
            },
            matches);
        return matches[definition.Slot].Select(address => checked((int)address)).Order().ToArray();
    }

    private static bool ReadChunk(
        SafeProcessHandle process,
        ulong address,
        byte[] buffer,
        int requested,
        out int count)
    {
        if (NativeMethods.ReadProcessMemory(
                process,
                (nint)address,
                buffer,
                (nuint)requested,
                out var bytesRead) && bytesRead > 0)
        {
            count = checked((int)bytesRead);
            return true;
        }
        count = 0;
        return false;
    }

    private static bool TryQuery(
        SafeProcessHandle process,
        ulong address,
        out NativeMethods.MemoryBasicInformation64 region) =>
        NativeMethods.VirtualQueryEx(
            process,
            (nint)address,
            out region,
            (nuint)Marshal.SizeOf<NativeMethods.MemoryBasicInformation64>()) != 0;

    internal static bool IsWritablePrivate(NativeMethods.MemoryBasicInformation64 region)
    {
        if (region.State != NativeMethods.MemCommit || region.Type != NativeMethods.MemPrivate ||
            (region.Protect & (NativeMethods.PageGuard | NativeMethods.PageNoAccess)) != 0)
        {
            return false;
        }

        const uint writable =
            NativeMethods.PageReadWrite |
            NativeMethods.PageWriteCopy |
            NativeMethods.PageExecuteReadWrite |
            NativeMethods.PageExecuteWriteCopy;
        return (region.Protect & writable) != 0;
    }

    private static PoolPattern CreatePoolPattern(SlotDefinition definition)
    {
        var anchorBytes = new byte[sizeof(int)];
        Buffer.BlockCopy(definition.PoolIds, 0, anchorBytes, 0, sizeof(int));
        return new PoolPattern(
            anchorBytes,
            definition.PoolIds
                .Select((id, index) => (id, index))
                .ToDictionary(item => item.id, item => item.index),
            definition.PoolIds.Length);
    }

    private static bool MatchesPool(ReadOnlySpan<byte> buffer, int offset, PoolPattern pattern)
    {
        ulong seen = 0;
        for (var index = 0; index < pattern.IdCount; index++)
        {
            var id = MemoryMarshal.Read<int>(
                buffer.Slice(offset + index * sizeof(int), sizeof(int)));
            if (!pattern.BitById.TryGetValue(id, out var bit))
            {
                return false;
            }

            var mask = 1UL << bit;
            if ((seen & mask) != 0)
            {
                return false;
            }
            seen |= mask;
        }
        return seen == pattern.FullMask;
    }

    private static void LogPrimaryScan(
        long scanned,
        int visitedGroups,
        AllocationGroup? resolvedGroup,
        IReadOnlyList<RuntimeManagerProfile> profiles,
        IEnumerable<SlotDefinition> definitions,
        IReadOnlyDictionary<int, HashSet<ulong>> poolAddresses)
    {
        var definitionArray = definitions.ToArray();
        var profileSummary = string.Join("; ", profiles.Select(profile =>
            $"{profile.Profile.Name}=0x{profile.Vtable:X16}/0x{profile.SecondVtable:X16} " +
            $"[{string.Join(',', definitionArray.Select(definition => profile.Matches[definition.Slot].Count))}]"));
        var pools = string.Join("; ", definitionArray.Select(definition =>
            $"S{definition.Slot} pools={poolAddresses[definition.Slot].Count} " +
            $"[{FormatAddresses(poolAddresses[definition.Slot])}]"));
        var resolved = resolvedGroup is null
            ? "none"
            : $"0x{resolvedGroup.AllocationBase:X16}/" +
              $"{resolvedGroup.Regions.Length}/{resolvedGroup.TotalBytes}";
        AppLogger.Info(
            $"Manager primary scan completed: bytes={scanned}; groupsVisited={visitedGroups}; " +
            $"resolvedGroup={resolved}; profiles={profileSummary}; {pools}.");
    }

    private static IReadOnlyList<WritableMemoryRegion> EnumerateWritablePrivateRegions(
        SafeProcessHandle process,
        CancellationToken cancellationToken)
    {
        var regions = new List<WritableMemoryRegion>();
        var address = 0UL;
        while (address < MaximumUserAddress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryQuery(process, address, out var region))
            {
                break;
            }

            var nextAddress = AddClamped(region.BaseAddress, region.RegionSize);
            if (IsWritablePrivate(region) && region.RegionSize > 0)
            {
                regions.Add(new WritableMemoryRegion(
                    region.BaseAddress,
                    region.AllocationBase == 0 ? region.BaseAddress : region.AllocationBase,
                    region.RegionSize));
            }
            if (nextAddress <= address)
            {
                break;
            }
            address = nextAddress;
        }
        return regions;
    }

    private static string FormatAddresses(IEnumerable<ulong> addresses)
    {
        const int limit = 16;
        var values = addresses.Order().Take(limit + 1).ToArray();
        var text = string.Join(", ", values.Take(limit).Select(address => $"0x{address:X16}"));
        return values.Length > limit ? text + ", ..." : text;
    }

    private static ulong Advance(ulong current, int requested, ulong remaining)
    {
        var advance = remaining > (ulong)requested && requested > ChunkOverlap
            ? requested - ChunkOverlap
            : requested;
        return current + checked((ulong)advance);
    }

    private static ulong AddClamped(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static void ReportProgress(
        IProgress<GameProgress>? progress,
        Stopwatch watch,
        long scanned,
        string message)
    {
        if (watch.ElapsedMilliseconds < 300)
        {
            return;
        }
        watch.Restart();
        progress?.Report(new GameProgress(message, scanned));
    }

    private sealed record PoolPattern(
        byte[] AnchorBytes,
        IReadOnlyDictionary<int, int> BitById,
        int IdCount)
    {
        public int ByteCount { get; } = checked(IdCount * sizeof(int));
        public ulong FullMask { get; } = (1UL << IdCount) - 1;
    }

    private sealed record RuntimeManagerProfile(
        ManagerVtableProfile Profile,
        ulong Vtable,
        ulong SecondVtable,
        IReadOnlyDictionary<int, Dictionary<ulong, ManagerCandidate>> Matches);

    private sealed record WritableMemoryRegion(
        ulong BaseAddress,
        ulong AllocationBase,
        ulong RegionSize);

    private sealed record AllocationGroup(
        ulong AllocationBase,
        WritableMemoryRegion[] Regions,
        ulong TotalBytes);
}
