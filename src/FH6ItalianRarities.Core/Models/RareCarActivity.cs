namespace FH6ItalianRarities.Models;

public sealed record RareCarActivity(
    string Id,
    string DisplayName,
    string OriginalName,
    DateTimeOffset FirstAvailableFrom,
    DateTimeOffset FirstAvailableTo,
    string VenueName,
    int SlotCount)
{
    public string FirstAvailabilityText =>
        $"首次开放：{FirstAvailableFrom:yyyy-MM-dd HH:mm} - " +
        $"{FirstAvailableTo:yyyy-MM-dd HH:mm}（UTC+8）";

    public string SelectionText => $"{DisplayName}  ·  {FirstAvailabilityText}";
}
