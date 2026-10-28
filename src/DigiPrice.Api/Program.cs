using System.Text.Json.Serialization;
using System.Net;
using DigiPrice.Api.Data;
using DigiPrice.Core.Pricing;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<PricingEngine>();

var connectionString = builder.Configuration.GetConnectionString("DigiPrice")
    ?? "Server=localhost;Database=DigiPrice;Integrated Security=True;Encrypt=True;TrustServerCertificate=False";
builder.Services.AddDbContext<DigiPriceDbContext>(options => options.UseSqlServer(
    connectionString,
    sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

var app = builder.Build();

// The pilot is local-only by default. Do not enable remote access before adding authentication and network controls.
app.Use(async (context, next) =>
{
    var allowRemote = app.Configuration.GetValue<bool>("Pilot:AllowRemoteAccess");
    var remoteIp = context.Connection.RemoteIpAddress;
    if (!allowRemote && remoteIp is not null && !IPAddress.IsLoopback(remoteIp))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "دسترسی نسخه آزمایشی فقط از همین رایانه مجاز است." });
        return;
    }
    await next();
});

// Schema is provisioned explicitly by the versioned SQL script. Never auto-create or reset a seller database.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DigiPriceDbContext>();
    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException("اتصال به SQL Server برقرار نشد؛ ConnectionStrings__DigiPrice را بررسی کنید.");
    try
    {
        await db.Products.AsNoTracking().Select(p => p.Id).Take(1).ToListAsync();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("جدول‌های DigiPrice نصب نشده‌اند؛ اسکریپت database/001-initial-schema.sql را در دیتابیس DigiPrice اجرا کنید.", ex);
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
    storage = "sqlserver",
    liveDigikalaConnectionConfigured = false,
    livePriceWritesEnabled = false,
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/products", async (DigiPriceDbContext db) =>
{
    var products = await db.Products.AsNoTracking().OrderBy(p => p.SellerSku).ToListAsync();
    return Results.Ok(products.Select(p => p.ToModel()).ToArray());
});

app.MapPost("/api/products", async Task<IResult> (CreateProductRequest request, DigiPriceDbContext db) =>
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

app.MapPut("/api/products/{id:guid}/strategy", async Task<IResult> (
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

app.MapPost("/api/products/import", async Task<IResult> (HttpRequest request, DigiPriceDbContext db) =>
{
    if (request.ContentLength is > 5_000_000)
        return Results.BadRequest(new { error = "حجم فایل نباید بیشتر از ۵ مگابایت باشد." });

    using var reader = new StreamReader(request.Body);
    var csv = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(csv))
        return Results.BadRequest(new { error = "فایل CSV خالی است." });

    List<List<string>> rows;
    try { rows = ParseCsv(csv); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    if (rows.Count < 2)
        return Results.BadRequest(new { error = "فایل باید شامل سطر عنوان ستون‌ها و حداقل یک کالا باشد." });

    var headerPairs = rows[0].Select((value, index) => new { Key = value.Trim().TrimStart('\uFEFF'), Index = index }).ToArray();
    if (headerPairs.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        return Results.BadRequest(new { error = "نام ستون تکراری در سطر اول فایل وجود دارد." });
    var headers = headerPairs.ToDictionary(x => x.Key, x => x.Index, StringComparer.OrdinalIgnoreCase);
    var required = new[] { "sellerSku", "title", "currentPriceToman", "costPriceToman", "minPriceToman", "maxPriceToman", "competitorPriceToman", "strategy" };
    var missing = required.Where(h => !headers.ContainsKey(h)).ToArray();
    if (missing.Length > 0)
        return Results.BadRequest(new { error = "ستون‌های لازم در فایل وجود ندارند.", missingColumns = missing });

    string Value(IReadOnlyList<string> cells, string header) =>
        headers[header] < cells.Count ? cells[headers[header]].Trim() : "";

    var existing = await db.Products.ToListAsync();
    var bySku = existing.ToDictionary(x => x.SellerSku, StringComparer.OrdinalIgnoreCase);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var errors = new List<object>();
    var created = 0;
    var updated = 0;

    for (var i = 1; i < rows.Count; i++)
    {
        var cells = rows[i];
        if (cells.All(string.IsNullOrWhiteSpace)) continue;
        var sku = Value(cells, "sellerSku");
        if (string.IsNullOrWhiteSpace(sku) || !seen.Add(sku))
        {
            errors.Add(new { row = i + 1, sku, error = "شناسه کالا خالی یا در همین فایل تکراری است." });
            continue;
        }

        bool TryLong(string name, out long number) =>
            long.TryParse(Value(cells, name), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out number);

        if (!TryLong("currentPriceToman", out var current) ||
            !TryLong("costPriceToman", out var cost) ||
            !TryLong("minPriceToman", out var min) ||
            !TryLong("maxPriceToman", out var max))
        {
            errors.Add(new { row = i + 1, sku, error = "قیمت‌ها باید عدد صحیح باشند و جداکننده هزارگان نداشته باشند." });
            continue;
        }

        long? competitor = null;
        var competitorText = Value(cells, "competitorPriceToman");
        if (!string.IsNullOrWhiteSpace(competitorText))
        {
            if (!long.TryParse(competitorText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                errors.Add(new { row = i + 1, sku, error = "قیمت رقیب باید خالی یا عدد صحیح مثبت باشد." });
                continue;
            }
            competitor = parsed;
        }

        if (!Enum.TryParse<PricingStrategy>(Value(cells, "strategy"), true, out var strategy) ||
            !Enum.IsDefined(strategy))
        {
            errors.Add(new { row = i + 1, sku, error = "مقدار strategy معتبر نیست." });
            continue;
        }

        var title = Value(cells, "title");
        var requestModel = new CreateProductRequest(sku, title, current, cost, min, max, competitor, strategy);
        var validation = ValidateProduct(requestModel);
        if (validation is not null)
        {
            errors.Add(new { row = i + 1, sku, error = validation });
            continue;
        }

        if (bySku.TryGetValue(sku, out var product))
        {
            product.Title = title.Trim();
            product.CurrentPriceToman = current;
            product.CostPriceToman = cost;
            product.MinPriceToman = min;
            product.MaxPriceToman = max;
            product.CompetitorPriceToman = competitor;
            product.Strategy = strategy;
            product.UpdatedAtUtc = DateTimeOffset.UtcNow;
            updated++;
        }
        else
        {
            product = new ProductEntity
            {
                Id = Guid.NewGuid(), SellerSku = sku.Trim(), Title = title.Trim(),
                CurrentPriceToman = current, CostPriceToman = cost, MinPriceToman = min,
                MaxPriceToman = max, CompetitorPriceToman = competitor, Strategy = strategy,
                Mode = PricingMode.ManualApproval, Enabled = false, UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Products.Add(product);
            bySku[product.SellerSku] = product;
            created++;
        }
    }

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException)
    {
        return Results.Conflict(new { error = "ذخیره‌سازی انجام نشد؛ فایل را بررسی کنید و دوباره تلاش کنید." });
    }

    return Results.Ok(new { created, updated, rejected = errors.Count, errors, livePriceWritesEnabled = false });
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

static List<List<string>> ParseCsv(string input)
{
    var rows = new List<List<string>>();
    var row = new List<string>();
    var cell = new System.Text.StringBuilder();
    var quoted = false;

    for (var i = 0; i < input.Length; i++)
    {
        var ch = input[i];
        if (ch == '"')
        {
            if (quoted && i + 1 < input.Length && input[i + 1] == '"')
            {
                cell.Append('"');
                i++;
            }
            else quoted = !quoted;
        }
        else if (ch == ',' && !quoted)
        {
            row.Add(cell.ToString()); cell.Clear();
        }
        else if ((ch == '\r' || ch == '\n') && !quoted)
        {
            if (ch == '\r' && i + 1 < input.Length && input[i + 1] == '\n') i++;
            row.Add(cell.ToString()); cell.Clear();
            rows.Add(row); row = new List<string>();
        }
        else cell.Append(ch);
    }

    if (quoted) throw new ArgumentException("ساختار CSV نامعتبر است؛ کوتیشن بسته‌نشده وجود دارد.");
    if (cell.Length > 0 || row.Count > 0)
    {
        row.Add(cell.ToString());
        rows.Add(row);
    }
    return rows;
}

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
