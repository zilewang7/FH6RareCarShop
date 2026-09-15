using FH6ItalianRarities.Models;

namespace FH6ItalianRarities.Core;

public sealed record OfflineSelfTestResult(
    int CatalogCars,
    int RebuyPlans,
    int SafetyPolicyCases,
    int PoolScanCases,
    int SetterStubBytes,
    int RefreshStubBytes,
    int PredicateStubBytes)
{
    public override string ToString() =>
        $"catalog={CatalogCars}; rebuy-plans={RebuyPlans}; " +
        $"safety-cases={SafetyPolicyCases}; pool-scan-cases={PoolScanCases}; " +
        $"stubs={SetterStubBytes}/{RefreshStubBytes}/{PredicateStubBytes}";
}

public static class OfflineSelfTest
{
    public static OfflineSelfTestResult Run()
    {
        CatalogValidator.Validate();
        ValidateLayout();

        var planCount = 0;
        foreach (var target in CarCatalog.All)
        {
            foreach (var current in CarCatalog.ForSlot(target.ActivityId, target.Slot))
            {
                var plan = RebuyPlanFactory.Create(target, current.AftermarketId);
                Require(plan.First.ActivityId == target.ActivityId &&
                        plan.Refresh.ActivityId == target.ActivityId &&
                        plan.First.Slot == target.Slot &&
                        plan.Refresh.Slot == target.Slot,
                    $"Staging plan escaped slot {target.Slot}.");
                Require(plan.First.AftermarketId != plan.Refresh.AftermarketId,
                    $"Staging cars collided for target {target.AftermarketId}.");
                Require(plan.First.AftermarketId != target.AftermarketId &&
                        plan.Refresh.AftermarketId != target.AftermarketId,
                    $"Staging plan reused target {target.AftermarketId}.");
                Require(plan.First.AftermarketId != current.AftermarketId &&
                        plan.Refresh.AftermarketId != current.AftermarketId,
                    $"Staging plan reused current car {current.AftermarketId}.");
                planCount++;
            }
        }

        ValidateKnownPlans();
        var safetyCases = ValidateSafetyPolicy();
        var poolScanCases = ValidatePoolScanning();
        var stubs = RemoteGameCall.ValidateOffline();
        return new OfflineSelfTestResult(
            CarCatalog.All.Count,
            planCount,
            safetyCases,
            poolScanCases,
            stubs.SetterBytes,
            stubs.RefreshBytes,
            stubs.PredicateBytes);
    }

    private static int ValidatePoolScanning()
    {
        var cases = 0;
        foreach (var definition in SlotDefinition.AllActivities)
        {
            var buffer = Enumerable.Repeat((byte)0xA5, 320).ToArray();
            WriteIds(buffer, 32, definition.PoolIds);
            WriteIds(buffer, 128, definition.PoolIds.Reverse().ToArray());

            var invalid = definition.PoolIds.ToArray();
            invalid[^1] = invalid[0];
            WriteIds(buffer, 224, invalid);

            var offsets = ManagerScanner.FindPoolOffsetsForTest(buffer, definition);
            Require(offsets.SequenceEqual(new[] { 32, 128 }),
                $"Unordered pool scan failed for slot {definition.Slot}: " +
                string.Join(',', offsets));
            cases += 3;
        }
        return cases;
    }

    private static void WriteIds(byte[] buffer, int offset, int[] ids)
    {
        Buffer.BlockCopy(ids, 0, buffer, offset, checked(ids.Length * sizeof(int)));
    }

    private static int ValidateSafetyPolicy()
    {
        var cases = new[]
        {
            (RefreshStarted: false, RefreshFinished: false, Unknown: false, Expected: true),
            (RefreshStarted: false, RefreshFinished: true, Unknown: false, Expected: true),
            (RefreshStarted: true, RefreshFinished: false, Unknown: false, Expected: false),
            (RefreshStarted: true, RefreshFinished: true, Unknown: false, Expected: true),
            (RefreshStarted: false, RefreshFinished: false, Unknown: true, Expected: false),
            (RefreshStarted: true, RefreshFinished: false, Unknown: true, Expected: false),
            (RefreshStarted: true, RefreshFinished: true, Unknown: true, Expected: false)
        };
        foreach (var test in cases)
        {
            Require(RebuySafetyPolicy.CanRollback(
                    test.RefreshStarted,
                    test.RefreshFinished,
                    test.Unknown) == test.Expected,
                $"Unexpected rollback policy for " +
                $"started={test.RefreshStarted}, finished={test.RefreshFinished}, unknown={test.Unknown}.");
        }
        return cases.Length;
    }

    private static void ValidateKnownPlans()
    {
        RequirePlan(targetId: 157, currentId: 157, firstId: 159, refreshId: 152);
        RequirePlan(targetId: 160, currentId: 160, firstId: 158, refreshId: 187);
        RequirePlan(targetId: 171, currentId: 171, firstId: 161, refreshId: 172);
        RequirePlan(targetId: 214, currentId: 214, firstId: 218, refreshId: 201);
        RequirePlan(targetId: 211, currentId: 211, firstId: 220, refreshId: 203);
    }

    private static void RequirePlan(int targetId, int currentId, int firstId, int refreshId)
    {
        var plan = RebuyPlanFactory.Create(CarCatalog.ByAftermarketId(targetId), currentId);
        Require(plan.First.AftermarketId == firstId && plan.Refresh.AftermarketId == refreshId,
            $"Unexpected staging plan for target {targetId}: " +
            $"{plan.First.AftermarketId}/{plan.Refresh.AftermarketId}.");
    }

    private static void ValidateLayout()
    {
        Require(GameLayout.ManagerVtableProfiles.Length >= 4,
            "Current and legacy Xbox and Steam manager profiles must be present.");
        Require(GameLayout.ManagerVtableProfiles
                    .Select(profile => (profile.VtableRva, profile.SecondVtableRva))
                    .Distinct()
                    .Count() == GameLayout.ManagerVtableProfiles.Length,
            "Manager vtable profiles must be unique.");
        foreach (var profile in GameLayout.ManagerVtableProfiles)
        {
            Require((profile.VtableRva & 7) == 0 && (profile.SecondVtableRva & 7) == 0,
                $"Manager vtables for {profile.Name} are not pointer-aligned.");
            Require(profile.SecondVtableRva > profile.VtableRva,
                $"Manager second vtable for {profile.Name} is not ordered.");
        }
        var legacySteamProfile = GameLayout.ManagerVtableProfiles.SingleOrDefault(
            profile => profile.Name == "Steam 6.403.798.0");
        Require(legacySteamProfile is
                {
                    VtableRva: 0x662F738,
                    SecondVtableRva: 0x662F888
                },
            "Legacy Steam manager vtable profile changed unexpectedly.");
        var currentSteamProfile = GameLayout.ManagerVtableProfiles.SingleOrDefault(
            profile => profile.Name == "Steam 6.440.853.0");
        Require(currentSteamProfile is
                {
                    VtableRva: 0x6AF5AC0,
                    SecondVtableRva: 0x6AF5C10
                },
            "Current Steam manager vtable profile changed unexpectedly.");
        var currentSteamSaveStateProfile = GameLayout.SaveStateLinkProfiles.SingleOrDefault(
            profile => profile.Name == "Steam 6.440.853.0");
        Require(currentSteamSaveStateProfile is
                {
                    ManagerVtableRva: 0x6AF5AC0,
                    ManagerSecondVtableRva: 0x6AF5C10,
                    SaveStateVtableRva: 0x6AF61A8,
                    SaveStateSecondVtableRva: 0x6AF62F8,
                    EligibilityVtableRva: 0x6D962E8,
                    EligibilitySecondVtableRva: 0x6D96450,
                    ManagerEligibilityAnchorOffset: 0x28,
                    EligibilityReferenceDelta: -0xC8
                },
            "Current Steam SaveState link profile changed unexpectedly.");
        var currentXboxProfile = GameLayout.ManagerVtableProfiles.SingleOrDefault(
            profile => profile.Name == "Xbox 3.440.853.0");
        Require(currentXboxProfile is
                {
                    VtableRva: 0x6AE3A10,
                    SecondVtableRva: 0x6AE3B60
                },
            "Current Xbox manager vtable profile changed unexpectedly.");
        Require(GameLayout.EligibilityGetterSignature.AsSpan().SequenceEqual(
                new byte[] { 0x0F, 0xB6, 0x41, 0x70, 0xC3 }),
            "Eligibility getter signature changed unexpectedly.");
        Require(GameLayout.PurchaseEligibilityObjectOffset == 0x60 &&
                GameLayout.PredicateCheckVtableSlot == 0x118 &&
                GameLayout.PurchaseEligibilityFlagOffset == 0x70,
            "Purchase-eligibility layout changed unexpectedly.");
        Require(GameLayout.SetterSignature.Count(value => value.HasValue) >= 25,
            "Setter signature is too weak for compatibility scanning.");
        Require(GameLayout.RefreshSignature.Count(value => value.HasValue) >= 35,
            "Refresh signature is too weak for compatibility scanning.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
