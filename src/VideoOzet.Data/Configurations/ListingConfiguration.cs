using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.HasKey(l => l.Id);
        
        builder.Property(l => l.Title).IsRequired().HasMaxLength(150);
        builder.HasIndex(l => l.Slug).IsUnique();
        builder.Property(l => l.Slug).IsRequired().HasMaxLength(150);
        
        builder.Property(l => l.Description).HasMaxLength(3000);

        // İlanın tek bir sahibi var
        builder.HasOne(l => l.Owner)
            .WithMany(u => u.Listings)
            .HasForeignKey(l => l.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Branch ilişkisi
        builder.HasOne(l => l.Branch)
            .WithMany(b => b.Listings)
            .HasForeignKey(l => l.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // District ilişkisi
        builder.HasOne(l => l.District)
            .WithMany(d => d.Listings)
            .HasForeignKey(l => l.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
