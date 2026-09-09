using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class ContentRequestConfiguration : IEntityTypeConfiguration<ContentRequest>
{
    public void Configure(EntityTypeBuilder<ContentRequest> builder)
    {
        builder.ToTable("content_requests");
        builder.HasKey(cr => cr.Id);
        
        builder.Property(cr => cr.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(cr => cr.EgitimId).HasColumnName("egitim_id").IsRequired();
        builder.Property(cr => cr.Konu).HasColumnName("konu").HasMaxLength(500).IsRequired();
        builder.Property(cr => cr.HedefUzunluk).HasColumnName("hedef_uzunluk").HasMaxLength(50);
        builder.Property(cr => cr.HedefKitle).HasColumnName("hedef_kitle").HasMaxLength(50);
        builder.Property(cr => cr.Durum).HasColumnName("durum").HasDefaultValue(VideoOzet.Data.Enums.ContentRequestDurumu.Bekliyor).IsRequired();
        builder.Property(cr => cr.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();
        builder.Property(cr => cr.TamamlanmaTarihi).HasColumnName("tamamlanma_tarihi").HasColumnType("timestamptz");

        builder.HasIndex(cr => cr.EgitimId).HasDatabaseName("idx_content_requests_egitim_id");
        builder.HasIndex(cr => cr.Durum).HasDatabaseName("idx_content_requests_durum");

        builder.HasOne(cr => cr.Egitim)
               .WithMany(e => e.ContentRequests)
               .HasForeignKey(cr => cr.EgitimId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_content_requests_egitim");
    }
}


