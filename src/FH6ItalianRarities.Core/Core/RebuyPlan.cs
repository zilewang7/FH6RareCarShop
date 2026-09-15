using FH6ItalianRarities.Models;

namespace FH6ItalianRarities.Core;

internal sealed record RebuyStagingPlan(CarOption First, CarOption Refresh);

internal static class RebuyPlanFactory
{
    public static RebuyStagingPlan Create(CarOption target, int currentId)
    {
        var cars = CarCatalog.ForSlot(target.ActivityId, target.Slot);
        var preferredFirstId = (target.ActivityId, target.Slot) switch
        {
            (CarCatalog.ItalianActivityId, 1) => 159,
            (CarCatalog.ItalianActivityId, 2) => 158,
            (CarCatalog.ItalianActivityId, 3) => 161,
            (CarCatalog.BritishActivityId, 1) => 220,
            (CarCatalog.BritishActivityId, 2) => 218,
            _ => -1
        };
        var preferredRefreshId = (target.ActivityId, target.Slot) switch
        {
            (CarCatalog.ItalianActivityId, 1) => 152,
            (CarCatalog.ItalianActivityId, 2) => 187,
            (CarCatalog.ItalianActivityId, 3) => 172,
            (CarCatalog.BritishActivityId, 1) => 203,
            (CarCatalog.BritishActivityId, 2) => 201,
            _ => -1
        };

        var refresh = cars.FirstOrDefault(car =>
                          car.AftermarketId == preferredRefreshId &&
                          car.AftermarketId != target.AftermarketId &&
                          car.AftermarketId != currentId)
                      ?? cars.First(car =>
                          car.AftermarketId != target.AftermarketId &&
                          car.AftermarketId != currentId);
        var first = cars.FirstOrDefault(car =>
                        car.AftermarketId == preferredFirstId &&
                        car.AftermarketId != target.AftermarketId &&
                        car.AftermarketId != currentId &&
                        car.AftermarketId != refresh.AftermarketId)
                    ?? cars.First(car =>
                        car.AftermarketId != target.AftermarketId &&
                        car.AftermarketId != currentId &&
                        car.AftermarketId != refresh.AftermarketId);
        return new RebuyStagingPlan(first, refresh);
    }
}
