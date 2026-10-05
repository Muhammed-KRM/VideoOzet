using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class KaynakKonuCikarimiConfiguration : IEntityTypeConfiguration<KaynakKonuCikarimi>
{
    public void Configure(EntityTypeBuilder<KaynakKonuCikarimi> builder)
    {
        builder.ToTable("KaynakKonuCikarimlari");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.IcerikHash).HasMaxLength(64);

        builder.HasIndex(x => new { x.KaynakId, x.KaynakTuru }).IsUnique();
    }
}
