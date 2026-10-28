namespace DigiPrice.Core.Pricing;

public enum PricingStrategy
{
    UndercutCompetitor,
    MaximizePriceWithinLimits,
    FixedPrice,
    Defensive
}

public enum PricingMode
{
    ManualApproval,
    LimitedAutoPilot
}

public sealed record Product(
    Guid Id,
    string SellerSku,
    string Title,
    long CurrentPriceToman,
    long CostPriceToman,
    long MinPriceToman,
    long MaxPriceToman,
    long? CompetitorPriceToman,
    PricingStrategy Strategy,
    PricingMode Mode,
    bool Enabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record PricingPreviewRequest(
    long CurrentPriceToman,
    long CostPriceToman,
    long? CompetitorPriceToman,
    long MinPriceToman,
    long MaxPriceToman,
    long UndercutAmountToman,
    decimal MaxChangePercent,
    PricingStrategy Strategy);

public sealed record PricingPreviewResult(
    long CurrentPriceToman,
    long SuggestedPriceToman,
    long? CompetitorPriceToman,
    long MinPriceToman,
    long MaxPriceToman,
    decimal EstimatedGrossMarginPercent,
    bool RequiresApproval,
    string Strategy,
    string Explanation,
    IReadOnlyList<string> Warnings);

public sealed record CreateProductRequest(
    string SellerSku,
    string Title,
    long CurrentPriceToman,
    long CostPriceToman,
    long MinPriceToman,
    long MaxPriceToman,
    long? CompetitorPriceToman,
    PricingStrategy Strategy);

public sealed record UpdateProductStrategyRequest(
    PricingStrategy Strategy,
    PricingMode Mode,
    bool Enabled);
