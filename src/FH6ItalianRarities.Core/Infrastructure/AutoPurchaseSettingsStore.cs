using System.Text.Json;
using FH6ItalianRarities.Core;

namespace FH6ItalianRarities.Infrastructure;

public sealed class AutoPurchaseSettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FH6RareCarShop", "AutoPurchase.json");

    public AutoPurchaseOptions Load()
    {
        try
        {
            if (!File.Exists(path)) return new AutoPurchaseOptions();
            var options = JsonSerializer.Deserialize<AutoPurchaseOptions>(File.ReadAllText(path))
                ?? throw new JsonException("Empty automatic purchase settings.");
            options.Validate();
            return options;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            AppLogger.Warn("Automatic purchase settings could not be loaded; using defaults. " + exception.Message);
            return new AutoPurchaseOptions();
        }
    }

    public void Save(AutoPurchaseOptions options)
    {
        options.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath + ".tmp", JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(fullPath + ".tmp", fullPath, overwrite: true);
    }
}
