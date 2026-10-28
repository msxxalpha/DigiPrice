using System.Text.Json.Serialization;
using DigiPrice.Core;
using DigiPrice.Core.Pricing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<PricingEngine>();
builder.Services.AddSingleton<InMemoryCatalog>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    product = "DigiPrice",
    environment = app.Environment.EnvironmentName,
    mode = "simulation-only",
    liveDigikalaConnectionConfigured = false,
    livePriceWritesEnabled = false,
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/products", (InMemoryCatalog catalog) => Results.Ok(catalog.GetAll()));

app.MapPost("/api/products", (CreateProductRequest request, InMemoryCatalog catalog) =>
{
    try
    {
        var product = catalog.Add(request);
        return Results.Created($"/api/products/{product.Id}", product);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/products/{id:guid}/strategy", (Guid id, UpdateProductStrategyRequest request, InMemoryCatalog catalog) =>
{
    try
    {
        var updated = catalog.UpdateStrategy(id, request);
        return updated is null
            ? Results.NotFound(new { error = "کالا پیدا نشد." })
            : Results.Ok(new
            {
                product = updated,
                note = "این نسخه فقط تنظیم را ذخیره می‌کند؛ اجرای خودکار یا تغییر قیمت واقعی فعال نیست."
            });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
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

public partial class Program { }
