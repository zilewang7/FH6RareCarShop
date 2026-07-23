using System.Globalization;
using System.Text;

namespace FH6ItalianRarities.Models;

public sealed record CarOption(
    int Slot,
    int PoolPosition,
    int AftermarketId,
    int CarModelId,
    int Year,
    string ManufacturerZh,
    string ManufacturerEn,
    string ModelZh,
    string ModelEn,
    decimal DiscountMultiplier,
    bool IsLimited = false,
    bool IsRecommended = false,
    string? ReferencePrice = null,
    string? Description = null,
    string SearchAliases = "")
{
    public string ChineseName => $"{ManufacturerZh} {ModelZh}";

    public string OriginalName => $"{Year} {ManufacturerEn} {ModelEn}";

    public string IdText => $"库存 ID {AftermarketId}  ·  车型 ID {CarModelId}";

    public string DetailText => Description ?? $"活动价格系数 {DiscountMultiplier:0.00}";

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var normalizedQuery = Normalize(query);
        var searchable = Normalize(string.Join(' ',
            ChineseName,
            OriginalName,
            ManufacturerZh,
            ManufacturerEn,
            ModelZh,
            ModelEn,
            SearchAliases,
            AftermarketId.ToString(CultureInfo.InvariantCulture),
            CarModelId.ToString(CultureInfo.InvariantCulture),
            Year.ToString(CultureInfo.InvariantCulture)));
        return searchable.Contains(normalizedQuery, StringComparison.Ordinal);
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark &&
                !char.IsWhiteSpace(character) &&
                character is not '-' and not '_' and not '·' and not '\'' and not '’')
            {
                builder.Append(character);
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
