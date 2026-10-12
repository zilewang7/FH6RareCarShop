namespace FH6ItalianRarities.Core;

public sealed record AutoPurchaseOptions
{
    public TimeSpan EnterDuration { get; init; } = TimeSpan.FromSeconds(9);
    public TimeSpan EnterInterval { get; init; } = TimeSpan.FromMilliseconds(120);
    public TimeSpan StartDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PurchaseDelay { get; init; } = TimeSpan.Zero;
    public TimeSpan RestoreDelay { get; init; } = TimeSpan.Zero;
    public bool RepeatPurchase { get; init; } = true;
    public int MaxRounds { get; init; }
    public bool ShowOverlay { get; init; } = true;

    public void Validate()
    {
        Check(EnterDuration, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(120), "连按时长须为 0.1–120 秒。");
        Check(EnterInterval, TimeSpan.FromMilliseconds(60), TimeSpan.FromSeconds(2), "按键间隔须为 60–2000 毫秒。");
        Check(StartDelay, TimeSpan.Zero, TimeSpan.FromSeconds(60), "启动倒计时须为 0–60 秒。");
        Check(PurchaseDelay, TimeSpan.Zero, TimeSpan.FromSeconds(60), "购买后等待须为 0–60 秒。");
        Check(RestoreDelay, TimeSpan.Zero, TimeSpan.FromSeconds(60), "恢复后等待须为 0–60 秒。");
        if (MaxRounds < 0)
        {
            throw new ArgumentException("购买数量须为非负整数；0 表示无限循环。");
        }
    }

    private static void Check(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string message)
    {
        if (value < minimum || value > maximum) throw new ArgumentException(message);
    }
}

public enum AutoPurchaseStage
{
    Starting,
    Preparing,
    Purchasing,
    PurchaseWait,
    Restoring,
    RestoreWait,
    Completed
}

public sealed record AutoPurchaseProgress(
    AutoPurchaseStage Stage,
    int CompletedRounds,
    string Message);

public sealed record AutoPurchaseActions(
    Func<CancellationToken, Task> PrepareAsync,
    Action EnsureGameForeground,
    Action EnterDown,
    Action EnterUp,
    Func<CancellationToken, Task> RestorePurchaseAsync);
