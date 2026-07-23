using FH6ItalianRarities.Models;

namespace FH6ItalianRarities.Core;

internal sealed record RebuyStagingPlan(CarOption First, CarOption Refresh);

internal static class RebuyPlanFactory
{
    public static RebuyStagingPlan Create(CarOption target, int currentId)
    {
        var cars = CarCatalog.ForSlot(target.Slot);
        var preferredFirstId = target.Slot switch
        {
            1 => 159,
            2 => 158,
            3 => 161,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        var preferredRefreshId = target.Slot switch
        {
            1 => 152,
            2 => 187,
            3 => 172,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
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
