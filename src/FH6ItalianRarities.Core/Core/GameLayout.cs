using FH6ItalianRarities.Models;

namespace FH6ItalianRarities.Core;

internal static class GameLayout
{
    public const ulong KnownManagerVtableRva = 0x661D500;
    public const ulong KnownManagerSecondVtableRva = 0x661D650;
    public const ulong KnownAftermarketSaveStateVtableRva = 0x661DC10;
    public const ulong KnownAftermarketSaveStateSecondVtableRva = 0x661DD60;
    public const ulong KnownRefreshRva = 0x1522280;
    public const ulong KnownSetterRva = 0x1524000;

    public const ulong SaveStateValidVtableSlot = 0x120;
    public const ulong SaveStateInvalidVtableSlot = 0x128;
    public const ulong SaveStateEligibleVtableSlot = 0x130;
    public const ulong PurchaseEligibilityObjectOffset = 0x60;
    public const ulong PredicateCheckVtableSlot = 0x118;
    public const ulong PurchaseEligibilityFlagOffset = 0x70;

    public const int CarModelIdOffset = 0x38;
    public const int CurrentIdOffset = 0x278;
    public const int PendingIdOffset = 0x280;
    public const int PoolStartOffset = 0x288;
    public const int PoolEndOffset = 0x290;
    public const int PoolCapacityOffset = 0x298;

    public static readonly ManagerVtableProfile[] ManagerVtableProfiles =
    [
        new("Xbox 3.440.853.0", 0x6AE3A10, 0x6AE3B60),
        new("Steam 6.440.853.0", 0x6AF5AC0, 0x6AF5C10),
        new("Xbox 6.403", KnownManagerVtableRva, KnownManagerSecondVtableRva),
        new("Steam 6.403.798.0", 0x662F738, 0x662F888)
    ];

    public static readonly SaveStateLinkProfile[] SaveStateLinkProfiles =
    [
        new(
            "Steam 6.440.853.0",
            0x6AF5AC0,
            0x6AF5C10,
            0x6AF61A8,
            0x6AF62F8,
            0x6D962E8,
            0x6D96450,
            0x28,
            -0xC8)
    ];

    public static readonly byte?[] SetterSignature =
    [
        0x83, 0xFA, 0xFF, 0x0F, 0x84, null, null, null, null,
        0x48, 0x89, 0x5C, 0x24, 0x08, 0x44, 0x88, 0x4C, 0x24, 0x20,
        0x55, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57,
        0x48, 0x8B, 0xEC
    ];

    public static readonly byte?[] RefreshSignature =
    [
        0x48, 0x89, 0x5C, 0x24, 0x18,
        0x88, 0x54, 0x24, 0x10,
        0x55, 0x56, 0x57,
        0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57,
        0x48, 0x8B, 0xEC,
        0x48, 0x83, 0xEC, 0x50,
        0x48, 0x8B, 0xD9,
        0xC7, 0x45, 0x40, 0x00, 0x00, 0x00, 0x00,
        0x48, 0x81, 0xC1, 0x00, 0x02, 0x00, 0x00,
        0xE8, null, null, null, null,
        0x84, 0xC0
    ];

    public static readonly byte[] EligibilityGetterSignature =
        [0x0F, 0xB6, 0x41, 0x70, 0xC3];
}

internal sealed record ManagerVtableProfile(
    string Name,
    ulong VtableRva,
    ulong SecondVtableRva);

internal sealed record SaveStateLinkProfile(
    string Name,
    ulong ManagerVtableRva,
    ulong ManagerSecondVtableRva,
    ulong SaveStateVtableRva,
    ulong SaveStateSecondVtableRva,
    ulong EligibilityVtableRva,
    ulong EligibilitySecondVtableRva,
    ulong ManagerEligibilityAnchorOffset,
    long EligibilityReferenceDelta);

internal sealed record SlotDefinition(RareCarActivity Activity, int Slot, CarOption[] Cars)
{
    public string ActivityId => Activity.Id;

    public int[] PoolIds { get; } = Cars.Select(car => car.AftermarketId).ToArray();

    public IReadOnlyDictionary<int, int> ModelByAftermarketId { get; } =
        Cars.ToDictionary(car => car.AftermarketId, car => car.CarModelId);

    public static SlotDefinition[] ForActivity(RareCarActivity activity) =>
        Enumerable.Range(1, activity.SlotCount)
            .Select(slot => new SlotDefinition(
                activity,
                slot,
                CarCatalog.ForSlot(activity.Id, slot).ToArray()))
            .ToArray();

    public static SlotDefinition[] AllActivities { get; } =
        CarCatalog.Activities.SelectMany(ForActivity).ToArray();
}
