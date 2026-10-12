using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FH6ItalianRarities.Core;

// No game process is opened and no keyboard input is sent by these tests.
// Reflection lets the new feature report a test failure before its types exist,
// and keeps this executable independent of the WPF application.
var tests = new (string Name, Func<Task> Run)[]
{
    ("defaults match nine-second continuous repeat", Defaults),
    ("finite loop restores between purchases only", FiniteLoop),
    ("requested quantity ten executes ten purchase rounds", RequestedQuantity),
    ("single purchase ignores unlimited repeat limit", SinglePurchase),
    ("invalid timing is rejected before any game action", InvalidOptions),
    ("cancelled countdown sends no input", CancelCountdown),
    ("cancellation releases Enter before stopping", CancelKeyHold),
    ("lost focus releases Enter and stops", LoseFocusDuringKeyHold),
    ("lost focus before preparation makes no game changes", LoseFocusBeforePrepare),
    ("preparation failure sends no input", PrepareFailure),
    ("lost focus cancels preparation before any purchase", LoseFocusDuringPrepare),
    ("restore failure stops the loop", RestoreFailure),
    ("stop waits for an active restore transaction", CancelDuringRestore),
    ("lost focus during restore prevents the next purchase", LoseFocusDuringRestore),
    ("one runner cannot run two loops at once", ConcurrentStart),
    ("unlimited mode stops on cancellation", UnlimitedLoop),
    ("cancellation during purchase wait skips restore", CancelPurchaseWait),
    ("cancellation during refresh wait skips next purchase", CancelRestoreWait),
    ("runner can start again after completion", Restart),
    ("Windows input layout and Enter scan codes are correct", InputLayout),
    ("keyboard adapter rejects an unrelated process", RejectUnrelatedProcess),
    ("parameters persist and reload", SettingsRoundTrip),
    ("older preferences retain timings and enable the new overlay", OlderSettings),
    ("corrupt parameters fall back without overwriting the file", CorruptSettings)
};
var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run().WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL {test.Name}: {exception.GetType().Name}: {exception.Message}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} automation tests passed.");
return failures == 0 ? 0 : 1;

static Type FeatureType(string name) => typeof(ForzaGameService).Assembly
    .GetType("FH6ItalianRarities.Core." + name)
    ?? throw new Exception($"Automatic purchase feature is missing: {name}");

static void Set(object options, string name, object value) =>
    options.GetType().GetProperty(name)!.SetValue(options, value);

static object Options()
{
    var options = Activator.CreateInstance(FeatureType("AutoPurchaseOptions"))!;
    Set(options, "EnterDuration", TimeSpan.FromMilliseconds(100));
    Set(options, "EnterInterval", TimeSpan.FromMilliseconds(60));
    Set(options, "StartDelay", TimeSpan.Zero);
    Set(options, "PurchaseDelay", TimeSpan.Zero);
    Set(options, "RestoreDelay", TimeSpan.Zero);
    Set(options, "MaxRounds", 2);
    return options;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static async Task Expect<T>(Func<Task> run) where T : Exception
{
    try { await run(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

static Task Defaults()
{
    var options = Activator.CreateInstance(FeatureType("AutoPurchaseOptions"))!;
    Require((TimeSpan)options.GetType().GetProperty("EnterDuration")!.GetValue(options)! == TimeSpan.FromSeconds(9), "Expected 9 seconds.");
    Require((TimeSpan)options.GetType().GetProperty("EnterInterval")!.GetValue(options)! == TimeSpan.FromMilliseconds(120), "Expected 120 ms.");
    Require((TimeSpan)options.GetType().GetProperty("PurchaseDelay")!.GetValue(options)! == TimeSpan.Zero, "Screenshot default purchase wait should be zero.");
    Require((TimeSpan)options.GetType().GetProperty("RestoreDelay")!.GetValue(options)! == TimeSpan.Zero, "Screenshot default restore wait should be zero.");
    Require((TimeSpan)options.GetType().GetProperty("StartDelay")!.GetValue(options)! == TimeSpan.FromSeconds(5), "Expected 5 second countdown.");
    Require((bool)options.GetType().GetProperty("ShowOverlay")!.GetValue(options)!, "Overlay should be enabled by default.");
    Require((bool)options.GetType().GetProperty("RepeatPurchase")!.GetValue(options)!, "Repeat should be enabled.");
    Require((int)options.GetType().GetProperty("MaxRounds")!.GetValue(options)! == 0, "Zero should mean unlimited.");
    return Task.CompletedTask;
}

static async Task FiniteLoop()
{
    var harness = new Harness();
    var options = Options();
    Set(options, "MaxRounds", 3);
    var rounds = await harness.Run(options);
    Require(rounds == 3 && harness.Restores == 2, "Expected three purchases with two restores.");
    Require(harness.Events[0] == "prepare", "Preparation must precede input.");
    Require(harness.Events.Last() == "up", "Do not restore after the final round.");
    Require(harness.Downs == harness.Ups && harness.Downs >= 3, "Enter should always be released.");
    Require(!harness.Pressed, "Enter remained pressed.");
}

static async Task SinglePurchase()
{
    var harness = new Harness();
    var options = Options();
    Set(options, "RepeatPurchase", false);
    Set(options, "MaxRounds", 0);
    Require(await harness.Run(options) == 1 && harness.Restores == 0, "Single mode should finish without restoring.");
}

static async Task RequestedQuantity()
{
    var harness = new Harness();
    var options = Options();
    Set(options, "MaxRounds", 10);
    Require(await harness.Run(options) == 10 && harness.Restores == 9,
        "A quantity of ten must perform ten rounds and stop without an eleventh restore/purchase.");
    Require(!harness.Pressed, "Enter remained pressed after reaching the requested quantity.");
}

static async Task InvalidOptions()
{
    (string, object)[] invalid =
    [
        ("EnterDuration", TimeSpan.Zero), ("EnterDuration", TimeSpan.FromSeconds(121)),
        ("EnterInterval", TimeSpan.Zero), ("EnterInterval", TimeSpan.FromMilliseconds(59)),
        ("EnterInterval", TimeSpan.FromSeconds(3)),
        ("StartDelay", TimeSpan.FromMilliseconds(-1)), ("StartDelay", TimeSpan.FromSeconds(61)),
        ("PurchaseDelay", TimeSpan.FromSeconds(-1)), ("PurchaseDelay", TimeSpan.FromSeconds(61)),
        ("RestoreDelay", TimeSpan.FromSeconds(-1)), ("RestoreDelay", TimeSpan.FromSeconds(61)),
        ("MaxRounds", -1)
    ];
    foreach (var (name, value) in invalid)
    {
        var harness = new Harness();
        var options = Options();
        Set(options, name, value);
        await Expect<ArgumentException>(async () => { await harness.Run(options); });
        Require(harness.Events.Count == 0, $"Invalid {name} caused a game action.");
    }
}

static async Task CancelCountdown()
{
    var harness = new Harness();
    var options = Options();
    Set(options, "StartDelay", TimeSpan.FromSeconds(1));
    using var cancellation = new CancellationTokenSource(20);
    await Expect<OperationCanceledException>(async () => { await harness.Run(options, cancellation.Token); });
    Require(harness.Events.Count == 0, "Cancelled countdown should make no game changes.");
}

static async Task CancelKeyHold()
{
    var harness = new Harness();
    using var cancellation = new CancellationTokenSource();
    harness.OnDown = cancellation.Cancel;
    await Expect<OperationCanceledException>(async () => { await harness.Run(Options(), cancellation.Token); });
    Require(harness.Downs == 1 && harness.Ups == 1 && !harness.Pressed, "Cancellation must release the active key.");
    Require(harness.Restores == 0, "Cancelled input must not start a restore.");
}

static async Task LoseFocusDuringKeyHold()
{
    var harness = new Harness();
    harness.OnDown = () => harness.Foreground = false;
    await Expect<GameToolException>(async () => { await harness.Run(Options()); });
    Require(harness.Downs == 1 && harness.Ups == 1 && !harness.Pressed, "Focus loss must release Enter.");
    Require(harness.Restores == 0, "Focus loss must not start a restore.");
}

static async Task LoseFocusBeforePrepare()
{
    var harness = new Harness { Foreground = false };
    await Expect<GameToolException>(async () => { await harness.Run(Options()); });
    Require(harness.Events.Count == 0, "Do not prepare or send input while outside the game.");
}

static async Task PrepareFailure()
{
    var harness = new Harness { OnPrepare = _ => throw new GameToolException("prepare failed") };
    await Expect<GameToolException>(async () => { await harness.Run(Options()); });
    Require(harness.Downs == 0 && harness.Restores == 0, "Failed preparation must stop input.");
}

static async Task LoseFocusDuringPrepare()
{
    var harness = new Harness();
    var cancellationObserved = false;
    harness.OnPrepare = async token =>
    {
        harness.Foreground = false;
        try { await Task.Delay(1000, token); }
        catch (OperationCanceledException) { cancellationObserved = true; throw; }
    };
    await Expect<GameToolException>(async () => { await harness.Run(Options()); });
    Require(cancellationObserved && harness.Downs == 0 && harness.Restores == 0,
        "Focus loss must cancel preparatory work, not allow more writes or input.");
}

static async Task RestoreFailure()
{
    var harness = new Harness { OnRestore = _ => throw new GameToolException("restore failed") };
    await Expect<GameToolException>(async () => { await harness.Run(Options()); });
    Require(harness.Restores == 1 && !harness.Pressed, "Restore failure should terminate and release input.");
    var restoreIndex = harness.Events.IndexOf("restore");
    Require(!harness.Events.Skip(restoreIndex + 1).Contains("down"), "Do not buy after a failed restore.");
}

static async Task CancelDuringRestore()
{
    var harness = new Harness();
    using var cancellation = new CancellationTokenSource();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    harness.OnRestore = token =>
    {
        Require(!token.CanBeCanceled, "An active restore must complete its transaction.");
        cancellation.Cancel();
        entered.SetResult();
        return finished.Task;
    };
    var running = harness.Run(Options(), cancellation.Token);
    await entered.Task;
    Require(!running.IsCompleted, "Stop must wait for transaction cleanup.");
    finished.SetResult();
    await Expect<OperationCanceledException>(async () => { await running; });
    Require(harness.Events.Last() == "restore" && !harness.Pressed, "No further input after stop.");
}

static async Task LoseFocusDuringRestore()
{
    var harness = new Harness();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    harness.OnRestore = _ => { entered.SetResult(); return finished.Task; };
    var running = harness.Run(Options());
    await entered.Task;
    harness.Foreground = false;
    await Task.Delay(120);
    Require(!running.IsCompleted, "Focus loss must wait for restoration to finish.");
    harness.Foreground = true; // Even a brief focus loss should stop the loop.
    finished.SetResult();
    await Expect<GameToolException>(async () => { await running; });
    Require(harness.Events.Last() == "restore", "Focus loss should skip the next purchase.");
}

static async Task ConcurrentStart()
{
    var harness = new Harness();
    var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var cancellation = new CancellationTokenSource();
    harness.OnPrepare = _ => prepared.Task;
    var running = harness.Run(Options(), cancellation.Token);
    await Expect<InvalidOperationException>(async () => { await harness.Run(Options()); });
    cancellation.Cancel();
    prepared.SetResult();
    await Expect<OperationCanceledException>(async () => { await running; });
    Require(harness.Downs == 0, "Cancelled preparation must send no input.");
}

static async Task UnlimitedLoop()
{
    var harness = new Harness();
    using var cancellation = new CancellationTokenSource();
    harness.OnRestore = _ => { if (harness.Restores == 3) cancellation.Cancel(); return Task.CompletedTask; };
    var options = Options();
    Set(options, "MaxRounds", 0);
    await Expect<OperationCanceledException>(async () => { await harness.Run(options, cancellation.Token); });
    Require(harness.Restores == 3 && harness.Downs == harness.Ups, "Unlimited mode should repeat until stopped.");
}

static async Task CancelPurchaseWait()
{
    var harness = new Harness();
    using var cancellation = new CancellationTokenSource();
    harness.OnUp = () => cancellation.CancelAfter(150);
    var options = Options();
    Set(options, "PurchaseDelay", TimeSpan.FromSeconds(1));
    await Expect<OperationCanceledException>(async () => { await harness.Run(options, cancellation.Token); });
    Require(harness.Restores == 0, "Stop during the purchase wait should skip restoration.");
}

static async Task CancelRestoreWait()
{
    var harness = new Harness();
    using var cancellation = new CancellationTokenSource();
    harness.OnRestore = _ => { cancellation.CancelAfter(30); return Task.CompletedTask; };
    var options = Options();
    Set(options, "RestoreDelay", TimeSpan.FromSeconds(1));
    await Expect<OperationCanceledException>(async () => { await harness.Run(options, cancellation.Token); });
    Require(harness.Restores == 1 && harness.Events.Last() == "restore", "Stop during reload wait should skip the next purchase.");
}

static async Task Restart()
{
    var harness = new Harness();
    var options = Options();
    Set(options, "RepeatPurchase", false);
    Require(await harness.Run(options) == 1 && await harness.Run(options) == 1, "Runner should be reusable.");
}

static Task InputLayout()
{
    var inputType = FeatureType("ForzaGameInput");
    var packetType = inputType.GetNestedType("Input", BindingFlags.NonPublic)!;
    Require(Marshal.SizeOf(packetType) == 40, "Windows x64 INPUT must occupy 40 bytes.");
    var create = inputType.GetMethod("CreateEnterInput", BindingFlags.Static | BindingFlags.NonPublic)!;
    foreach (var pressed in new[] { true, false })
    {
        var packet = create.Invoke(null, [pressed])!;
        var data = packetType.GetField("Data")!.GetValue(packet)!;
        var keyboard = data.GetType().GetField("Keyboard")!.GetValue(data)!;
        Require((uint)packetType.GetField("Type")!.GetValue(packet)! == 1, "Expected keyboard input.");
        Require((ushort)keyboard.GetType().GetField("ScanCode")!.GetValue(keyboard)! == 0x1C, "Expected Enter scan code.");
        Require((uint)keyboard.GetType().GetField("Flags")!.GetValue(keyboard)! == (pressed ? 8u : 10u), "Expected scan-code and key-up flags.");
    }
    return Task.CompletedTask;
}

static Task RejectUnrelatedProcess()
{
    var type = FeatureType("ForzaGameInput");
    try { Activator.CreateInstance(type, [Environment.ProcessId]); }
    catch (TargetInvocationException exception) when (exception.InnerException is GameToolException) { return Task.CompletedTask; }
    throw new Exception("Keyboard input must reject a non-game process.");
}

static Task SettingsRoundTrip()
{
    WithSettings((store, path) =>
    {
        var options = Options();
        Set(options, "EnterDuration", TimeSpan.FromSeconds(9.5));
        Set(options, "MaxRounds", 25);
        Set(options, "ShowOverlay", false);
        store.GetType().GetMethod("Save")!.Invoke(store, [options]);
        var loaded = store.GetType().GetMethod("Load")!.Invoke(store, null)!;
        Require(loaded.Equals(options), "Saved parameters changed on reload.");
        Require(File.Exists(path), "Parameters were not saved.");
    });
    return Task.CompletedTask;
}

static Task OlderSettings()
{
    WithSettings((store, path) =>
    {
        var options = Options();
        Set(options, "PurchaseDelay", TimeSpan.FromSeconds(2));
        store.GetType().GetMethod("Save")!.Invoke(store, [options]);
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json.Remove("ShowOverlay");
        File.WriteAllText(path, json.ToJsonString());
        var original = File.ReadAllText(path);
        var loaded = store.GetType().GetMethod("Load")!.Invoke(store, null)!;
        Require(loaded.Equals(options), "Older timings should remain while the overlay defaults to enabled.");
        Require(File.ReadAllText(path) == original, "Loading older settings must not rewrite preferences.");
    });
    return Task.CompletedTask;
}

static Task CorruptSettings()
{
    WithSettings((store, path) =>
    {
        File.WriteAllText(path, "invalid-json");
        var loaded = store.GetType().GetMethod("Load")!.Invoke(store, null)!;
        Require(loaded.Equals(Activator.CreateInstance(FeatureType("AutoPurchaseOptions"))), "Corrupt settings should return defaults.");
        Require(File.ReadAllText(path) == "invalid-json", "Loading must not overwrite malformed settings.");
        var options = Options();
        Set(options, "EnterInterval", TimeSpan.Zero);
        try { store.GetType().GetMethod("Save")!.Invoke(store, [options]); }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException) { return; }
        throw new Exception("Invalid settings must not be saved.");
    });
    return Task.CompletedTask;
}

static void WithSettings(Action<object, string> test)
{
    var type = typeof(ForzaGameService).Assembly.GetType("FH6ItalianRarities.Infrastructure.AutoPurchaseSettingsStore")
        ?? throw new Exception("Automatic purchase feature is missing: AutoPurchaseSettingsStore");
    var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "automation-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "settings.json");
    try { test(Activator.CreateInstance(type, [path])!, path); }
    finally { File.Delete(path); File.Delete(path + ".tmp"); Directory.Delete(directory); }
}

sealed class Harness
{
    private object? _runner;
    public List<string> Events { get; } = [];
    public bool Foreground { get; set; } = true;
    public bool Pressed { get; private set; }
    public int Downs => Events.Count(item => item == "down");
    public int Ups => Events.Count(item => item == "up");
    public int Restores => Events.Count(item => item == "restore");
    public Action? OnDown { get; set; }
    public Action? OnUp { get; set; }
    public Func<CancellationToken, Task>? OnPrepare { get; set; }
    public Func<CancellationToken, Task>? OnRestore { get; set; }

    public Task<int> Run(object options, CancellationToken token = default)
    {
        if (_runner is null)
        {
            var assembly = typeof(ForzaGameService).Assembly;
            var actionsType = assembly.GetType("FH6ItalianRarities.Core.AutoPurchaseActions")
                ?? throw new Exception("Automatic purchase feature is missing: AutoPurchaseActions");
            Func<CancellationToken, Task> prepare = cancellation =>
            {
                Events.Add("prepare");
                return OnPrepare?.Invoke(cancellation) ?? Task.CompletedTask;
            };
            Action foreground = () => { if (!Foreground) throw new GameToolException("Game lost foreground focus."); };
            Action down = () => { RequireNoOverlap(); Pressed = true; Events.Add("down"); OnDown?.Invoke(); };
            Action up = () => { if (!Pressed) return; Pressed = false; Events.Add("up"); OnUp?.Invoke(); };
            Func<CancellationToken, Task> restore = cancellation =>
            {
                if (Pressed) throw new Exception("Restore started while Enter was pressed.");
                Events.Add("restore");
                return OnRestore?.Invoke(cancellation) ?? Task.CompletedTask;
            };
            var actions = Activator.CreateInstance(actionsType, [prepare, foreground, down, up, restore])!;
            _runner = Activator.CreateInstance(assembly.GetType("FH6ItalianRarities.Core.AutoPurchaseRunner")!, [actions])!;
        }
        try
        {
            return (Task<int>)_runner.GetType().GetMethod("RunAsync")!.Invoke(_runner, [options, null, token])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private void RequireNoOverlap()
    {
        if (Pressed) throw new Exception("Overlapping Enter presses.");
    }
}
