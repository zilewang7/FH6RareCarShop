using FH6ItalianRarities.Models;

namespace FH6ItalianRarities.Core;

public sealed record GameProcessStatus(
    bool IsRunning,
    int? ProcessId = null,
    string Version = "",
    string ExecutablePath = "");

public sealed record GameProgress(string Message, long BytesScanned = 0);

public sealed record SlotState(
    int Slot,
    ulong ManagerAddress,
    ulong PoolAddress,
    int CurrentAftermarketId,
    int CurrentCarModelId,
    CarOption CurrentCar);

public sealed record GameSnapshot(
    int ProcessId,
    string Version,
    string ExecutablePath,
    ulong ModuleBase,
    int ModuleSize,
    ulong SetterAddress,
    string SetterDiscovery,
    ulong RefreshAddress,
    string RefreshDiscovery,
    string ManagerDiscovery,
    IReadOnlyList<SlotState> Slots,
    DateTimeOffset CapturedAt);

public sealed record SlotApplyResult(
    int Slot,
    CarOption Target,
    bool WasAlreadySelected);

public sealed record ApplyResult(
    GameSnapshot Snapshot,
    IReadOnlyList<SlotApplyResult> Slots);

public sealed record PurchaseDiagnostic(
    int Slot,
    CarOption CurrentCar,
    bool IsValid,
    bool IsInvalid,
    bool IsEligible,
    byte EligibilityFlag,
    ulong SaveStateAddress,
    ulong EligibilityObjectAddress,
    string ResolverDiscovery);

public sealed record RebuyResult(
    int Slot,
    CarOption Target,
    CarOption FirstStagingCar,
    CarOption RefreshStagingCar,
    PurchaseDiagnostic FinalDiagnostic,
    TimeSpan Elapsed);

public sealed class GameToolException : Exception
{
    public GameToolException()
        : this("游戏工具操作失败。")
    {
    }

    public GameToolException(string userMessage)
        : base(userMessage)
    {
        UserMessage = userMessage;
    }

    public GameToolException(string userMessage, Exception innerException)
        : base(userMessage, innerException)
    {
        UserMessage = userMessage;
    }

    public GameToolException(
        string userMessage,
        string? technicalMessage,
        Exception? inner = null)
        : base(technicalMessage ?? userMessage, inner)
    {
        UserMessage = userMessage;
    }

    public string UserMessage { get; }
}
