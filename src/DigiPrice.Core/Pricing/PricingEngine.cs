namespace DigiPrice.Core.Pricing;

/// <summary>
/// Pure pricing calculation. It never communicates with a marketplace or changes a live price.
/// All monetary values are integer tomans.
/// </summary>
public sealed class PricingEngine
{
    public PricingPreviewResult Calculate(PricingPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var warnings = new List<string>();
        if (request.CurrentPriceToman < request.MinPriceToman ||
            request.CurrentPriceToman > request.MaxPriceToman)
        {
            warnings.Add("قیمت فعلی خارج از کف یا سقف تعیین‌شده است.");
        }

        if (request.CompetitorPriceToman is null or <= 0)
        {
            warnings.Add("قیمت رقیب معتبر در دسترس نیست؛ نتیجه بر اساس قیمت فعلی و قواعد کالا محاسبه می‌شود.");
        }

        long rawTarget = request.Strategy switch
        {
            PricingStrategy.UndercutCompetitor => CalculateUndercut(request),
            PricingStrategy.MaximizePriceWithinLimits => request.MaxPriceToman,
            PricingStrategy.FixedPrice => request.CurrentPriceToman,
            PricingStrategy.Defensive => request.CurrentPriceToman,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Strategy), "استراتژی قیمت‌گذاری ناشناخته است.")
        };

        var boundedTarget = Math.Clamp(rawTarget, request.MinPriceToman, request.MaxPriceToman);
        if (boundedTarget != rawTarget)
        {
            warnings.Add("قیمت پیشنهادی اولیه به دلیل رعایت کف یا سقف مجاز محدود شد.");
        }

        var maxDelta = (long)Math.Floor(request.CurrentPriceToman * request.MaxChangePercent / 100m);
        var lowerLimit = Math.Max(1, request.CurrentPriceToman - maxDelta);
        var upperLimit = request.CurrentPriceToman > long.MaxValue - maxDelta
            ? long.MaxValue
            : request.CurrentPriceToman + maxDelta;
        var changeLimitedTarget = Math.Clamp(boundedTarget, lowerLimit, upperLimit);
        if (changeLimitedTarget != boundedTarget)
        {
            warnings.Add($"میزان تغییر به سقف {request.MaxChangePercent:0.##}% نسبت به قیمت فعلی محدود شد.");
        }

        var suggested = Math.Clamp(changeLimitedTarget, request.MinPriceToman, request.MaxPriceToman);
        if (suggested != changeLimitedTarget)
        {
            warnings.Add("برای رعایت کف یا سقف قیمت، محدودیت درصد تغییر قابل اعمال کامل نبود؛ این مورد نیازمند بررسی انسانی است.");
        }

        if (suggested < request.CostPriceToman)
        {
            warnings.Add("قیمت پیشنهادی از بهای تمام‌شده اعلام‌شده کمتر است؛ هزینه‌های جانبی نیز در این محاسبه منظور نشده‌اند.");
        }

        var margin = suggested <= 0
            ? 0m
            : decimal.Round((suggested - request.CostPriceToman) / suggested * 100m, 2, MidpointRounding.AwayFromZero);

        return new PricingPreviewResult(
            request.CurrentPriceToman,
            suggested,
            request.CompetitorPriceToman,
            request.MinPriceToman,
            request.MaxPriceToman,
            margin,
            true,
            request.Strategy.ToString(),
            Explain(request.Strategy, request.CompetitorPriceToman, suggested),
            warnings.Distinct().ToArray());
    }

    private static long CalculateUndercut(PricingPreviewRequest request)
    {
        if (request.CompetitorPriceToman is not > 0)
            return request.CurrentPriceToman;

        var competitorTarget = request.CompetitorPriceToman.Value <= request.UndercutAmountToman
            ? 1
            : request.CompetitorPriceToman.Value - request.UndercutAmountToman;

        // Do not raise the price as a side effect of an undercut strategy.
        return Math.Min(request.CurrentPriceToman, competitorTarget);
    }

    private static string Explain(PricingStrategy strategy, long? competitorPrice, long suggested) =>
        strategy switch
        {
            PricingStrategy.UndercutCompetitor when competitorPrice is > 0 =>
                $"قیمت پیشنهادی با هدف فاصله گرفتن از قیمت مرجع محاسبه شد: {suggested:N0} تومان؛ کف و سقف و محدودیت تغییر اعمال شده‌اند.",
            PricingStrategy.UndercutCompetitor =>
                "قیمت رقیب معتبر در دسترس نبود؛ قیمت فعلی مبنای محاسبه قرار گرفت.",
            PricingStrategy.MaximizePriceWithinLimits =>
                "قیمت در جهت سقف تعیین‌شده حرکت می‌کند؛ محدودیت تغییر هر نوبت و بازه مجاز اعمال شده‌اند.",
            PricingStrategy.FixedPrice => "استراتژی قیمت ثابت انتخاب شده است.",
            PricingStrategy.Defensive => "استراتژی محافظه‌کار قیمت فعلی را حفظ می‌کند و قیمت پایین‌تر رقیب را تعقیب نمی‌کند.",
            _ => "قیمت پیشنهادی محاسبه شد."
        };

    private static void Validate(PricingPreviewRequest request)
    {
        if (request.CurrentPriceToman <= 0)
            throw new ArgumentException("قیمت فعلی باید بزرگ‌تر از صفر باشد.");
        if (request.CostPriceToman < 0)
            throw new ArgumentException("بهای تمام‌شده نمی‌تواند منفی باشد.");
        if (request.MinPriceToman <= 0)
            throw new ArgumentException("کف قیمت باید بزرگ‌تر از صفر باشد.");
        if (request.MaxPriceToman < request.MinPriceToman)
            throw new ArgumentException("سقف قیمت باید مساوی یا بزرگ‌تر از کف قیمت باشد.");
        if (request.UndercutAmountToman < 0)
            throw new ArgumentException("مقدار فاصله از قیمت رقیب نمی‌تواند منفی باشد.");
        if (request.MaxChangePercent < 0m || request.MaxChangePercent > 100m)
            throw new ArgumentException("محدودیت تغییر باید بین صفر تا ۱۰۰ درصد باشد.");
        if (request.CompetitorPriceToman is < 0)
            throw new ArgumentException("قیمت رقیب نمی‌تواند منفی باشد.");
    }
}
