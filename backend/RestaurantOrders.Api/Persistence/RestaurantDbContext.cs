using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Domain;

namespace RestaurantOrders.Api.Persistence;

public abstract class RestaurantDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<RestaurantTable> Tables => Set<RestaurantTable>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Subcategory> Subcategories => Set<Subcategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<RestaurantOrder> Orders => Set<RestaurantOrder>();
    public DbSet<OrderUnit> OrderUnits => Set<OrderUnit>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
        });
        model.Entity<RestaurantTable>(e => e.Property(x => x.Name).HasMaxLength(100));
        model.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(150);
        });
        model.Entity<Subcategory>(e =>
        {
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Name).HasMaxLength(150);
        });
        model.Entity<MenuItem>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Subcategory>().WithMany().HasForeignKey(x => x.SubcategoryId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.PriceBeforeVat).HasPrecision(18, 2);
            e.Property(x => x.VatRate).HasPrecision(5, 2);
            e.Property(x => x.Price).HasPrecision(18, 2);
        });
        model.Entity<RestaurantOrder>(e =>
        {
            e.HasIndex(x => x.ActiveTableId).IsUnique();
            e.HasOne<RestaurantTable>().WithMany().HasForeignKey(x => x.TableId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasMany(x => x.Units).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OrderUnit>(e =>
        {
            e.HasOne<MenuItem>().WithMany().HasForeignKey(x => x.MenuItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AddedByAccountId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ItemName).HasMaxLength(200);
            e.Property(x => x.CategoryName).HasMaxLength(150);
            e.Property(x => x.SubcategoryName).HasMaxLength(150);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
        });
        model.Entity<AuditEvent>(e =>
        {
            e.HasOne<RestaurantOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Action).HasMaxLength(100);
        });
    }
}

public sealed class SqliteRestaurantDbContext(DbContextOptions<SqliteRestaurantDbContext> options) : RestaurantDbContext(options);
public sealed class SqlServerRestaurantDbContext(DbContextOptions<SqlServerRestaurantDbContext> options) : RestaurantDbContext(options);
