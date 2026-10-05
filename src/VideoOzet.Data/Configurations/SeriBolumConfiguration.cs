using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class SeriBolumConfiguration : IEntityTypeConfiguration<SeriBolum>
{
    public void Configure(EntityTypeBuilder<SeriBolum> builder)
    {
        builder.ToTable("SeriBolumler");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.SeriPlaniId, x.BolumNo }).IsUnique();
        
        builder.HasOne(x => x.SeriPlani)
            .WithMany(x => x.SeriBolumler)
            .HasForeignKey(x => x.SeriPlaniId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasMany(x => x.Revizyonlar)
            .WithOne(x => x.SeriBolum)
            .HasForeignKey(x => x.SeriBolumId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
