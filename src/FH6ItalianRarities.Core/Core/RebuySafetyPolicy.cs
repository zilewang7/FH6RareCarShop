namespace FH6ItalianRarities.Core;

internal static class RebuySafetyPolicy
{
    public static bool CanRollback(
        bool refreshStarted,
        bool refreshFinished,
        bool remoteCallStateUnknown) =>
        !remoteCallStateUnknown && (!refreshStarted || refreshFinished);
}
