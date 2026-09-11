using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class DokumanMetinConfiguration : IEntityTypeConfiguration<DokumanMetin>
{
    public void Configure(EntityTypeBuilder<DokumanMetin> builder)
    {
        builder.ToTable("dokuman_metinleri");
        builder.HasKey(d => d.Id);
        
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.DokumanId).HasColumnName("dokuman_id").IsRequired();
        builder.Property(d => d.HamMetin).HasColumnName("ham_metin").IsRequired();
        builder.Property(d => d.KelimeSayisi).HasColumnName("kelime_sayisi").HasDefaultValue(0).IsRequired();
        builder.Property(d => d.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(d => d.DokumanId).IsUnique().HasDatabaseName("idx_dokuman_metinleri_dokuman_id");

        builder.HasOne(d => d.Dokuman)
               .WithOne(d => d.DokumanMetin)
               .HasForeignKey<DokumanMetin>(d => d.DokumanId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_dokuman_metinleri_dokuman");
    }
}
