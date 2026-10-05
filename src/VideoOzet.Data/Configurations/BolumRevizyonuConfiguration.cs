using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class BolumRevizyonuConfiguration : IEntityTypeConfiguration<BolumRevizyonu>
{
    public void Configure(EntityTypeBuilder<BolumRevizyonu> builder)
    {
        builder.ToTable("BolumRevizyonlari");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.SeriBolumId, x.RevizyonNo }).IsUnique();
        
        builder.HasOne(x => x.SeriBolum)
            .WithMany(x => x.Revizyonlar)
            .HasForeignKey(x => x.SeriBolumId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
