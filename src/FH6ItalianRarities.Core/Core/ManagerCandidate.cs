using Microsoft.Win32.SafeHandles;

namespace FH6ItalianRarities.Core;

internal sealed record ManagerCandidate(
    SlotDefinition Definition,
    ulong Address,
    ulong Vtable,
    ulong SecondVtable,
    int CurrentId,
    int CarModelId,
    int PendingId,
    ulong PoolStart,
    ulong PoolEnd,
    ulong PoolCapacity,
    int[] PoolIds)
{
    public static ManagerCandidate? TryRead(
        SafeProcessHandle process,
        ulong address,
        ulong moduleBase,
        int moduleSize,
        SlotDefinition definition,
        ulong? exactVtable = null,
        ulong? exactSecondVtable = null)
    {
        return TryRead(
            process,
            address,
            moduleBase,
            moduleSize,
            definition,
            out _,
            exactVtable,
            exactSecondVtable);
    }

    public static ManagerCandidate? TryRead(
        SafeProcessHandle process,
        ulong address,
        ulong moduleBase,
        int moduleSize,
        SlotDefinition definition,
        out string rejectionReason,
        ulong? exactVtable = null,
        ulong? exactSecondVtable = null)
    {
        rejectionReason = string.Empty;
        if (!MemoryAccess.TryRead<ulong>(process, address, out var vtable) ||
            !MemoryAccess.TryRead<ulong>(process, address + 8, out var secondVtable))
        {
            rejectionReason = "manager header unreadable";
            return null;
        }
        if (!MemoryAccess.TryRead<int>(process, address + GameLayout.CurrentIdOffset, out var currentId) ||
            !MemoryAccess.TryRead<int>(process, address + GameLayout.CarModelIdOffset, out var carModelId) ||
            !MemoryAccess.TryRead<int>(process, address + GameLayout.PendingIdOffset, out var pendingId) ||
            !MemoryAccess.TryRead<ulong>(process, address + GameLayout.PoolStartOffset, out var poolStart) ||
            !MemoryAccess.TryRead<ulong>(process, address + GameLayout.PoolEndOffset, out var poolEnd) ||
            !MemoryAccess.TryRead<ulong>(process, address + GameLayout.PoolCapacityOffset, out var poolCapacity))
        {
            rejectionReason = "Xbox-layout fields unreadable";
            return null;
        }

        var moduleEnd = moduleBase + checked((ulong)moduleSize);
        if (exactVtable.HasValue)
        {
            if (vtable != exactVtable.Value || secondVtable != exactSecondVtable)
            {
                rejectionReason =
                    $"vtable mismatch ({vtable:X16}/{secondVtable:X16})";
                return null;
            }
        }
        else if (vtable < moduleBase || vtable >= moduleEnd ||
                 secondVtable < moduleBase || secondVtable >= moduleEnd ||
                 (vtable & 7) != 0 || (secondVtable & 7) != 0)
        {
            rejectionReason =
                $"vtables outside module ({vtable:X16}/{secondVtable:X16})";
            return null;
        }

        var expectedPoolBytes = checked((ulong)(definition.PoolIds.Length * sizeof(int)));
        if (poolStart < 0x10000 || poolEnd != poolStart + expectedPoolBytes ||
            poolCapacity < poolEnd || poolCapacity - poolStart > 4096 ||
            ((poolCapacity - poolStart) % sizeof(int)) != 0)
        {
            rejectionReason =
                $"invalid pool vector ({poolStart:X16}/{poolEnd:X16}/{poolCapacity:X16})";
            return null;
        }

        int[] poolIds;
        try
        {
            poolIds = MemoryAccess.ReadInt32Array(process, poolStart, definition.PoolIds.Length);
        }
        catch (Exception exception)
        {
            rejectionReason = $"pool unreadable ({exception.GetType().Name})";
            return null;
        }

        if (!poolIds.Order().SequenceEqual(definition.PoolIds.Order()))
        {
            rejectionReason = $"pool IDs differ ({string.Join(',', poolIds)})";
            return null;
        }
        if (!definition.ModelByAftermarketId.TryGetValue(currentId, out var expectedModel))
        {
            rejectionReason = $"current ID {currentId} is outside slot {definition.Slot}";
            return null;
        }
        if (carModelId != expectedModel)
        {
            rejectionReason = $"model mismatch ({carModelId}, expected {expectedModel})";
            return null;
        }
        if (pendingId != -1 && !definition.PoolIds.Contains(pendingId))
        {
            rejectionReason = $"pending ID {pendingId} is outside slot {definition.Slot}";
            return null;
        }

        return new ManagerCandidate(
            definition,
            address,
            vtable,
            secondVtable,
            currentId,
            carModelId,
            pendingId,
            poolStart,
            poolEnd,
            poolCapacity,
            poolIds);
    }
}
