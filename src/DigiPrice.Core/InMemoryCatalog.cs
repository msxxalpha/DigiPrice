using System.Collections.Concurrent;
using DigiPrice.Core.Pricing;

namespace DigiPrice.Core;

public sealed class InMemoryCatalog
{
    private readonly ConcurrentDictionary<Guid, Product> _products = new();

    public InMemoryCatalog()
    {
        AddSeed("DP-1001", "هدفون بی‌سیم نمونه", 1_250_000, 820_000, 1_050_000, 1_490_000, 1_230_000, PricingStrategy.UndercutCompetitor);
        AddSeed("DP-1002", "ماوس بی‌سیم نمونه", 680_000, 410_000, 590_000, 790_000, 650_000, PricingStrategy.Defensive);
        AddSeed("DP-1003", "پایه نگهدارنده نمونه", 390_000, 220_000, 330_000, 450_000, null, PricingStrategy.FixedPrice);
    }

    public IReadOnlyCollection<Product> GetAll() =>
        _products.Values.OrderBy(p => p.SellerSku, StringComparer.OrdinalIgnoreCase).ToArray();

    public Product? Get(Guid id) => _products.TryGetValue(id, out var product) ? product : null;

    public Product Add(CreateProductRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var product = new Product(
            Guid.NewGuid(),
            request.SellerSku.Trim(),
            request.Title.Trim(),
            request.CurrentPriceToman,
            request.CostPriceToman,
            request.MinPriceToman,
            request.MaxPriceToman,
            request.CompetitorPriceToman,
            request.Strategy,
            PricingMode.ManualApproval,
            false,
            DateTimeOffset.UtcNow);
        _products[product.Id] = product;
        return product;
    }

    public Product? UpdateStrategy(Guid id, UpdateProductStrategyRequest request)
    {
        if (!_products.TryGetValue(id, out var product))
            return null;

        // LimitedAutoPilot is stored as an intended setting only. This MVP has no live write adapter.
        var updated = product with
        {
            Strategy = request.Strategy,
            Mode = request.Mode,
            Enabled = request.Enabled,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        _products[id] = updated;
        return updated;
    }

    private void AddSeed(
        string sku, string title, long current, long cost, long min, long max, long? competitor, PricingStrategy strategy)
    {
        var product = new Product(
            Guid.NewGuid(), sku, title, current, cost, min, max, competitor, strategy,
            PricingMode.ManualApproval, true, DateTimeOffset.UtcNow);
        _products[product.Id] = product;
    }

    private static void Validate(CreateProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SellerSku) || request.SellerSku.Length > 100)
            throw new ArgumentException("شناسه کالا الزامی است و حداکثر ۱۰۰ نویسه می‌تواند داشته باشد.");
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 250)
            throw new ArgumentException("عنوان کالا الزامی است و حداکثر ۲۵۰ نویسه می‌تواند داشته باشد.");
        if (request.CurrentPriceToman <= 0 || request.MinPriceToman <= 0)
            throw new ArgumentException("قیمت فعلی و کف قیمت باید بزرگ‌تر از صفر باشند.");
        if (request.CostPriceToman < 0)
            throw new ArgumentException("بهای تمام‌شده نمی‌تواند منفی باشد.");
        if (request.MaxPriceToman < request.MinPriceToman)
            throw new ArgumentException("سقف قیمت باید مساوی یا بزرگ‌تر از کف قیمت باشد.");
        if (request.CompetitorPriceToman is < 0)
            throw new ArgumentException("قیمت رقیب نمی‌تواند منفی باشد.");
    }
}
