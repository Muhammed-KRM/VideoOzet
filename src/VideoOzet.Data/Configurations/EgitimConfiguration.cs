using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class EgitimConfiguration : IEntityTypeConfiguration<Egitim>
{
    public void Configure(EntityTypeBuilder<Egitim> builder)
    {
        builder.ToTable("egitimler");
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.Ad).HasColumnName("ad").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Aciklama).HasColumnName("aciklama").HasMaxLength(2000);
        builder.Property(e => e.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.GuncellemeTarihi).HasColumnName("guncelleme_tarihi").HasColumnType("timestamptz");
        builder.Property(e => e.ToplamVideoSayisi).HasColumnName("toplam_video_sayisi").HasDefaultValue(VideoOzet.Data.Enums.EgitimDurumu.Taslak).IsRequired();
        builder.Property(e => e.IslenmiVideoSayisi).HasColumnName("islenmi_video_sayisi").HasDefaultValue(VideoOzet.Data.Enums.EgitimDurumu.Taslak).IsRequired();
        builder.Property(e => e.Durum).HasColumnName("durum").HasDefaultValue(VideoOzet.Data.Enums.EgitimDurumu.Taslak).IsRequired();

        builder.HasIndex(e => e.Durum).HasDatabaseName("idx_egitimler_durum");
        builder.HasIndex(e => e.OlusturmaTarihi).HasDatabaseName("idx_egitimler_olusturma").IsDescending();
    }
}


