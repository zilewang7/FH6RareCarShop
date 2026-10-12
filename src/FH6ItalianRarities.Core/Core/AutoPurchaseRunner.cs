using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace FH6ItalianRarities.Core;

public sealed class AutoPurchaseRunner(AutoPurchaseActions actions)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan KeyHoldDuration = TimeSpan.FromMilliseconds(35);
    private int _running;

    public async Task<int> RunAsync(
        AutoPurchaseOptions options,
        IProgress<AutoPurchaseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InvalidOperationException("自动购买脚本已经在运行。");
        }

        var completed = 0;
        try
        {
            Report(AutoPurchaseStage.Starting, "启动倒计时，请切回游戏并站在目标车辆购买位置");
            await WaitAsync(options.StartDelay, false, cancellationToken,
                seconds => Report(AutoPurchaseStage.Starting, $"{seconds} 秒后启动，请切回游戏"));
            cancellationToken.ThrowIfCancellationRequested();
            actions.EnsureGameForeground();
            Report(AutoPurchaseStage.Preparing, "正在校验活动、目标车辆与购买交互");
            using (var preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                await WaitForGameOperationAsync(
                    actions.PrepareAsync(preparationCancellation.Token), cancellationToken, preparationCancellation);
            }

            var limit = options.RepeatPurchase ? options.MaxRounds : 1;
            while (limit == 0 || completed < limit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                actions.EnsureGameForeground();
                Report(AutoPurchaseStage.Purchasing,
                    $"第 {completed + 1} 轮：连续按 Enter {options.EnterDuration.TotalSeconds:0.##} 秒");
                await PressEnterAsync(options, cancellationToken);
                Report(AutoPurchaseStage.PurchaseWait, "等待购买动画与交互结束");
                await WaitAsync(options.PurchaseDelay, true, cancellationToken);
                completed++;
                if (limit > 0 && completed >= limit) break;

                cancellationToken.ThrowIfCancellationRequested();
                actions.EnsureGameForeground();
                Report(AutoPurchaseStage.Restoring, "正在自动恢复重购，完成后继续购买同一车辆");
                // Once started, let the existing transaction finish and clean up.
                // Stop/focus loss is honored before any subsequent input or write.
                await WaitForGameOperationAsync(
                    actions.RestorePurchaseAsync(CancellationToken.None), cancellationToken);
                Report(AutoPurchaseStage.RestoreWait, "重购已恢复，等待车辆与购买入口加载");
                await WaitAsync(options.RestoreDelay, true, cancellationToken);
            }

            Report(AutoPurchaseStage.Completed, $"自动购买已完成，共执行 {completed} 轮");
            return completed;
        }
        finally
        {
            try { actions.EnterUp(); }
            finally { Interlocked.Exchange(ref _running, 0); }
        }

        void Report(AutoPurchaseStage stage, string message) =>
            progress?.Report(new AutoPurchaseProgress(stage, completed, message));
    }

    private async Task PressEnterAsync(AutoPurchaseOptions options, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < options.EnterDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            actions.EnsureGameForeground();
            var pressStarted = watch.Elapsed;
            try
            {
                actions.EnterDown();
                await WaitAsync(Min(KeyHoldDuration, options.EnterDuration - watch.Elapsed), true, cancellationToken);
            }
            finally
            {
                actions.EnterUp();
            }
            var untilNextPress = options.EnterInterval - (watch.Elapsed - pressStarted);
            await WaitAsync(Min(untilNextPress, options.EnterDuration - watch.Elapsed), true, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        actions.EnsureGameForeground();
    }

    private async Task WaitAsync(
        TimeSpan duration,
        bool checkForeground,
        CancellationToken cancellationToken,
        Action<int>? countdown = null)
    {
        var watch = Stopwatch.StartNew();
        var lastSeconds = -1;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (checkForeground) actions.EnsureGameForeground();
            var remaining = duration - watch.Elapsed;
            if (remaining <= TimeSpan.Zero) return;
            var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
            if (seconds != lastSeconds)
            {
                countdown?.Invoke(seconds);
                lastSeconds = seconds;
            }
            await Task.Delay(Min(PollInterval, remaining), cancellationToken).ConfigureAwait(false);
        } while (true);
    }

    private async Task WaitForGameOperationAsync(
        Task operation,
        CancellationToken cancellationToken,
        CancellationTokenSource? preparationCancellation = null)
    {
        Exception? focusError = null;
        while (!operation.IsCompleted)
        {
            if (focusError is null)
            {
                try { actions.EnsureGameForeground(); }
                catch (Exception exception)
                {
                    focusError = exception;
                    preparationCancellation?.Cancel();
                }
            }
            await Task.WhenAny(operation, Task.Delay(PollInterval)).ConfigureAwait(false);
        }
        try { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException) when (focusError is not null && !cancellationToken.IsCancellationRequested)
        {
            ExceptionDispatchInfo.Capture(focusError).Throw();
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (focusError is not null) ExceptionDispatchInfo.Capture(focusError).Throw();
        actions.EnsureGameForeground();
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;
}
