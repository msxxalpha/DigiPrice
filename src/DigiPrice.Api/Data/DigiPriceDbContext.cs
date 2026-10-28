using DigiPrice.Core.Pricing;
using Microsoft.EntityFrameworkCore;

namespace DigiPrice.Api.Data;

public sealed class DigiPriceDbContext(DbContextOptions<DigiPriceDbContext> options) : DbContext(options)
{
    public DbSet<ProductEntity> Products => Set<ProductEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<ProductEntity>();
        product.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_CurrentPrice_Positive", "\"CurrentPriceToman\" > 0");
            table.HasCheckConstraint("CK_Products_PriceBounds", "\"MinPriceToman\" > 0 AND \"MaxPriceToman\" >= \"MinPriceToman\"");
            table.HasCheckConstraint("CK_Products_CurrentWithinBounds", "\"CurrentPriceToman\" >= \"MinPriceToman\" AND \"CurrentPriceToman\" <= \"MaxPriceToman\"");
            table.HasCheckConstraint("CK_Products_Cost_NonNegative", "\"CostPriceToman\" >= 0");
        });
        product.HasKey(x => x.Id);
        product.Property(x => x.SellerSku).HasMaxLength(100).IsRequired();
        product.Property(x => x.Title).HasMaxLength(250).IsRequired();
        product.HasIndex(x => x.SellerSku).IsUnique();
        product.Property(x => x.Strategy).HasConversion<string>().HasMaxLength(64);
        product.Property(x => x.Mode).HasConversion<string>().HasMaxLength(64);
    }
}

public sealed class ProductEntity
{
    public Guid Id { get; set; }
    public string SellerSku { get; set; } = "";
    public string Title { get; set; } = "";
    public long CurrentPriceToman { get; set; }
    public long CostPriceToman { get; set; }
    public long MinPriceToman { get; set; }
    public long MaxPriceToman { get; set; }
    public long? CompetitorPriceToman { get; set; }
    public PricingStrategy Strategy { get; set; }
    public PricingMode Mode { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Product ToModel() => new(
        Id, SellerSku, Title, CurrentPriceToman, CostPriceToman, MinPriceToman,
        MaxPriceToman, CompetitorPriceToman, Strategy, Mode, Enabled, UpdatedAtUtc);

    public static ProductEntity FromModel(Product product) => new()
    {
        Id = product.Id,
        SellerSku = product.SellerSku,
        Title = product.Title,
        CurrentPriceToman = product.CurrentPriceToman,
        CostPriceToman = product.CostPriceToman,
        MinPriceToman = product.MinPriceToman,
        MaxPriceToman = product.MaxPriceToman,
        CompetitorPriceToman = product.CompetitorPriceToman,
        Strategy = product.Strategy,
        Mode = product.Mode,
        Enabled = product.Enabled,
        UpdatedAtUtc = product.UpdatedAtUtc
    };
}
