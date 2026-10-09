namespace FH6ItalianRarities.Core;

internal static class GamePackage
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint MaximumFullNameLength = 128;

    // Xbox app builds carry no executable version resource; the package identity does.
    public static string? TryReadVersion(int processId)
    {
        using var process = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            false,
            processId);
        if (process.IsInvalid)
        {
            return null;
        }

        var length = 0u;
        if (NativeMethods.GetPackageFullName(process, ref length, null) != ErrorInsufficientBuffer ||
            length is 0 or > MaximumFullNameLength)
        {
            return null;
        }

        var buffer = new char[length];
        if (NativeMethods.GetPackageFullName(process, ref length, buffer) != ErrorSuccess ||
            length is 0 || length > buffer.Length)
        {
            return null;
        }
        return ParseVersion(new string(buffer, 0, checked((int)length - 1)));
    }

    // Package full names use Name_Version_Architecture_ResourceId_PublisherId.
    internal static string? ParseVersion(string packageFullName)
    {
        var parts = packageFullName.Split('_');
        return parts.Length == 5 &&
               Version.TryParse(parts[1], out var version) &&
               version.Revision >= 0
            ? version.ToString()
            : null;
    }
}
