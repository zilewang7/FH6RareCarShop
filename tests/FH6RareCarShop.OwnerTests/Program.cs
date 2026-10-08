using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FH6ItalianRarities.Core;
using FH6ItalianRarities.Models;
using Microsoft.Win32.SafeHandles;

// All mutations are to this harness's own allocation. The production resolver
// receives a read/query-only handle to this process; no game process is opened.
const int moduleSize = 0x7000000;
var memory = Marshal.AllocHGlobal(moduleSize);
try
{
    var b = (ulong)memory;
    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    var asm = typeof(ForzaGameService).Assembly;
    var native = asm.GetType("FH6ItalianRarities.Core.NativeMethods")!;
    using var handle = (SafeProcessHandle)native.GetMethod("OpenProcess", flags)!
        .Invoke(null, [0x410u, false, Environment.ProcessId])!;
    if (handle.IsInvalid) throw new Exception("Cannot open self read-only");
    var definition = ((Array)asm.GetType("FH6ItalianRarities.Core.SlotDefinition")!
        .GetMethod("ForActivity", flags)!.Invoke(null, [CarCatalog.DefaultActivity])!).GetValue(0)!;
    var candidateType = asm.GetType("FH6ItalianRarities.Core.ManagerCandidate")!;
    var tryRead = candidateType.GetMethods(flags).Single(m => m.Name == "TryRead" && m.GetParameters().Length == 7);
    var resolver = asm.GetType("FH6ItalianRarities.Core.AftermarketSaveStateResolver")!.GetMethod("Resolve", flags)!;
    ulong m = b + 0x1000, owner = b + 0x2000, state = b + 0x3000, elig = b + 0x4000;
    ulong secondState = b + 0x5000, vector = b + 0x6000, pool = b + 0x7000, getter = b + 0x9000;
    var cars = CarCatalog.ForSlot(CarCatalog.DefaultActivity.Id, 1);
    var results = new List<object>();
    void Q(ulong address, ulong value) => Marshal.WriteInt64((nint)address, unchecked((long)value));
    void I(ulong address, int value) => Marshal.WriteInt32((nint)address, value);
    void Byte(ulong address, byte value) => Marshal.WriteByte((nint)address, value);
    void Bytes(ulong address, byte[] bytes) => Marshal.Copy(bytes, 0, (nint)address, bytes.Length);
    void State(ulong address)
    {
        Q(address, b + 0x6B0F1D8); Q(address + 8, b + 0x6B0F328);
        Q(address + 0x10, owner); Q(address + 0x60, elig);
    }
    void Reset()
    {
        Bytes(b, new byte[0x10000]);
        Q(m, b + 0x6B0EAE8); Q(m + 8, b + 0x6B0EC38); Q(m + 0x10, owner);
        I(m + 0x38, cars[0].CarModelId); I(m + 0x278, cars[0].AftermarketId); I(m + 0x280, -1);
        Q(m + 0x288, pool); Q(m + 0x290, pool + (ulong)cars.Count * 4); Q(m + 0x298, pool + (ulong)cars.Count * 4);
        for (int i = 0; i < cars.Count; i++) I(pool + (ulong)i * 4, cars[i].AftermarketId);
        Q(owner, b + 0x6E18D18); Q(owner + 8, vector); Q(owner + 16, vector + 24); Q(owner + 24, vector + 24);
        Q(vector, m + 8); Q(vector + 8, state + 8); Q(vector + 16, elig + 8);
        State(state); State(secondState);
        Q(elig, b + 0x6DB01A8); Q(elig + 8, b + 0x6DB0310); Q(elig + 0x10, owner);
        Bytes(elig + 0x20, Encoding.ASCII.GetBytes("SeeItDriveIt")); Q(elig + 0x30, 12); Q(elig + 0x38, 15); Byte(elig + 0x70, 1);
        Q(b + 0x6DB01A8 + 0x118, getter); Bytes(getter, [0x0f, 0xb6, 0x41, 0x70, 0xc3]);
        foreach (ulong slot in new ulong[] { 0x120, 0x128, 0x130 }) Q(b + 0x6B0F1D8 + slot, b + 0x9100);
    }
    void Test(string name, Action mutate, bool expectSuccess = false)
    {
        Reset();
        var candidate = tryRead.Invoke(null, [handle, m, b, moduleSize, definition, null, null])
            ?? throw new Exception("Valid fixture manager rejected");
        var candidates = Array.CreateInstance(candidateType, 1); candidates.SetValue(candidate, 0);
        mutate();
        bool success; string detail;
        try
        {
            var resolution = resolver.Invoke(null, [handle, b, moduleSize, candidates, CancellationToken.None])!;
            var dictionary = resolution.GetType().GetProperty("ByManager")!.GetValue(resolution)!;
            var selected = dictionary.GetType().GetProperty("Item")!.GetValue(dictionary, [m])!;
            var actualState = (ulong)selected.GetType().GetProperty("Address")!.GetValue(selected)!;
            if (actualState != state) throw new Exception("Resolver chose wrong state");
            success = true; detail = "Unique expected SaveState resolved";
        }
        catch (TargetInvocationException ex) when (ex.InnerException is GameToolException ge)
        {
            success = false; detail = ge.Message;
        }
        if (success != expectSuccess) throw new Exception($"FAILED {name}: {detail}");
        Console.WriteLine($"PASS {name}: {(success ? "accepted" : "rejected")}");
        results.Add(new { name, expected = expectSuccess ? "accepted" : "rejected", passed = true, detail });
    }
    void RevalidateTest(string name, Action mutate, bool expectSuccess = false)
    {
        Reset();
        var candidate = tryRead.Invoke(null, [handle, m, b, moduleSize, definition, null, null])!;
        var candidates = Array.CreateInstance(candidateType, 1); candidates.SetValue(candidate, 0);
        var resolution = resolver.Invoke(null, [handle, b, moduleSize, candidates, CancellationToken.None])!;
        var dictionary = resolution.GetType().GetProperty("ByManager")!.GetValue(resolution)!;
        var selected = dictionary.GetType().GetProperty("Item")!.GetValue(dictionary, [m])!;
        if (selected.GetType().GetProperty("OwnerBinding")!.GetValue(selected) is null)
            throw new Exception("Fixture resolved without owner binding");
        mutate();
        bool success; string detail;
        try
        {
            asm.GetType("FH6ItalianRarities.Core.AftermarketSaveStateResolver")!
                .GetMethod("RevalidateOwnerBinding", flags)!.Invoke(null, [handle, b, moduleSize, selected]);
            success = true; detail = "Original owner/state/eligibility identity revalidated";
        }
        catch (TargetInvocationException ex) when (ex.InnerException is GameToolException ge)
        {
            success = false; detail = ge.Message;
        }
        if (success != expectSuccess) throw new Exception($"FAILED {name}: {detail}");
        Console.WriteLine($"PASS {name}: {(success ? "accepted" : "rejected")}");
        results.Add(new { name, expected = expectSuccess ? "accepted" : "rejected", passed = true, detail });
    }
    Test("valid unique owner association", () => {}, true);
    Test("valid boolean false", () => Byte(elig + 0x70, 0), true);
    Test("component order independent", () => { Q(vector, elig + 8); Q(vector + 16, m + 8); }, true);
    Test("missing manager membership", () => Q(owner + 8, vector + 8));
    Test("duplicate component", () => Q(vector + 16, state + 8));
    Test("state belongs to another owner", () => Q(state + 0x10, owner + 0x100));
    Test("eligibility belongs to another owner", () => Q(elig + 0x10, owner + 0x100));
    Test("two valid SaveStates", () => { Q(vector + 24, secondState + 8); Q(owner + 16, vector + 32); Q(owner + 24, vector + 32); });
    Test("eligibility absent from owner collection", () => Q(owner + 16, vector + 16));
    Test("getter signature mismatch", () => Byte(getter, 0x90));
    Test("getter outside module", () => Q(b + 0x6DB01A8 + 0x118, b + moduleSize));
    Test("invalid eligibility flag", () => Byte(elig + 0x70, 2));
    Test("marker mismatch", () => Byte(elig + 0x20, 0));
    Test("marker length mismatch", () => Q(elig + 0x30, 13));
    Test("marker capacity mismatch", () => Q(elig + 0x38, 16));
    Test("owner type mismatch", () => Q(owner, b + 0x6E18D20));
    Test("state second vtable mismatch", () => Q(state + 8, b + 0x6B0F330));
    Test("eligibility second vtable mismatch", () => Q(elig + 8, b + 0x6DB0318));
    Test("predicate function outside module", () => Q(b + 0x6B0F1D8 + 0x120, b + moduleSize));
    Test("oversize component vector", () => Q(owner + 24, vector + 8192 + 8));
    Test("reversed component vector", () => Q(owner + 16, vector - 8));
    Test("capacity before end", () => Q(owner + 24, vector + 16));
    Test("unaligned vector", () => Q(owner + 8, vector + 1));
    Test("unaligned component", () => Q(vector + 8, state + 9));
    Test("empty component vector", () => Q(owner + 16, vector));
    Test("unreadable null owner", () => Q(m + 0x10, 0));
    RevalidateTest("unchanged owner binding", () => {}, true);
    RevalidateTest("original identity survives vector relocation", () =>
    {
        ulong relocated = b + 0xb000;
        Q(relocated, m + 8); Q(relocated + 8, state + 8); Q(relocated + 16, elig + 8);
        Q(owner + 8, relocated); Q(owner + 16, relocated + 24); Q(owner + 24, relocated + 24);
    }, true);
    RevalidateTest("eligibility pointer swapped after resolution", () => Q(state + 0x60, b + 0xa000));
    RevalidateTest("valid eligibility replacement with unchanged vector bounds", () =>
    {
        ulong replacement = b + 0xa000;
        var copy = new byte[0x80]; Marshal.Copy((nint)elig, copy, 0, copy.Length); Bytes(replacement, copy);
        Q(state + 0x60, replacement); Q(vector + 16, replacement + 8);
    });
    RevalidateTest("valid SaveState replacement with unchanged vector bounds", () => Q(vector + 8, secondState + 8));
    RevalidateTest("manager membership removed with unchanged vector bounds", () => Q(vector, secondState + 8));
    RevalidateTest("duplicate component introduced after resolution", () => Q(vector + 16, state + 8));
    RevalidateTest("same objects transferred to another valid owner", () =>
    {
        ulong replacement = b + 0xa000;
        Q(replacement, b + 0x6E18D18); Q(replacement + 8, vector);
        Q(replacement + 16, vector + 24); Q(replacement + 24, vector + 24);
        Q(m + 0x10, replacement); Q(state + 0x10, replacement); Q(elig + 0x10, replacement);
    });
    RevalidateTest("getter invalidated after resolution", () => Byte(getter, 0x90));
    RevalidateTest("flag invalidated after resolution", () => Byte(elig + 0x70, 2));
    Console.WriteLine($"Passed {results.Count} synthetic integration cases.");
}
finally { Marshal.FreeHGlobal(memory); }
