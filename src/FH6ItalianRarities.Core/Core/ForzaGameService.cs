using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FH6ItalianRarities.Infrastructure;
using FH6ItalianRarities.Models;
using Microsoft.Win32.SafeHandles;

namespace FH6ItalianRarities.Core;

public sealed class ForzaGameService : IDisposable
{
    private const string ProcessName = "forzahorizon6";
    private const uint ReadAccess =
        NativeMethods.ProcessQueryLimitedInformation |
        NativeMethods.ProcessQueryInformation |
        NativeMethods.ProcessVmRead;
    private const uint FullAccess =
        ReadAccess |
        NativeMethods.ProcessVmOperation |
        NativeMethods.ProcessVmWrite |
        NativeMethods.ProcessCreateThread;

    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ManagerCache? _managerCache;

    public GameProcessStatus GetProcessStatus()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        if (processes.Length == 0)
        {
            return new GameProcessStatus(false);
        }

        Process? selected = null;
        try
        {
            selected = SelectProcess(processes);
            string executablePath;
            string version;
            try
            {
                executablePath = selected.MainModule?.FileName ?? string.Empty;
                version = GetVersion(executablePath);
            }
            catch
            {
                executablePath = string.Empty;
                version = string.Empty;
            }
            return new GameProcessStatus(
                true,
                selected.Id,
                version,
                Path.GetFileName(executablePath));
        }
        finally
        {
            foreach (var process in processes)
            {
                if (!ReferenceEquals(process, selected))
                {
                    process.Dispose();
                }
            }
            selected?.Dispose();
        }
    }

    public async Task<GameSnapshot> InspectAsync(
        IProgress<GameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    progress?.Report(new GameProgress("正在连接游戏进程"));
                    using var context = OpenContext(writable: false);
                    var managers = ResolveManagers(context, progress, cancellationToken);
                    var setter = TryLocateSetter(context, cancellationToken);
                    var refresh = TryLocateRefresh(context, cancellationToken);
                    return CreateSnapshot(context, managers.Candidates, managers.DiscoveryMethod, setter, refresh);
                }
                catch (Exception exception)
                {
                    throw Normalize(exception, "只读兼容性检查失败");
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ApplyResult> ApplyAsync(
        IEnumerable<CarOption> selectedCars,
        IProgress<GameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var targets = selectedCars.OrderBy(car => car.Slot).ToArray();
        ValidateTargets(targets);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var context = OpenContext(writable: true);
                    var managers = ResolveManagers(context, progress, cancellationToken);
                    var setter = SetterLocator.Find(
                        context.Handle, context.ModuleBase, context.ModuleSize, cancellationToken);
                    var results = new List<SlotApplyResult>();
                    foreach (var target in targets)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(new GameProgress(
                            $"正在切换展位 {target.Slot}：{target.ChineseName}"));
                        var manager = managers.Candidates.Single(candidate => candidate.Definition.Slot == target.Slot);
                        var alreadySelected = manager.CurrentId == target.AftermarketId &&
                                              manager.CarModelId == target.CarModelId;
                        if (!alreadySelected)
                        {
                            SetCar(context, manager, setter.Address, target, cancellationToken);
                        }
                        results.Add(new SlotApplyResult(target.Slot, target, alreadySelected));
                    }

                    var currentManagers = RereadManagers(context, managers.Candidates);
                    var refresh = TryLocateRefresh(context, cancellationToken);
                    var snapshot = CreateSnapshot(
                        context,
                        currentManagers,
                        managers.DiscoveryMethod,
                        setter,
                        refresh);
                    AppLogger.Info("Applied slots: " + string.Join(", ",
                        results.Select(result =>
                            $"S{result.Slot}={result.Target.AftermarketId}/{result.Target.CarModelId}")));
                    return new ApplyResult(snapshot, results);
                }
                catch (Exception exception)
                {
                    throw Normalize(exception, "车辆切换失败，未完成的展位没有继续写入");
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PurchaseDiagnostic> DiagnosePurchaseAsync(
        int slot,
        IProgress<GameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSlot(slot);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var context = OpenContext(writable: true);
                    var managers = ResolveManagers(context, progress, cancellationToken);
                    var manager = managers.Candidates.Single(candidate => candidate.Definition.Slot == slot);
                    progress?.Report(new GameProgress("正在关联购买资格状态"));
                    var saveStates = ResolveSaveState(context, manager, cancellationToken);
                    return ReadPurchaseDiagnostic(
                        context,
                        manager,
                        saveStates.ByManager[manager.Address],
                        saveStates.DiscoveryMethod,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    throw Normalize(exception, "购买资格诊断失败");
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<RebuyResult> RestorePurchaseAsync(
        CarOption target,
        IProgress<GameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateSlot(target.Slot);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                var watch = Stopwatch.StartNew();
                ProcessContext? context = null;
                TemporarySingleCarPool? poolOverride = null;
                var changedFlags = new List<EligibilityMutation>();
                ManagerCandidate? initialManager = null;
                SetterLocation? setter = null;
                var refreshStarted = false;
                var refreshFinished = false;
                try
                {
                    context = OpenContext(writable: true);
                    var managers = ResolveManagers(context, progress, cancellationToken);
                    initialManager = managers.Candidates.Single(
                        candidate => candidate.Definition.Slot == target.Slot);
                    if (!initialManager.Definition.PoolIds.Contains(target.AftermarketId))
                    {
                        throw new GameToolException("所选车辆不属于这个展位，已停止操作。");
                    }

                    setter = SetterLocator.Find(
                        context.Handle, context.ModuleBase, context.ModuleSize, cancellationToken);
                    var refresh = RefreshLocator.Find(
                        context.Handle, context.ModuleBase, context.ModuleSize, cancellationToken);
                    AppLogger.Info(
                        $"Rebuy functions: setter=0x{setter.Address:X16} ({setter.DiscoveryMethod}); " +
                        $"refresh=0x{refresh.Address:X16} ({refresh.DiscoveryMethod}).");
                    var staging = RebuyPlanFactory.Create(target, initialManager.CurrentId);
                    progress?.Report(new GameProgress("正在验证并恢复购买资格"));
                    SaveStateResolution? initialSaveStates = null;
                    GameToolException? initialResolutionError = null;
                    try
                    {
                        initialSaveStates = ResolveSaveState(
                            context,
                            initialManager,
                            cancellationToken);
                    }
                    catch (GameToolException exception)
                    {
                        initialResolutionError = exception;
                        AppLogger.Warn(
                            $"Target SaveState was unavailable before staging; " +
                            $"slot={target.Slot}; manager=0x{initialManager.Address:X16}; " +
                            $"reason={exception.Message}");
                    }

                    if (initialSaveStates is not null)
                    {
                        RestoreEligibility(
                            context,
                            initialSaveStates.ByManager[initialManager.Address],
                            changedFlags,
                            cancellationToken);
                    }
                    else
                    {
                        progress?.Report(new GameProgress(
                            "目标展位交互已卸载，正在通过临时换车重新加载"));
                    }

                    progress?.Report(new GameProgress(
                        $"第一阶段：临时切换到 {staging.First.ChineseName}"));
                    var liveManager = SetCar(
                        context,
                        initialManager,
                        setter.Address,
                        staging.First,
                        cancellationToken);
                    if (initialSaveStates is null)
                    {
                        var reboundSaveStates = ResolveSaveStateWithRetry(
                            context,
                            liveManager,
                            TimeSpan.FromSeconds(12),
                            progress,
                            "目标展位交互",
                            cancellationToken);
                        RestoreEligibility(
                            context,
                            reboundSaveStates.ByManager[liveManager.Address],
                            changedFlags,
                            cancellationToken);
                        AppLogger.Info(
                            $"Target slot interaction rebound after staging switch; " +
                            $"slot={target.Slot}; prior={initialResolutionError?.Message}");
                    }

                    progress?.Report(new GameProgress(
                        "第二阶段：准备重建购买交互；请切回游戏并停留在活动场地"));
                    const int maximumRefreshAttempts = 4;
                    var refreshBase = liveManager;
                    ManagerCandidate? refreshedManager = null;
                    CarOption? actualRefreshCar = null;
                    for (var attempt = 1; attempt <= maximumRefreshAttempts; attempt++)
                    {
                        poolOverride = new TemporarySingleCarPool(
                            context.Handle,
                            refreshBase,
                            staging.Refresh.AftermarketId);
                        AppLogger.Info(
                            $"Temporary refresh pool armed (attempt {attempt}): " +
                            poolOverride.DescribeLiveState());
                        cancellationToken.ThrowIfCancellationRequested();
                        refreshStarted = true;
                        refreshFinished = false;
                        var refreshExitCode = await RemoteGameCall.InvokeRefreshAsync(
                            context.Handle,
                            refresh.Address,
                            refreshBase.Address,
                            progress,
                            cancellationToken).ConfigureAwait(false);
                        refreshFinished = true;
                        AppLogger.Info(
                            $"Refresh attempt {attempt} returned 0x{refreshExitCode:X8}: " +
                            poolOverride.DescribeLiveState());
                        poolOverride.Restore();
                        if (refreshExitCode != 0)
                        {
                            throw new InvalidOperationException(
                                $"Remote refresh stub returned 0x{refreshExitCode:X8}.");
                        }

                        var selectedManager = WaitForManagerReady(
                            context,
                            refreshBase,
                            TimeSpan.FromSeconds(3),
                            CancellationToken.None);
                        var selectedCar = CarCatalog.ByAftermarketId(selectedManager.CurrentId);
                        if (selectedCar.AftermarketId != staging.First.AftermarketId &&
                            selectedCar.AftermarketId != target.AftermarketId)
                        {
                            refreshedManager = selectedManager;
                            actualRefreshCar = selectedCar;
                            break;
                        }

                        AppLogger.Warn(
                            $"Refresh attempt {attempt} selected unsuitable staging car " +
                            $"{selectedCar.AftermarketId}; retrying.");
                        if (attempt < maximumRefreshAttempts)
                        {
                            progress?.Report(new GameProgress(
                                $"完整刷新未产生可用中转车辆，正在重试（{attempt + 1}/{maximumRefreshAttempts}）"));
                            refreshBase = selectedManager;
                        }
                    }

                    if (refreshedManager is null || actualRefreshCar is null)
                    {
                        throw new GameToolException(
                            "游戏完整刷新连续返回了不适合作为中转的车辆，请重新执行恢复重购。",
                            $"No distinct refresh car after {maximumRefreshAttempts} attempts.");
                    }
                    AppLogger.Info(
                        $"Full refresh selected {actualRefreshCar.AftermarketId}/" +
                        $"{actualRefreshCar.CarModelId} ({actualRefreshCar.OriginalName}); " +
                        $"temporary pool requested {staging.Refresh.AftermarketId}.");
                    var refreshedSaveStates = ResolveSaveStateWithRetry(
                        context,
                        refreshedManager,
                        TimeSpan.FromSeconds(5),
                        progress,
                        "完整刷新后的展位交互",
                        CancellationToken.None);
                    RestoreEligibility(
                        context,
                        refreshedSaveStates.ByManager[refreshedManager.Address],
                        changedFlags,
                        CancellationToken.None);

                    progress?.Report(new GameProgress(
                        $"第三阶段：切回目标车辆 {target.ChineseName}"));
                    var finalManager = SetCar(
                        context,
                        refreshedManager,
                        setter.Address,
                        target,
                        CancellationToken.None);
                    finalManager = RereadManagers(context, [finalManager]).Single();
                    var finalSaveStates = ResolveSaveStateWithRetry(
                        context,
                        finalManager,
                        TimeSpan.FromSeconds(5),
                        progress,
                        "目标车辆的购买交互",
                        CancellationToken.None);
                    var diagnostic = ReadPurchaseDiagnostic(
                        context,
                        finalManager,
                        finalSaveStates.ByManager[finalManager.Address],
                        finalSaveStates.DiscoveryMethod,
                        CancellationToken.None);
                    if (!diagnostic.IsEligible)
                    {
                        throw new InvalidOperationException(
                            "The final game eligibility predicate remained false.");
                    }

                    watch.Stop();
                    AppLogger.Info(
                        $"Rebuy transaction completed: slot={target.Slot}; target={target.AftermarketId}; " +
                        $"stage1={staging.First.AftermarketId}; refresh={actualRefreshCar.AftermarketId}; " +
                        $"elapsed={watch.Elapsed}.");
                    return new RebuyResult(
                        target.Slot,
                        target,
                        staging.First,
                        actualRefreshCar,
                        diagnostic,
                        watch.Elapsed);
                }
                catch (Exception exception)
                {
                    var rollbackNotes = new List<string>();
                    var remoteCallStateUnknown = exception is RemoteCallStateUnknownException;
                    if (context is not null && RebuySafetyPolicy.CanRollback(
                            refreshStarted,
                            refreshFinished,
                            remoteCallStateUnknown))
                    {
                        TryRollbackPool(poolOverride, rollbackNotes);
                        var rollbackStillSafe = true;
                        if (initialManager is not null && setter is not null)
                        {
                            rollbackStillSafe = TryRollbackCar(
                                context,
                                initialManager,
                                setter.Address,
                                rollbackNotes);
                        }
                        if (rollbackStillSafe)
                        {
                            TryRollbackEligibility(context, changedFlags, rollbackNotes);
                        }
                        else
                        {
                            rollbackNotes.Add(
                                "回滚 setter 的线程状态未知，已停止后续资格回滚；退出游戏即可清除临时状态。");
                        }
                    }
                    else if (context is not null)
                    {
                        rollbackNotes.Add(
                            "游戏远程线程状态未知，为避免与仍在执行的代码竞争，没有执行不安全回滚；退出游戏即可清除临时状态。");
                    }

                    var rollbackText = rollbackNotes.Count == 0
                        ? "未产生可回滚的写入。"
                        : string.Join(" ", rollbackNotes);
                    AppLogger.Error($"Rebuy transaction failed. Rollback: {rollbackText}", exception);
                    var normalized = Normalize(exception, "重购链路未完成");
                    throw new GameToolException(
                        normalized is GameToolException gameError
                            ? $"{gameError.UserMessage} {rollbackText}"
                            : $"重购链路未完成。{rollbackText}",
                        normalized.ToString(),
                        normalized);
                }
                finally
                {
                    context?.Dispose();
                }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private ManagerScanResult ResolveManagers(
        ProcessContext context,
        IProgress<GameProgress>? progress,
        CancellationToken cancellationToken)
    {
        var definitions = SlotDefinition.All;
        if (_managerCache is not null &&
            _managerCache.ProcessId == context.Process.Id &&
            _managerCache.ProcessStartTicks == context.ProcessStartTicks &&
            _managerCache.ModuleBase == context.ModuleBase)
        {
            var cached = new List<ManagerCandidate>();
            foreach (var definition in definitions)
            {
                var entry = _managerCache.Entries[definition.Slot];
                var candidate = ManagerCandidate.TryRead(
                    context.Handle,
                    entry.Address,
                    context.ModuleBase,
                    context.ModuleSize,
                    definition,
                    entry.Vtable,
                    entry.SecondVtable);
                if (candidate is null)
                {
                    cached.Clear();
                    break;
                }
                cached.Add(candidate);
            }

            if (cached.Count == definitions.Length)
            {
                progress?.Report(new GameProgress("已复核本次游戏进程的展位缓存"));
                return new ManagerScanResult(cached, "进程内缓存（已复核）");
            }
            _managerCache = null;
        }

        progress?.Report(new GameProgress("正在定位三个活动展位"));
        var result = ManagerScanner.Find(
            context.Handle,
            context.ModuleBase,
            context.ModuleSize,
            definitions,
            progress,
            cancellationToken);
        _managerCache = new ManagerCache(
            context.Process.Id,
            context.ProcessStartTicks,
            context.ModuleBase,
            result.Candidates.ToDictionary(
                candidate => candidate.Definition.Slot,
                candidate => new ManagerCacheEntry(
                    candidate.Address,
                    candidate.Vtable,
                    candidate.SecondVtable)));
        return result;
    }

    private static IReadOnlyList<ManagerCandidate> RereadManagers(
        ProcessContext context,
        IReadOnlyList<ManagerCandidate> managers)
    {
        return managers.Select(manager =>
            ManagerCandidate.TryRead(
                context.Handle,
                manager.Address,
                context.ModuleBase,
                context.ModuleSize,
                manager.Definition,
                manager.Vtable,
                manager.SecondVtable) ?? throw new GameToolException(
                    "展位状态在操作期间发生了意外变化，已停止后续写入。",
                    $"Manager revalidation failed for slot {manager.Definition.Slot} at " +
                    $"0x{manager.Address:X16}.")).ToArray();
    }

    private static ManagerCandidate SetCar(
        ProcessContext context,
        ManagerCandidate manager,
        ulong setterAddress,
        CarOption target,
        CancellationToken cancellationToken)
    {
        var live = RereadManagers(context, [manager]).Single();
        if (live.PendingId != -1)
        {
            throw new GameToolException(
                $"展位 {target.Slot} 正在由游戏刷新，请等待几秒后重试。",
                $"Pending ID was {live.PendingId}.");
        }
        if (!live.Definition.PoolIds.Contains(target.AftermarketId) ||
            !live.Definition.ModelByAftermarketId.TryGetValue(target.AftermarketId, out var modelId) ||
            modelId != target.CarModelId)
        {
            throw new GameToolException("车辆目录与当前展位池不一致，已停止写入。");
        }
        if (live.CurrentId == target.AftermarketId && live.CarModelId == target.CarModelId)
        {
            return live;
        }

        RemoteGameCall.InvokeSetter(
            context.Handle,
            setterAddress,
            live.Address,
            target.AftermarketId,
            cancellationToken,
            notifyBeforeChange: true,
            rebuildDependentState: true);
        return WaitForCar(context, live, target, TimeSpan.FromSeconds(12), cancellationToken);
    }

    private static ManagerCandidate WaitForCar(
        ProcessContext context,
        ManagerCandidate manager,
        CarOption target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentId = MemoryAccess.Read<int>(
                context.Handle, manager.Address + GameLayout.CurrentIdOffset);
            var modelId = MemoryAccess.Read<int>(
                context.Handle, manager.Address + GameLayout.CarModelIdOffset);
            if (currentId == target.AftermarketId && modelId == target.CarModelId)
            {
                return ManagerCandidate.TryRead(
                    context.Handle,
                    manager.Address,
                    context.ModuleBase,
                    context.ModuleSize,
                    manager.Definition,
                    manager.Vtable,
                    manager.SecondVtable) ?? throw new GameToolException(
                        "车辆已切换，但展位复核失败，已停止后续操作。");
            }
            Thread.Sleep(25);
        }

        throw new TimeoutException(
            $"Slot {target.Slot} did not commit {target.AftermarketId}/{target.CarModelId} " +
            $"within {timeout.TotalSeconds:0} seconds.");
    }

    private static ManagerCandidate WaitForManagerReady(
        ProcessContext context,
        ManagerCandidate manager,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = ManagerCandidate.TryRead(
                context.Handle,
                manager.Address,
                context.ModuleBase,
                context.ModuleSize,
                manager.Definition,
                manager.Vtable,
                manager.SecondVtable);
            if (candidate is not null && candidate.PendingId == -1)
            {
                return candidate;
            }
            Thread.Sleep(25);
        }

        throw new TimeoutException(
            $"Slot {manager.Definition.Slot} did not settle after a full refresh " +
            $"within {timeout.TotalSeconds:0} seconds.");
    }

    private static SaveStateResolution ResolveSaveState(
        ProcessContext context,
        ManagerCandidate manager,
        CancellationToken cancellationToken) =>
        AftermarketSaveStateResolver.Resolve(
            context.Handle,
            context.ModuleBase,
            context.ModuleSize,
            [manager],
            cancellationToken);

    private static SaveStateResolution ResolveSaveStateWithRetry(
        ProcessContext context,
        ManagerCandidate manager,
        TimeSpan timeout,
        IProgress<GameProgress>? progress,
        string phase,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        var watch = Stopwatch.StartNew();
        GameToolException? lastError = null;
        var attempts = 0;
        var lastReportedSecond = -1;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            try
            {
                return ResolveSaveState(context, manager, cancellationToken);
            }
            catch (GameToolException exception)
            {
                lastError = exception;
                var elapsedSecond = checked((int)watch.Elapsed.TotalSeconds);
                if (elapsedSecond != lastReportedSecond)
                {
                    lastReportedSecond = elapsedSecond;
                    progress?.Report(new GameProgress(
                        $"{phase}尚未加载；请切回游戏并停留在活动场地" +
                        $"（已等待 {elapsedSecond} 秒）"));
                }
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        throw new GameToolException(
            $"{phase}仍未加载。请切回游戏并停留在活动场地，等待车辆模型出现后重试。",
            $"{phase} SaveState remained unavailable after {attempts} attempts over " +
            $"{watch.Elapsed.TotalSeconds:0.0} seconds (timeout {timeout.TotalSeconds:0.0}). " +
            $"Last error: {lastError?.Message}",
            lastError);
    }

    private static EligibilityBinding RestoreEligibility(
        ProcessContext context,
        SaveStateCandidate saveState,
        ICollection<EligibilityMutation> mutations,
        CancellationToken cancellationToken)
    {
        var binding = ResolveEligibilityBinding(context, saveState);
        if (binding.OriginalFlag == 0)
        {
            if (!mutations.Any(mutation => mutation.Address == binding.FlagAddress))
            {
                mutations.Add(new EligibilityMutation(binding.FlagAddress, binding.OriginalFlag));
            }
            MemoryAccess.Write(context.Handle, binding.FlagAddress, (byte)1);
        }

        if (!RemoteGameCall.InvokeBool(
                context.Handle,
                binding.EligibleFunction,
                saveState.Address,
                cancellationToken))
        {
            throw new GameToolException(
                "资格字节已写入，但游戏完整资格判定仍未通过，已停止后续操作。");
        }
        return binding;
    }

    private static PurchaseDiagnostic ReadPurchaseDiagnostic(
        ProcessContext context,
        ManagerCandidate manager,
        SaveStateCandidate saveState,
        string resolverDiscovery,
        CancellationToken cancellationToken)
    {
        var binding = ResolveEligibilityBinding(context, saveState);
        var valid = RemoteGameCall.InvokeBool(
            context.Handle, binding.ValidFunction, saveState.Address, cancellationToken);
        var invalid = RemoteGameCall.InvokeBool(
            context.Handle, binding.InvalidFunction, saveState.Address, cancellationToken);
        var eligible = RemoteGameCall.InvokeBool(
            context.Handle, binding.EligibleFunction, saveState.Address, cancellationToken);
        if (valid == invalid)
        {
            throw new GameToolException(
                "游戏返回了互相矛盾的展位状态，已停止操作。",
                $"valid={valid}; invalid={invalid}; saveState=0x{saveState.Address:X16}.");
        }

        return new PurchaseDiagnostic(
            manager.Definition.Slot,
            CarCatalog.ByAftermarketId(manager.CurrentId),
            valid,
            invalid,
            eligible,
            MemoryAccess.Read<byte>(context.Handle, binding.FlagAddress),
            saveState.Address,
            binding.EligibilityObjectAddress,
            resolverDiscovery);
    }

    private static EligibilityBinding ResolveEligibilityBinding(
        ProcessContext context,
        SaveStateCandidate saveState)
    {
        var saveStateVtable = MemoryAccess.Read<ulong>(context.Handle, saveState.Address);
        var validFunction = MemoryAccess.Read<ulong>(
            context.Handle, saveStateVtable + GameLayout.SaveStateValidVtableSlot);
        var invalidFunction = MemoryAccess.Read<ulong>(
            context.Handle, saveStateVtable + GameLayout.SaveStateInvalidVtableSlot);
        var eligibleFunction = MemoryAccess.Read<ulong>(
            context.Handle, saveStateVtable + GameLayout.SaveStateEligibleVtableSlot);
        ValidateModuleFunction(context, validFunction, "valid predicate");
        ValidateModuleFunction(context, invalidFunction, "invalid predicate");
        ValidateModuleFunction(context, eligibleFunction, "eligible predicate");

        var eligibilityObject = MemoryAccess.Read<ulong>(
            context.Handle, saveState.Address + GameLayout.PurchaseEligibilityObjectOffset);
        if (eligibilityObject < 0x10000 || eligibilityObject >= 0x0000800000000000)
        {
            throw new GameToolException(
                "没有找到有效的购买资格对象，当前展位可能尚未完全加载。",
                $"Eligibility object=0x{eligibilityObject:X16}.");
        }

        var predicateVtable = MemoryAccess.Read<ulong>(context.Handle, eligibilityObject);
        var getterFunction = MemoryAccess.Read<ulong>(
            context.Handle, predicateVtable + GameLayout.PredicateCheckVtableSlot);
        ValidateModuleFunction(context, getterFunction, "eligibility getter");
        var signature = MemoryAccess.ReadBytes(
            context.Handle, getterFunction, GameLayout.EligibilityGetterSignature.Length);
        if (!signature.AsSpan().SequenceEqual(GameLayout.EligibilityGetterSignature))
        {
            throw new GameToolException(
                "购买资格结构与已验证版本不一致，已停止写入。",
                $"Eligibility getter signature mismatch at 0x{getterFunction:X16}.");
        }

        var flagAddress = eligibilityObject + GameLayout.PurchaseEligibilityFlagOffset;
        var flag = MemoryAccess.Read<byte>(context.Handle, flagAddress);
        if (flag > 1)
        {
            throw new GameToolException(
                "购买资格值超出预期范围，已停止写入。",
                $"Eligibility flag={flag} at 0x{flagAddress:X16}.");
        }

        return new EligibilityBinding(
            eligibilityObject,
            flagAddress,
            flag,
            validFunction,
            invalidFunction,
            eligibleFunction);
    }

    private static void ValidateModuleFunction(
        ProcessContext context,
        ulong address,
        string name)
    {
        var moduleEnd = context.ModuleBase + checked((ulong)context.ModuleSize);
        if (address < context.ModuleBase || address >= moduleEnd)
        {
            throw new GameToolException(
                "运行时函数指针超出游戏模块范围，已停止操作。",
                $"{name}=0x{address:X16}; module=0x{context.ModuleBase:X16}-0x{moduleEnd:X16}.");
        }
    }

    private static void TryRollbackPool(
        TemporarySingleCarPool? poolOverride,
        ICollection<string> notes)
    {
        if (poolOverride is null || poolOverride.IsRestored)
        {
            return;
        }
        try
        {
            poolOverride.Restore();
            notes.Add("临时车池已恢复。");
        }
        catch (Exception rollbackError)
        {
            notes.Add($"临时车池回滚失败：{rollbackError.Message}");
        }
    }

    private static bool TryRollbackCar(
        ProcessContext context,
        ManagerCandidate original,
        ulong setterAddress,
        ICollection<string> notes)
    {
        try
        {
            var originalCar = CarCatalog.ByAftermarketId(original.CurrentId);
            var live = RereadManagers(context, [original]).Single();
            if (live.CurrentId != original.CurrentId || live.CarModelId != original.CarModelId)
            {
                SetCar(context, live, setterAddress, originalCar, CancellationToken.None);
                notes.Add("原展位车辆已恢复。");
            }
            return true;
        }
        catch (RemoteCallStateUnknownException rollbackError)
        {
            notes.Add($"原车辆回滚线程状态未知：{rollbackError.Message}");
            return false;
        }
        catch (Exception rollbackError)
        {
            notes.Add($"原车辆回滚失败：{rollbackError.Message}");
            return true;
        }
    }

    private static void TryRollbackEligibility(
        ProcessContext context,
        IEnumerable<EligibilityMutation> mutations,
        ICollection<string> notes)
    {
        var restored = 0;
        foreach (var mutation in mutations.Reverse())
        {
            try
            {
                if (MemoryAccess.TryRead<byte>(context.Handle, mutation.Address, out var value) && value == 1)
                {
                    MemoryAccess.Write(context.Handle, mutation.Address, mutation.OriginalValue);
                    restored++;
                }
            }
            catch (Exception rollbackError)
            {
                notes.Add($"资格回滚失败：{rollbackError.Message}");
            }
        }
        if (restored > 0)
        {
            notes.Add("购买资格标记已恢复到操作前状态。");
        }
    }

    private static GameSnapshot CreateSnapshot(
        ProcessContext context,
        IReadOnlyList<ManagerCandidate> managers,
        string managerDiscovery,
        SetterLocation? setter,
        RefreshLocation? refresh)
    {
        var slots = managers
            .OrderBy(manager => manager.Definition.Slot)
            .Select(manager => new SlotState(
                manager.Definition.Slot,
                manager.Address,
                manager.PoolStart,
                manager.CurrentId,
                manager.CarModelId,
                CarCatalog.ByAftermarketId(manager.CurrentId)))
            .ToArray();
        return new GameSnapshot(
            context.Process.Id,
            context.Version,
            Path.GetFileName(context.ExecutablePath),
            context.ModuleBase,
            context.ModuleSize,
            setter?.Address ?? 0,
            setter?.DiscoveryMethod ?? "未通过特征校验",
            refresh?.Address ?? 0,
            refresh?.DiscoveryMethod ?? "未通过特征校验",
            managerDiscovery,
            slots,
            DateTimeOffset.Now);
    }

    private static SetterLocation? TryLocateSetter(
        ProcessContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return SetterLocator.Find(
                context.Handle, context.ModuleBase, context.ModuleSize, cancellationToken);
        }
        catch (GameToolException exception)
        {
            AppLogger.Warn(exception.ToString());
            return null;
        }
    }

    private static RefreshLocation? TryLocateRefresh(
        ProcessContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return RefreshLocator.Find(
                context.Handle, context.ModuleBase, context.ModuleSize, cancellationToken);
        }
        catch (GameToolException exception)
        {
            AppLogger.Warn(exception.ToString());
            return null;
        }
    }

    private static void ValidateTargets(IReadOnlyList<CarOption> targets)
    {
        if (targets.Count == 0 || targets.Count > 3)
        {
            throw new ArgumentException("Select between one and three cars.", nameof(targets));
        }
        if (targets.Select(target => target.Slot).Distinct().Count() != targets.Count)
        {
            throw new ArgumentException("Only one car may be selected per slot.", nameof(targets));
        }
        foreach (var target in targets)
        {
            ValidateSlot(target.Slot);
            if (!CarCatalog.ForSlot(target.Slot).Contains(target))
            {
                throw new ArgumentException("The selected car is not in the verified catalog.", nameof(targets));
            }
        }
    }

    private static void ValidateSlot(int slot)
    {
        if (slot is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), "Slot must be 1, 2, or 3.");
        }
    }

    private static ProcessContext OpenContext(bool writable)
    {
        var processes = Process.GetProcessesByName(ProcessName);
        if (processes.Length == 0)
        {
            throw new GameToolException("未检测到 Forza Horizon 6，请先启动游戏并进入“意大利奇珍”场地。");
        }

        var selected = SelectProcess(processes);
        foreach (var process in processes)
        {
            if (!ReferenceEquals(process, selected))
            {
                process.Dispose();
            }
        }

        try
        {
            var module = selected.MainModule ?? throw new GameToolException("无法读取游戏主模块。");
            var moduleBase = checked((ulong)module.BaseAddress.ToInt64());
            var moduleSize = module.ModuleMemorySize;
            var path = module.FileName;
            var version = GetVersion(path);
            var processStartTicks = selected.StartTime.ToUniversalTime().Ticks;
            var handle = NativeMethods.OpenProcess(writable ? FullAccess : ReadAccess, false, selected.Id);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, $"OpenProcess failed for PID {selected.Id}.");
            }

            var distribution = path.Contains("steamapps", StringComparison.OrdinalIgnoreCase)
                ? "Steam"
                : path.Contains("XboxGames", StringComparison.OrdinalIgnoreCase)
                    ? "Xbox"
                    : "Unknown";
            AppLogger.Info(
                $"Game context opened: distribution={distribution}; writable={writable}; " +
                $"pid={selected.Id}; version={version}; executable={Path.GetFileName(path)}; " +
                $"module=0x{moduleBase:X16}; size=0x{moduleSize:X}.");
            return new ProcessContext(
                selected,
                handle,
                moduleBase,
                moduleSize,
                path,
                version,
                processStartTicks);
        }
        catch
        {
            selected.Dispose();
            throw;
        }
    }

    private static Process SelectProcess(IReadOnlyList<Process> processes) =>
        processes.OrderByDescending(process =>
        {
            try
            {
                return process.WorkingSet64;
            }
            catch
            {
                return 0;
            }
        }).First();

    private static string GetVersion(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return string.Empty;
        }
        var info = FileVersionInfo.GetVersionInfo(executablePath);
        return info.FileVersion ?? info.ProductVersion ?? string.Empty;
    }

    private static Exception Normalize(Exception exception, string operation)
    {
        if (exception is GameToolException or OperationCanceledException)
        {
            return exception;
        }
        if (exception is Win32Exception { NativeErrorCode: 5 })
        {
            return new GameToolException(
                "没有足够权限访问游戏进程，请以管理员身份重新运行工具。",
                exception.ToString(),
                exception);
        }

        AppLogger.Error(operation, exception);
        return new GameToolException(
            $"{operation}。工具已停止后续操作，请在诊断页打开日志查看详情。",
            exception.ToString(),
            exception);
    }

    public void Dispose()
    {
        _operationGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record ManagerCache(
        int ProcessId,
        long ProcessStartTicks,
        ulong ModuleBase,
        IReadOnlyDictionary<int, ManagerCacheEntry> Entries);

    private sealed record ManagerCacheEntry(ulong Address, ulong Vtable, ulong SecondVtable);

    private sealed record EligibilityBinding(
        ulong EligibilityObjectAddress,
        ulong FlagAddress,
        byte OriginalFlag,
        ulong ValidFunction,
        ulong InvalidFunction,
        ulong EligibleFunction);

    private sealed record EligibilityMutation(ulong Address, byte OriginalValue);

    private sealed class ProcessContext(
        Process process,
        SafeProcessHandle handle,
        ulong moduleBase,
        int moduleSize,
        string executablePath,
        string version,
        long processStartTicks) : IDisposable
    {
        public Process Process { get; } = process;
        public SafeProcessHandle Handle { get; } = handle;
        public ulong ModuleBase { get; } = moduleBase;
        public int ModuleSize { get; } = moduleSize;
        public string ExecutablePath { get; } = executablePath;
        public string Version { get; } = version;
        public long ProcessStartTicks { get; } = processStartTicks;

        public void Dispose()
        {
            Handle.Dispose();
            Process.Dispose();
        }
    }

    private sealed class TemporarySingleCarPool
    {
        private readonly SafeProcessHandle _process;
        private readonly ulong _managerAddress;
        private readonly ulong _poolStart;
        private readonly ulong _poolEndAddress;
        private readonly int _originalFirstId;
        private readonly ulong _originalEnd;
        private readonly int _temporaryId;
        private readonly ulong _temporaryEnd;

        public TemporarySingleCarPool(
            SafeProcessHandle process,
            ManagerCandidate manager,
            int temporaryId)
        {
            if (manager.PendingId != -1 || !manager.Definition.PoolIds.Contains(temporaryId))
            {
                throw new GameToolException("临时刷新车池未通过校验，已停止操作。");
            }

            _process = process;
            _managerAddress = manager.Address;
            _poolStart = manager.PoolStart;
            _poolEndAddress = manager.Address + GameLayout.PoolEndOffset;
            _originalFirstId = MemoryAccess.Read<int>(process, _poolStart);
            _originalEnd = MemoryAccess.Read<ulong>(process, _poolEndAddress);
            _temporaryId = temporaryId;
            _temporaryEnd = _poolStart + sizeof(int);
            if (_originalEnd != manager.PoolEnd)
            {
                throw new GameToolException("展位车池在操作前发生变化，已停止写入。");
            }

            MemoryAccess.Write(process, _poolStart, temporaryId);
            try
            {
                MemoryAccess.Write(process, _poolEndAddress, _temporaryEnd);
            }
            catch
            {
                MemoryAccess.Write(process, _poolStart, _originalFirstId);
                throw;
            }
        }

        public bool IsRestored { get; private set; }

        public string DescribeLiveState()
        {
            try
            {
                var currentId = MemoryAccess.Read<int>(
                    _process, _managerAddress + GameLayout.CurrentIdOffset);
                var pendingId = MemoryAccess.Read<int>(
                    _process, _managerAddress + GameLayout.PendingIdOffset);
                var firstId = MemoryAccess.Read<int>(_process, _poolStart);
                var poolEnd = MemoryAccess.Read<ulong>(_process, _poolEndAddress);
                var count = poolEnd >= _poolStart && (poolEnd - _poolStart) % sizeof(int) == 0
                    ? (poolEnd - _poolStart) / sizeof(int)
                    : ulong.MaxValue;
                return $"manager=0x{_managerAddress:X16}; current={currentId}; pending={pendingId}; " +
                       $"first={firstId}; end=0x{poolEnd:X16}; count=" +
                       (count == ulong.MaxValue ? "invalid" : count.ToString());
            }
            catch (Exception exception)
            {
                return $"live-state unavailable ({exception.GetType().Name}: {exception.Message})";
            }
        }

        public void Restore()
        {
            if (IsRestored)
            {
                return;
            }

            var currentFirst = MemoryAccess.Read<int>(_process, _poolStart);
            var currentEnd = MemoryAccess.Read<ulong>(_process, _poolEndAddress);
            if (currentFirst != _temporaryId && currentFirst != _originalFirstId)
            {
                throw new InvalidOperationException(
                    $"Temporary pool first ID changed unexpectedly to {currentFirst}.");
            }
            if (currentEnd != _temporaryEnd && currentEnd != _originalEnd)
            {
                throw new InvalidOperationException(
                    $"Temporary pool end changed unexpectedly to 0x{currentEnd:X16}.");
            }

            if (currentFirst != _originalFirstId)
            {
                MemoryAccess.Write(_process, _poolStart, _originalFirstId);
            }
            if (currentEnd != _originalEnd)
            {
                MemoryAccess.Write(_process, _poolEndAddress, _originalEnd);
            }
            IsRestored = true;
        }
    }
}
