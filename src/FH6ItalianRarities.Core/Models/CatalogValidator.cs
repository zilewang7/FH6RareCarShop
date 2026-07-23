namespace FH6ItalianRarities.Models;

public static class CatalogValidator
{
    private static readonly HashSet<int> LimitedIds = [157, 160, 156, 161, 159, 158];
    private static readonly HashSet<int> RecommendedIds = [157, 160];

    public static void Validate()
    {
        var cars = CarCatalog.All;
        Require(cars.Count == 42, $"Expected 42 cars, found {cars.Count}.");
        Require(cars.Select(car => car.AftermarketId).Distinct().Count() == cars.Count,
            "Aftermarket IDs must be unique.");
        Require(cars.Select(car => car.CarModelId).Distinct().Count() == cars.Count,
            "Car model IDs must be unique.");

        foreach (var slot in Enumerable.Range(1, 3))
        {
            var slotCars = cars.Where(car => car.Slot == slot).ToArray();
            Require(slotCars.Length == 14, $"Slot {slot} must contain 14 cars.");
            Require(slotCars.Select(car => car.PoolPosition).Order().SequenceEqual(Enumerable.Range(1, 14)),
                $"Slot {slot} pool positions must be 1 through 14.");
        }

        Require(cars.Where(car => car.IsLimited).Select(car => car.AftermarketId).ToHashSet()
                .SetEquals(LimitedIds),
            "Limited-car markers do not match the verified six-car set.");
        Require(cars.Where(car => car.IsRecommended).Select(car => car.AftermarketId).ToHashSet()
                .SetEquals(RecommendedIds),
            "Recommended-car markers do not match 599XX Evolution and Sesto Elemento.");
        Require(cars.All(car => car.DiscountMultiplier is > 0 and <= 1),
            "Every discount multiplier must be in the range (0, 1].");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
