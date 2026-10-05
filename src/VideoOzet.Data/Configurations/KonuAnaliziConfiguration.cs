using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class KonuAnaliziConfiguration : IEntityTypeConfiguration<KonuAnalizi>
{
    public void Configure(EntityTypeBuilder<KonuAnalizi> builder)
    {
        builder.ToTable("KonuAnalizleri");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.ContentRequest)
            .WithOne(x => x.KonuAnalizi)
            .HasForeignKey<KonuAnalizi>(x => x.ContentRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
