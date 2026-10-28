using System.Text.Json.Serialization;
using DigiPrice.Api.Data;
using DigiPrice.Core.Pricing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<PricingEngine>();

var connectionString = builder.Configuration.GetConnectionString("DigiPrice")
    ?? "Data Source=App_Data/digiprice.db";
var sqlite = new SqliteConnectionStringBuilder(connectionString);
if (!string.IsNullOrWhiteSpace(sqlite.DataSource) &&
    !string.Equals(sqlite.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) &&
    !sqlite.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
{
    var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(sqlite.DataSource));
    if (!string.IsNullOrWhiteSpace(databaseDirectory))
        Directory.CreateDirectory(databaseDirectory);
}

builder.Services.AddDbContext<DigiPriceDbContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

// The initial release uses EnsureCreated; a versioned migration workflow will be introduced before schema evolution.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DigiPriceDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Products.AnyAsync())
    {
        var now = DateTimeOffset.UtcNow;
        db.Products.AddRange(
            Seed("DP-1001", "هدفون بی‌سیم نمونه", 1_250_000, 820_000, 1_050_000, 1_490_000, 1_230_000, PricingStrategy.UndercutCompetitor, now),
            Seed("DP-1002", "ماوس بی‌سیم نمونه", 680_000, 410_000, 590_000, 790_000, 650_000, PricingStrategy.Defensive, now),
            Seed("DP-1003", "پایه نگهدارنده نمونه", 390_000, 220_000, 330_000, 450_000, null, PricingStrategy.FixedPrice, now));
        await db.SaveChangesAsync();
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (IConfiguration configuration) => Results.Ok(new
{
    status = "ok",
    product = "DigiPrice",
    environment = app.Environment.EnvironmentName,
    mode = "simulation-only",
    storage = "sqlite",
    liveDigikalaConnectionConfigured = false,
    livePriceWritesEnabled = false,
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/products", async (DigiPriceDbContext db) =>
{
    var products = await db.Products.AsNoTracking().OrderBy(p => p.SellerSku).ToListAsync();
    return Results.Ok(products.Select(p => p.ToModel()).ToArray());
});

app.MapPost("/api/products", async (CreateProductRequest request, DigiPriceDbContext db) =>
{
    var error = ValidateProduct(request);
    if (error is not null)
        return Results.BadRequest(new { error });

    var sku = request.SellerSku.Trim();
    if (await db.Products.AnyAsync(p => p.SellerSku == sku))
        return Results.Conflict(new { error = "شناسه کالا در این فهرست تکراری است." });

    var product = new ProductEntity
    {
        Id = Guid.NewGuid(),
        SellerSku = sku,
        Title = request.Title.Trim(),
        CurrentPriceToman = request.CurrentPriceToman,
        CostPriceToman = request.CostPriceToman,
        MinPriceToman = request.MinPriceToman,
        MaxPriceToman = request.MaxPriceToman,
        CompetitorPriceToman = request.CompetitorPriceToman,
        Strategy = request.Strategy,
        Mode = PricingMode.ManualApproval,
        Enabled = false,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    db.Products.Add(product);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException)
    {
        return Results.Conflict(new { error = "ثبت کالا انجام نشد؛ شناسه کالا ممکن است تکراری باشد." });
    }

    return Results.Created($"/api/products/{product.Id}", product.ToModel());
});

app.MapPut("/api/products/{id:guid}/strategy", async (
    Guid id, UpdateProductStrategyRequest request, DigiPriceDbContext db) =>
{
    if (!Enum.IsDefined(request.Strategy))
        return Results.BadRequest(new { error = "استراتژی قیمت‌گذاری معتبر نیست." });
    if (!Enum.IsDefined(request.Mode))
        return Results.BadRequest(new { error = "حالت اجرا معتبر نیست." });

    var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id);
    if (product is null)
        return Results.NotFound(new { error = "کالا پیدا نشد." });

    product.Strategy = request.Strategy;
    // Until a separately reviewed live adapter exists, store every product in manual-approval mode.
    product.Mode = PricingMode.ManualApproval;
    product.Enabled = request.Enabled;
    product.UpdatedAtUtc = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        product = product.ToModel(),
        note = "تنظیمات ذخیره شد؛ در این نسخه تمام پیشنهادها نیازمند تأیید انسانی هستند و قیمت واقعی تغییر نمی‌کند."
    });
});

app.MapPost("/api/pricing/preview", (PricingPreviewRequest request, PricingEngine engine) =>
{
    try
    {
        return Results.Ok(engine.Calculate(request));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/integration/status", () => Results.Ok(new
{
    provider = "Digikala Seller API",
    connected = false,
    readAccessVerified = false,
    priceWriteAccessVerified = false,
    liveWritesEnabled = false,
    message = "برای فعال‌سازی اتصال، مسیر مجاز و دسترسی‌های حساب فروشنده باید جداگانه تأیید شوند."
}));

app.MapFallbackToFile("index.html");
app.Run();

static ProductEntity Seed(
    string sku, string title, long current, long cost, long min, long max, long? competitor,
    PricingStrategy strategy, DateTimeOffset now) => new()
{
    Id = Guid.NewGuid(),
    SellerSku = sku,
    Title = title,
    CurrentPriceToman = current,
    CostPriceToman = cost,
    MinPriceToman = min,
    MaxPriceToman = max,
    CompetitorPriceToman = competitor,
    Strategy = strategy,
    Mode = PricingMode.ManualApproval,
    Enabled = true,
    UpdatedAtUtc = now
};

static string? ValidateProduct(CreateProductRequest request)
{
    if (string.IsNullOrWhiteSpace(request.SellerSku) || request.SellerSku.Trim().Length > 100)
        return "شناسه کالا الزامی است و حداکثر ۱۰۰ نویسه می‌تواند داشته باشد.";
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250)
        return "عنوان کالا الزامی است و حداکثر ۲۵۰ نویسه می‌تواند داشته باشد.";
    if (request.CurrentPriceToman <= 0 || request.MinPriceToman <= 0)
        return "قیمت فعلی و کف قیمت باید بزرگ‌تر از صفر باشند.";
    if (request.CostPriceToman < 0)
        return "بهای تمام‌شده نمی‌تواند منفی باشد.";
    if (request.MaxPriceToman < request.MinPriceToman)
        return "سقف قیمت باید مساوی یا بزرگ‌تر از کف قیمت باشد.";
    if (request.CurrentPriceToman < request.MinPriceToman || request.CurrentPriceToman > request.MaxPriceToman)
        return "قیمت فعلی باید در محدوده کف و سقف مجاز قرار داشته باشد.";
    if (request.CompetitorPriceToman is <= 0)
        return "قیمت رقیب باید بزرگ‌تر از صفر باشد؛ برای نبود قیمت رقیب از مقدار خالی استفاده کنید.";
    if (!Enum.IsDefined(request.Strategy))
        return "استراتژی قیمت‌گذاری معتبر نیست.";
    return null;
}

public partial class Program { }
