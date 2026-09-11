using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class DokumanConfiguration : IEntityTypeConfiguration<Dokuman>
{
    public void Configure(EntityTypeBuilder<Dokuman> builder)
    {
        builder.ToTable("dokumanlar");
        builder.HasKey(d => d.Id);
        
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.EgitimId).HasColumnName("egitim_id").IsRequired();
        builder.Property(d => d.DosyaAdi).HasColumnName("dosya_adi").HasMaxLength(300).IsRequired();
        builder.Property(d => d.DosyaYolu).HasColumnName("dosya_yolu").HasMaxLength(500).IsRequired();
        builder.Property(d => d.Uzanti).HasColumnName("uzanti").HasMaxLength(20).IsRequired();
        builder.Property(d => d.DosyaBoyutu).HasColumnName("dosya_boyutu").HasDefaultValue(0).IsRequired();
        builder.Property(d => d.IslemDurumu).HasColumnName("islem_durumu").HasDefaultValue(VideoOzet.Data.Enums.VideoIslemDurumu.Bekliyor).IsRequired();
        builder.Property(d => d.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();
        builder.Property(d => d.IslemTamamlanmaTarihi).HasColumnName("islem_tamamlanma_tarihi").HasColumnType("timestamptz");

        builder.HasIndex(d => d.EgitimId).HasDatabaseName("idx_dokumanlar_egitim_id");
        builder.HasIndex(d => d.IslemDurumu).HasDatabaseName("idx_dokumanlar_durum");

        builder.HasOne(d => d.Egitim)
               .WithMany(e => e.Dokumanlar)
               .HasForeignKey(d => d.EgitimId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_dokumanlar_egitim");
    }
}
