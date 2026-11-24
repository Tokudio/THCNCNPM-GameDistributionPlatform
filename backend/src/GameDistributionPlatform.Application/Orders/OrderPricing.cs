namespace GameDistributionPlatform.Application.Orders;

public static class OrderPricing
{
    // Round the final charge once to the scale of the order snapshot (decimal(18,2)).
    public static decimal Total(decimal price, int discountPercent) =>
        decimal.Round(price * (100 - discountPercent) / 100m, 2, MidpointRounding.AwayFromZero);
}
