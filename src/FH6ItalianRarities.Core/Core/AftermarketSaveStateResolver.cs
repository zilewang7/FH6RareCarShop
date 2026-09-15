using System.Runtime.InteropServices;
using FH6ItalianRarities.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace FH6ItalianRarities.Core;

internal sealed record SaveStateCandidate(
    ulong Address,
    ulong ManagerReference,
    ulong SaveStateReference);

internal sealed record SaveStateResolution(
    IReadOnlyDictionary<ulong, SaveStateCandidate> ByManager,
    string DiscoveryMethod);

internal static class AftermarketSaveStateResolver
{
    private const ulong MaximumReferenceDistance = 0x4000;
    private const ulong ManagerSearchRadius = 0x200000;
    private const ulong EligibilitySearchRadius = 0x400000;
    private static readonly byte[] EligibilityMarker =
        [0x53, 0x65, 0x65, 0x49, 0x74, 0x44, 0x72, 0x69, 0x76, 0x65, 0x49, 0x74];
    private static readonly long[] DerivedVtableDeltas =
        [0, 8, -8, 16, -16, 24, -24, 32, -32, 40, -40, 48, -48, 56, -56, 64, -64];

    public static SaveStateResolution Resolve(
        SafeProcessHandle process,
        ulong moduleBase,
        int moduleSize,
        IReadOnlyList<ManagerCandidate> managers,
        CancellationToken cancellationToken)
    {
        if (managers.Count == 0)
        {
            throw new ArgumentException("At least one manager is required.", nameof(managers));
        }

        var managerVtables = managers.Select(manager => manager.Vtable).Distinct().ToArray();
        var managerSecondVtables = managers.Select(manager => manager.SecondVtable).Distinct().ToArray();
        if (managerVtables.Length != 1 || managerSecondVtables.Length != 1)
        {
            throw new GameToolException(
                "活动展位的运行时类型不一致，已停止资格操作。",
                $"Manager vtables={managerVtables.Length}; second vtables={managerSecondVtables.Length}.");
        }

        if (moduleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleSize));
        }

        var attempts = new List<string>();
        var linkProfile = GameLayout.SaveStateLinkProfiles.SingleOrDefault(profile =>
            managerVtables[0] == moduleBase + profile.ManagerVtableRva &&
            managerSecondVtables[0] == moduleBase + profile.ManagerSecondVtableRva);
        if (linkProfile is not null)
        {
            var linkedResolution = TryResolveViaEligibilityObjects(
                process,
                moduleBase,
                moduleSize,
                managers,
                linkProfile,
                cancellationToken,
                out var linkedDiagnostic);
            var method = $"{linkProfile.Name} 资格对象反向关联";
            attempts.Add($"{method}: {linkedDiagnostic}");
            if (linkedResolution is not null)
            {
                AppLogger.Info(
                    $"SaveState resolution succeeded via {method}: " +
                    string.Join(", ", linkedResolution.Values
                        .OrderBy(candidate => candidate.ManagerReference)
                        .Select(candidate =>
                            $"0x{candidate.ManagerReference:X16}->" +
                            $"0x{candidate.SaveStateReference:X16}")));
                return new SaveStateResolution(linkedResolution, method);
            }
        }

        var managerAddresses = managers.Select(manager => manager.Address).ToArray();
        var searchStart = managerAddresses.Min() > ManagerSearchRadius
            ? managerAddresses.Min() - ManagerSearchRadius
            : 0;
        var searchEnd = AddClamped(managerAddresses.Max(), ManagerSearchRadius);
        var managerReferences = ReferenceScanner.FindValues(
            process,
            managerAddresses,
            searchStart,
            searchEnd,
            cancellationToken);
        var referenceSummary = string.Join(", ", managers.Select(manager =>
            $"S{manager.Definition.Slot}=" +
            managerReferences.Count(reference => reference.Value == manager.Address)));
        AppLogger.Info(
            $"SaveState manager references scanned once: range=0x{searchStart:X16}-0x{searchEnd:X16}; " +
            $"counts={referenceSummary}.");

        var vtablePairs = CreateVtablePairs(
            moduleBase,
            moduleSize,
            managerVtables[0],
            managerSecondVtables[0]);

        foreach (var pair in vtablePairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = TryResolve(
                process,
                moduleBase,
                moduleSize,
                managers,
                managerReferences,
                pair.Vtable,
                pair.SecondVtable,
                cancellationToken,
                out var diagnostic);
            attempts.Add($"{pair.Method}: {diagnostic}");
            if (resolution is not null)
            {
                AppLogger.Info(
                    $"SaveState resolution succeeded via {pair.Method}: " +
                    string.Join(", ", resolution.Values.OrderBy(candidate => candidate.ManagerReference)
                        .Select(candidate =>
                            $"0x{candidate.ManagerReference:X16}->0x{candidate.SaveStateReference:X16}")));
                return new SaveStateResolution(resolution, pair.Method);
            }
        }

        throw new GameToolException(
            "无法安全关联活动展位与购买资格状态，已停止重购操作。普通车辆切换不受影响。",
            "No unique validated AftermarketSaveState pairing matched the runtime layouts. " +
            string.Join(" | ", attempts));
    }

    private static IReadOnlyDictionary<ulong, SaveStateCandidate>? TryResolveViaEligibilityObjects(
        SafeProcessHandle process,
        ulong moduleBase,
        int moduleSize,
        IReadOnlyList<ManagerCandidate> managers,
        SaveStateLinkProfile profile,
        CancellationToken cancellationToken,
        out string diagnostic)
    {
        var result = new Dictionary<ulong, SaveStateCandidate>();
        var claimedSaveStates = new HashSet<ulong>();
        var summaries = new List<string>();
        foreach (var manager in managers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MemoryAccess.TryRead<ulong>(
                    process,
                    manager.Address + profile.ManagerEligibilityAnchorOffset,
                    out var anchor) ||
                !TryApplyDelta(anchor, profile.EligibilityReferenceDelta, out var eligibilityReference) ||
                !MemoryAccess.TryRead<ulong>(process, eligibilityReference, out var eligibilityObject))
            {
                diagnostic =
                    $"S{manager.Definition.Slot} eligibility link unreadable";
                return null;
            }
            if (!TryValidateEligibilityObject(
                    process,
                    moduleBase,
                    moduleSize,
                    eligibilityObject,
                    moduleBase + profile.EligibilityVtableRva,
                    moduleBase + profile.EligibilitySecondVtableRva,
                    out var eligibilityReason))
            {
                diagnostic =
                    $"S{manager.Definition.Slot} eligibility link invalid ({eligibilityReason})";
                return null;
            }

            var searchStart = eligibilityObject > EligibilitySearchRadius
                ? eligibilityObject - EligibilitySearchRadius
                : 0;
            var searchEnd = AddClamped(eligibilityObject, EligibilitySearchRadius);
            var eligibilityReferences = ReferenceScanner.FindValues(
                process,
                [eligibilityObject],
                searchStart,
                searchEnd,
                cancellationToken);
            var candidates = new List<(SaveStateCandidate Candidate, ulong Distance)>();
            foreach (var reference in eligibilityReferences)
            {
                if (reference.Address < GameLayout.PurchaseEligibilityObjectOffset)
                {
                    continue;
                }
                var saveStateAddress =
                    reference.Address - GameLayout.PurchaseEligibilityObjectOffset;
                if (claimedSaveStates.Contains(saveStateAddress) ||
                    !TryValidateSaveState(
                        process,
                        moduleBase,
                        moduleSize,
                        saveStateAddress,
                        moduleBase + profile.SaveStateVtableRva,
                        moduleBase + profile.SaveStateSecondVtableRva,
                        out _))
                {
                    continue;
                }

                candidates.Add((
                    new SaveStateCandidate(
                        saveStateAddress,
                        eligibilityReference,
                        reference.Address),
                    Distance(eligibilityObject, reference.Address)));
            }

            var unique = candidates
                .DistinctBy(candidate => candidate.Candidate.Address)
                .OrderBy(candidate => candidate.Distance)
                .ToArray();
            if (unique.Length != 1)
            {
                diagnostic =
                    $"S{manager.Definition.Slot} expected one SaveState for eligibility " +
                    $"0x{eligibilityObject:X16}, found {unique.Length}";
                return null;
            }

            var selected = unique[0].Candidate;
            claimedSaveStates.Add(selected.Address);
            result.Add(manager.Address, selected);
            summaries.Add(
                $"S{manager.Definition.Slot}=0x{selected.Address:X16}" +
                $"/eligibility=0x{eligibilityObject:X16}");
        }

        diagnostic = string.Join(", ", summaries);
        return result;
    }

    private static bool TryValidateEligibilityObject(
        SafeProcessHandle process,
        ulong moduleBase,
        int moduleSize,
        ulong address,
        ulong expectedVtable,
        ulong expectedSecondVtable,
        out string reason)
    {
        var moduleEnd = moduleBase + checked((ulong)moduleSize);
        if (address < 0x10000 || address >= 0x0000800000000000 || (address & 0xF) != 0 ||
            !MemoryAccess.TryRead<ulong>(process, address, out var vtable) ||
            vtable != expectedVtable ||
            !MemoryAccess.TryRead<ulong>(process, address + sizeof(ulong), out var secondVtable) ||
            secondVtable != expectedSecondVtable)
        {
            reason = "header mismatch";
            return false;
        }

        try
        {
            var marker = MemoryAccess.ReadBytes(process, address + 0x20, EligibilityMarker.Length);
            var getterFunction = MemoryAccess.Read<ulong>(
                process,
                vtable + GameLayout.PredicateCheckVtableSlot);
            var getterSignature = MemoryAccess.ReadBytes(
                process,
                getterFunction,
                GameLayout.EligibilityGetterSignature.Length);
            var flag = MemoryAccess.Read<byte>(
                process,
                address + GameLayout.PurchaseEligibilityFlagOffset);
            if (!marker.AsSpan().SequenceEqual(EligibilityMarker) ||
                MemoryAccess.Read<ulong>(process, address + 0x30) != (ulong)EligibilityMarker.Length ||
                MemoryAccess.Read<ulong>(process, address + 0x38) is < 12 or > 15 ||
                getterFunction < moduleBase || getterFunction >= moduleEnd ||
                !getterSignature.AsSpan().SequenceEqual(GameLayout.EligibilityGetterSignature) ||
                flag > 1)
            {
                reason = "marker, getter, or flag mismatch";
                return false;
            }
        }
        catch (Exception exception)
        {
            reason = $"unreadable ({exception.GetType().Name})";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static IReadOnlyDictionary<ulong, SaveStateCandidate>? TryResolve(
        SafeProcessHandle process,
        ulong moduleBase,
        int moduleSize,
        IReadOnlyList<ManagerCandidate> managers,
        IReadOnlyList<ValueReference> managerReferences,
        ulong expectedVtable,
        ulong expectedSecondVtable,
        CancellationToken cancellationToken,
        out string diagnostic)
    {
        var result = new Dictionary<ulong, SaveStateCandidate>();
        var claimedSaveStates = new HashSet<ulong>();
        var summaries = new List<string>();
        foreach (var manager in managers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pairings = new List<SaveStatePairing>();
            var rejectionSamples = new List<string>();
            foreach (var managerReference in managerReferences.Where(
                         reference => reference.Value == manager.Address))
            {
                var saveStateReferences = ReferenceScanner.FindObjectReferences(
                    process,
                    managerReference.Address,
                    MaximumReferenceDistance,
                    expectedVtable,
                    expectedSecondVtable,
                    cancellationToken);
                foreach (var saveStateReference in saveStateReferences)
                {
                    if (!TryValidateSaveState(
                            process,
                            moduleBase,
                            moduleSize,
                            saveStateReference.Value,
                            expectedVtable,
                            expectedSecondVtable,
                            out var rejectionReason))
                    {
                        if (rejectionSamples.Count < 3)
                        {
                            rejectionSamples.Add(
                                $"0x{saveStateReference.Value:X16}: {rejectionReason}");
                        }
                        continue;
                    }

                    pairings.Add(new SaveStatePairing(
                        managerReference.Address,
                        saveStateReference.Address,
                        saveStateReference.Value,
                        Distance(managerReference.Address, saveStateReference.Address)));
                }
            }

            var available = pairings
                .GroupBy(pairing => pairing.SaveStateAddress)
                .Select(group => group.OrderBy(pairing => pairing.Distance).First())
                .Where(pairing => !claimedSaveStates.Contains(pairing.SaveStateAddress))
                .OrderBy(pairing => pairing.Distance)
                .ToArray();
            if (available.Length == 0)
            {
                diagnostic = $"S{manager.Definition.Slot} no candidate" +
                    (rejectionSamples.Count == 0
                        ? string.Empty
                        : $" ({string.Join(", ", rejectionSamples)})");
                return null;
            }

            var best = available[0];
            if (available.Count(pairing => pairing.Distance == best.Distance) != 1)
            {
                diagnostic =
                    $"S{manager.Definition.Slot} ambiguous at distance 0x{best.Distance:X}";
                return null;
            }

            claimedSaveStates.Add(best.SaveStateAddress);
            result.Add(manager.Address, new SaveStateCandidate(
                best.SaveStateAddress,
                best.ManagerReference,
                best.SaveStateReference));
            summaries.Add(
                $"S{manager.Definition.Slot}=0x{best.SaveStateAddress:X16}/d0x{best.Distance:X}");
        }

        diagnostic = string.Join(", ", summaries);
        return result;
    }

    private static IReadOnlyList<VtablePair> CreateVtablePairs(
        ulong moduleBase,
        int moduleSize,
        ulong managerVtable,
        ulong managerSecondVtable)
    {
        var moduleEnd = moduleBase + checked((ulong)moduleSize);
        var pairs = new List<VtablePair>();
        var seen = new HashSet<(ulong Vtable, ulong SecondVtable)>();

        void Add(ulong vtable, ulong secondVtable, string method)
        {
            if (vtable < moduleBase || vtable >= moduleEnd ||
                secondVtable < moduleBase || secondVtable >= moduleEnd ||
                !seen.Add((vtable, secondVtable)))
            {
                return;
            }
            pairs.Add(new VtablePair(vtable, secondVtable, method));
        }

        Add(
            moduleBase + GameLayout.KnownAftermarketSaveStateVtableRva,
            moduleBase + GameLayout.KnownAftermarketSaveStateSecondVtableRva,
            "Xbox 6.403 已验证 RVA/vtable");

        var derivedVtable = managerVtable +
            (GameLayout.KnownAftermarketSaveStateVtableRva - GameLayout.KnownManagerVtableRva);
        var derivedSecondVtable = managerSecondVtable +
            (GameLayout.KnownAftermarketSaveStateSecondVtableRva - GameLayout.KnownManagerSecondVtableRva);
        var profile = GameLayout.ManagerVtableProfiles.SingleOrDefault(candidate =>
            managerVtable == moduleBase + candidate.VtableRva &&
            managerSecondVtable == moduleBase + candidate.SecondVtableRva);
        var profileName = profile?.Name ?? "未知版本";
        foreach (var delta in DerivedVtableDeltas)
        {
            if (!TryApplyDelta(derivedVtable, delta, out var vtable) ||
                !TryApplyDelta(derivedSecondVtable, delta, out var secondVtable))
            {
                continue;
            }
            var suffix = delta switch
            {
                > 0 => $"+0x{delta:X}",
                < 0 => $"-0x{-delta:X}",
                _ => "+0x0"
            };
            Add(vtable, secondVtable, $"{profileName} 相邻类型 {suffix}");
        }
        return pairs;
    }

    private static bool TryValidateSaveState(
        SafeProcessHandle process,
        ulong moduleBase,
        int moduleSize,
        ulong address,
        ulong expectedVtable,
        ulong expectedSecondVtable,
        out string reason)
    {
        var moduleEnd = moduleBase + checked((ulong)moduleSize);
        if (address < 0x10000 || address >= 0x0000800000000000 || (address & 7) != 0 ||
            !MemoryAccess.TryRead<ulong>(process, address, out var vtable) ||
            !MemoryAccess.TryRead<ulong>(process, address + sizeof(ulong), out var secondVtable) ||
            vtable != expectedVtable || secondVtable != expectedSecondVtable)
        {
            reason = "header mismatch";
            return false;
        }

        foreach (var slot in new[]
                 {
                     GameLayout.SaveStateValidVtableSlot,
                     GameLayout.SaveStateInvalidVtableSlot,
                     GameLayout.SaveStateEligibleVtableSlot
                 })
        {
            if (!MemoryAccess.TryRead<ulong>(process, vtable + slot, out var function) ||
                !IsInModule(function, moduleBase, moduleEnd))
            {
                reason = $"predicate function at vtable+0x{slot:X} is invalid";
                return false;
            }
        }

        if (!MemoryAccess.TryRead<ulong>(
                process,
                address + GameLayout.PurchaseEligibilityObjectOffset,
                out var eligibilityObject) ||
            eligibilityObject < 0x10000 || eligibilityObject >= 0x0000800000000000 ||
            (eligibilityObject & 7) != 0 ||
            !MemoryAccess.TryRead<ulong>(process, eligibilityObject, out var predicateVtable) ||
            !IsInModule(predicateVtable, moduleBase, moduleEnd) ||
            !MemoryAccess.TryRead<ulong>(
                process,
                predicateVtable + GameLayout.PredicateCheckVtableSlot,
                out var getterFunction) ||
            !IsInModule(getterFunction, moduleBase, moduleEnd))
        {
            reason = "eligibility object or getter is invalid";
            return false;
        }

        try
        {
            var signature = MemoryAccess.ReadBytes(
                process,
                getterFunction,
                GameLayout.EligibilityGetterSignature.Length);
            if (!signature.AsSpan().SequenceEqual(GameLayout.EligibilityGetterSignature))
            {
                reason = $"getter signature mismatch at 0x{getterFunction:X16}";
                return false;
            }
        }
        catch (Exception exception)
        {
            reason = $"getter unreadable ({exception.GetType().Name})";
            return false;
        }

        if (!MemoryAccess.TryRead<byte>(
                process,
                eligibilityObject + GameLayout.PurchaseEligibilityFlagOffset,
                out var flag) || flag > 1)
        {
            reason = "eligibility flag is not a boolean";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryApplyDelta(ulong value, long delta, out ulong result)
    {
        if (delta >= 0)
        {
            var amount = checked((ulong)delta);
            if (value > ulong.MaxValue - amount)
            {
                result = 0;
                return false;
            }
            result = value + amount;
            return true;
        }

        var magnitude = checked((ulong)-delta);
        if (value < magnitude)
        {
            result = 0;
            return false;
        }
        result = value - magnitude;
        return true;
    }

    private static bool IsInModule(ulong address, ulong moduleBase, ulong moduleEnd) =>
        address >= moduleBase && address < moduleEnd;

    private static ulong Distance(ulong left, ulong right) =>
        left >= right ? left - right : right - left;

    private static ulong AddClamped(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private sealed record VtablePair(ulong Vtable, ulong SecondVtable, string Method);

    private sealed record SaveStatePairing(
        ulong ManagerReference,
        ulong SaveStateReference,
        ulong SaveStateAddress,
        ulong Distance);
}

internal sealed record ValueReference(ulong Address, ulong Value);

internal static class ReferenceScanner
{
    private const int BufferSize = 4 * 1024 * 1024;

    public static IReadOnlyList<ValueReference> FindValues(
        SafeProcessHandle process,
        IEnumerable<ulong> values,
        ulong startAddress,
        ulong endAddress,
        CancellationToken cancellationToken)
    {
        var targets = values.ToHashSet();
        var matches = new List<ValueReference>();
        ScanWritableRegions(process, startAddress, endAddress, cancellationToken,
            (buffer, count, current) =>
            {
                var qwords = MemoryMarshal.Cast<byte, ulong>(
                    buffer.AsSpan(0, count - count % sizeof(ulong)));
                for (var index = 0; index < qwords.Length; index++)
                {
                    if (targets.Contains(qwords[index]))
                    {
                        matches.Add(new ValueReference(
                            current + checked((ulong)(index * sizeof(ulong))),
                            qwords[index]));
                    }
                }
            });
        return matches.DistinctBy(match => (match.Address, match.Value)).ToArray();
    }

    public static IReadOnlyList<ValueReference> FindObjectReferences(
        SafeProcessHandle process,
        ulong center,
        ulong radius,
        ulong expectedVtable,
        ulong expectedSecondVtable,
        CancellationToken cancellationToken)
    {
        var startAddress = (center > radius ? center - radius : 0) & ~7UL;
        var endAddress = ulong.MaxValue - center < radius ? ulong.MaxValue : center + radius;
        var matches = new List<ValueReference>();
        ScanWritableRegions(process, startAddress, endAddress, cancellationToken,
            (buffer, count, current) =>
            {
                var qwords = MemoryMarshal.Cast<byte, ulong>(
                    buffer.AsSpan(0, count - count % sizeof(ulong)));
                for (var index = 0; index < qwords.Length; index++)
                {
                    var value = qwords[index];
                    if (value < 0x10000 || value >= 0x0000800000000000 || (value & 0xF) != 0 ||
                        !MemoryAccess.TryRead<ulong>(process, value, out var vtable) ||
                        vtable != expectedVtable ||
                        !MemoryAccess.TryRead<ulong>(process, value + sizeof(ulong), out var secondVtable) ||
                        secondVtable != expectedSecondVtable)
                    {
                        continue;
                    }

                    matches.Add(new ValueReference(
                        current + checked((ulong)(index * sizeof(ulong))),
                        value));
                }
            });
        return matches.DistinctBy(match => (match.Address, match.Value)).ToArray();
    }

    private static void ScanWritableRegions(
        SafeProcessHandle process,
        ulong startAddress,
        ulong endAddress,
        CancellationToken cancellationToken,
        Action<byte[], int, ulong> scan)
    {
        var address = startAddress;
        var buffer = new byte[BufferSize];
        while (address < endAddress)
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

            var nextAddress = region.BaseAddress > ulong.MaxValue - region.RegionSize
                ? ulong.MaxValue
                : region.BaseAddress + region.RegionSize;
            if (ManagerScanner.IsWritablePrivate(region))
            {
                var scanStart = Math.Max(region.BaseAddress, startAddress);
                var scanEnd = Math.Min(nextAddress, endAddress);
                for (var current = scanStart; current < scanEnd;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var requested = checked((int)Math.Min((ulong)buffer.Length, scanEnd - current));
                    if (NativeMethods.ReadProcessMemory(
                            process,
                            (nint)current,
                            buffer,
                            (nuint)requested,
                            out var bytesRead) && bytesRead >= sizeof(ulong))
                    {
                        scan(buffer, checked((int)bytesRead), current);
                    }
                    current += checked((ulong)requested);
                }
            }

            if (nextAddress <= address)
            {
                break;
            }
            address = nextAddress;
        }
    }
}
