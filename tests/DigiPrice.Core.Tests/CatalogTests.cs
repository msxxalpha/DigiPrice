using DigiPrice.Core;
using DigiPrice.Core.Pricing;
using Xunit;

namespace DigiPrice.Core.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void Add_RejectsDuplicateSkuIgnoringCaseAndWhitespace()
    {
        var catalog = new InMemoryCatalog();
        var request = new CreateProductRequest(
            " dp-1001 ", "کالای تکراری", 100_000, 50_000, 80_000, 120_000, null,
            PricingStrategy.FixedPrice);

        var error = Assert.Throws<ArgumentException>(() => catalog.Add(request));

        Assert.Contains("تکراری", error.Message);
        Assert.Equal(3, catalog.GetAll().Count);
    }

    [Fact]
    public void Add_RejectsCurrentPriceOutsideConfiguredBounds()
    {
        var catalog = new InMemoryCatalog();
        var request = new CreateProductRequest(
            "NEW-1", "کالای آزمایشی", 70_000, 50_000, 80_000, 120_000, null,
            PricingStrategy.FixedPrice);

        Assert.Throws<ArgumentException>(() => catalog.Add(request));
    }

    [Fact]
    public void Add_RejectsUndefinedPricingStrategy()
    {
        var catalog = new InMemoryCatalog();
        var request = new CreateProductRequest(
            "NEW-2", "کالای آزمایشی", 100_000, 50_000, 80_000, 120_000, null,
            (PricingStrategy)999);

        Assert.Throws<ArgumentException>(() => catalog.Add(request));
    }

    [Fact]
    public void UpdateStrategy_RejectsUndefinedExecutionMode()
    {
        var catalog = new InMemoryCatalog();
        var product = catalog.GetAll().First();

        Assert.Throws<ArgumentException>(() => catalog.UpdateStrategy(
            product.Id,
            new UpdateProductStrategyRequest(product.Strategy, (PricingMode)999, true)));
    }

    [Fact]
    public void NewProductStartsDisabledAndRequiresManualApproval()
    {
        var catalog = new InMemoryCatalog();
        var product = catalog.Add(new CreateProductRequest(
            "NEW-3", "کالای آزمایشی", 100_000, 50_000, 80_000, 120_000, null,
            PricingStrategy.FixedPrice));

        Assert.False(product.Enabled);
        Assert.Equal(PricingMode.ManualApproval, product.Mode);
    }
}
