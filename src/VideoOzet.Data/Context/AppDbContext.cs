using Microsoft.EntityFrameworkCore;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingImage> ListingImages => Set<ListingImage>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<TokenTransaction> TokenTransactions => Set<TokenTransaction>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<TokenPackage> TokenPackages => Set<TokenPackage>();
    public DbSet<VitrinPackage> VitrinPackages => Set<VitrinPackage>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<EndpointLog> EndpointLogs => Set<EndpointLog>();
    public DbSet<FunctionLog> FunctionLogs => Set<FunctionLog>();
    public DbSet<ViolationLog> ViolationLogs => Set<ViolationLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Entity framework konfigürasyon dosyalarını (IEntityTypeConfiguration) otomatik tarayıp ekle
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
