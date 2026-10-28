using DigiPrice.Core.Pricing;
using Xunit;

namespace DigiPrice.Core.Tests;

public sealed class PricingEngineTests
{
    private readonly PricingEngine _engine = new();

    [Fact]
    public void UndercutStrategy_RespectsFloorPrice()
    {
        var result = _engine.Calculate(new PricingPreviewRequest(
            CurrentPriceToman: 1_000_000,
            CostPriceToman: 700_000,
            CompetitorPriceToman: 700_000,
            MinPriceToman: 780_000,
            MaxPriceToman: 1_200_000,
            UndercutAmountToman: 1_000,
            MaxChangePercent: 100m,
            Strategy: PricingStrategy.UndercutCompetitor));

        Assert.Equal(780_000, result.SuggestedPriceToman);
        Assert.Contains(result.Warnings, warning => warning.Contains("کف"));
        Assert.True(result.RequiresApproval);
    }

    [Fact]
    public void UndercutStrategy_DoesNotRaisePriceToChaseCompetitor()
    {
        var result = _engine.Calculate(new PricingPreviewRequest(
            900_000, 500_000, 1_000_000, 400_000, 1_200_000, 1_000, 5m,
            PricingStrategy.UndercutCompetitor));

        Assert.Equal(900_000, result.SuggestedPriceToman);
    }

    [Fact]
    public void ChangeLimit_RestrictsSuggestedMovement()
    {
        var result = _engine.Calculate(new PricingPreviewRequest(
            1_000_000, 500_000, 500_000, 100_000, 1_500_000, 1_000, 5m,
            PricingStrategy.UndercutCompetitor));

        Assert.Equal(950_000, result.SuggestedPriceToman);
        Assert.Contains(result.Warnings, warning => warning.Contains("محدود"));
    }

    [Fact]
    public void InvalidFloorAndCeiling_Throws()
    {
        var request = new PricingPreviewRequest(
            500_000, 300_000, null, 600_000, 400_000, 1_000, 5m,
            PricingStrategy.FixedPrice);

        Assert.Throws<ArgumentException>(() => _engine.Calculate(request));
    }

    [Fact]
    public void MissingCompetitor_ProducesWarningAndKeepsCurrentForUndercut()
    {
        var result = _engine.Calculate(new PricingPreviewRequest(
            600_000, 300_000, null, 400_000, 800_000, 1_000, 5m,
            PricingStrategy.UndercutCompetitor));

        Assert.Equal(600_000, result.SuggestedPriceToman);
        Assert.Contains(result.Warnings, warning => warning.Contains("رقیب"));
    }
}
