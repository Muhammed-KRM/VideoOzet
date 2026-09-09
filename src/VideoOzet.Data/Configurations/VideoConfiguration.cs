using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class VideoConfiguration : IEntityTypeConfiguration<Video>
{
    public void Configure(EntityTypeBuilder<Video> builder)
    {
        builder.ToTable("videolar");
        builder.HasKey(v => v.Id);
        
        builder.Property(v => v.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(v => v.EgitimId).HasColumnName("egitim_id").IsRequired();
        builder.Property(v => v.Baslik).HasColumnName("baslik").HasMaxLength(300).IsRequired();
        builder.Property(v => v.DosyaYolu).HasColumnName("dosya_yolu").HasMaxLength(500).IsRequired();
        builder.Property(v => v.SesYolu).HasColumnName("ses_yolu").HasMaxLength(500);
        builder.Property(v => v.Sure).HasColumnName("sure").HasColumnType("interval");
        builder.Property(v => v.Sira).HasColumnName("sira").HasDefaultValue(0).IsRequired();
        builder.Property(v => v.DosyaBoyutu).HasColumnName("dosya_boyutu").HasDefaultValue(0).IsRequired();
        builder.Property(v => v.IslemDurumu).HasColumnName("islem_durumu").HasDefaultValue(VideoOzet.Data.Enums.VideoIslemDurumu.Bekliyor).IsRequired();
        builder.Property(v => v.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();
        builder.Property(v => v.IslemTamamlanmaTarihi).HasColumnName("islem_tamamlanma_tarihi").HasColumnType("timestamptz");

        builder.HasIndex(v => v.EgitimId).HasDatabaseName("idx_videolar_egitim_id");
        builder.HasIndex(v => v.IslemDurumu).HasDatabaseName("idx_videolar_durum");
        builder.HasIndex(v => new { v.EgitimId, v.Sira }).HasDatabaseName("idx_videolar_egitim_sira");

        builder.HasOne(v => v.Egitim)
               .WithMany(e => e.Videolar)
               .HasForeignKey(v => v.EgitimId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_videolar_egitim");
    }
}


