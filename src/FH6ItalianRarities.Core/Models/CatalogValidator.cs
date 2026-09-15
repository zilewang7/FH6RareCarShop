namespace FH6ItalianRarities.Models;

public static class CatalogValidator
{
    private static readonly HashSet<int> LimitedIds = [157, 160, 156, 161, 159, 158, 199, 200];
    private static readonly HashSet<int> RecommendedIds = [157, 160];

    public static void Validate()
    {
        var cars = CarCatalog.All;
        Require(cars.Count == 64, $"Expected 64 cars, found {cars.Count}.");
        Require(cars.Select(car => car.AftermarketId).Distinct().Count() == cars.Count,
            "Aftermarket IDs must be unique.");
        Require(cars.Select(car => car.CarModelId).Distinct().Count() == cars.Count,
            "Car model IDs must be unique.");

        foreach (var activity in CarCatalog.Activities)
        {
            var activityCars = CarCatalog.ForActivity(activity.Id);
            Require(activityCars.Count > 0, $"Activity {activity.Id} has no cars.");
            Require(activityCars.All(car => car.Slot is >= 1 && car.Slot <= activity.SlotCount),
                $"Activity {activity.Id} contains a car outside its slot range.");
            foreach (var slot in Enumerable.Range(1, activity.SlotCount))
            {
                var slotCars = CarCatalog.ForSlot(activity.Id, slot);
                Require(slotCars.Count >= 3, $"Activity {activity.Id} slot {slot} needs at least three cars.");
                Require(slotCars.Select(car => car.PoolPosition)
                        .SequenceEqual(Enumerable.Range(1, slotCars.Count)),
                    $"Activity {activity.Id} slot {slot} pool positions are not contiguous.");
            }
        }

        Require(CarCatalog.ForActivity(CarCatalog.ItalianActivityId).Count == 42,
            "Italian Automotive must contain 42 cars.");
        Require(Enumerable.Range(1, 3).All(slot =>
                CarCatalog.ForSlot(CarCatalog.ItalianActivityId, slot).Count == 14),
            "Italian Automotive must contain three 14-car pools.");
        Require(CarCatalog.ForActivity(CarCatalog.BritishActivityId).Count == 22,
            "British Automotive must contain 22 cars.");
        Require(Enumerable.Range(1, 2).All(slot =>
                CarCatalog.ForSlot(CarCatalog.BritishActivityId, slot).Count == 11),
            "British Automotive must contain two 11-car pools.");

        Require(cars.Where(car => car.IsLimited).Select(car => car.AftermarketId).ToHashSet()
                .SetEquals(LimitedIds),
            "Limited-car markers do not match the verified activity catalogs.");
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
