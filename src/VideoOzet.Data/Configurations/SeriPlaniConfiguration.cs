using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class SeriPlaniConfiguration : IEntityTypeConfiguration<SeriPlani>
{
    public void Configure(EntityTypeBuilder<SeriPlani> builder)
    {
        builder.ToTable("SeriPlanlari");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ContentRequestId, x.PlanNo }).IsUnique();

        builder.HasOne(x => x.ContentRequest)
            .WithMany(x => x.SeriPlanlari)
            .HasForeignKey(x => x.ContentRequestId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(x => x.SeriBolumler)
            .WithOne(x => x.SeriPlani)
            .HasForeignKey(x => x.SeriPlaniId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
