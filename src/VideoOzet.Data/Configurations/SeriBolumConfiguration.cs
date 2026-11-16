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
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Kanca)
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(x => x.SahneIskeletiJson)
            .HasDefaultValue("[]")
            .IsRequired();

        builder.Property(x => x.AtananKaynaklarJson)
            .HasDefaultValue("[]")
            .IsRequired();

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
