using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class ContentVersionConfiguration : IEntityTypeConfiguration<ContentVersion>
{
    public void Configure(EntityTypeBuilder<ContentVersion> builder)
    {
        builder.ToTable("content_versions");
        builder.HasKey(cv => cv.Id);

        builder.Property(cv => cv.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(cv => cv.ContentRequestId).HasColumnName("content_request_id").IsRequired();
        builder.Property(cv => cv.VersiyonNo).HasColumnName("versiyon_no").IsRequired();
        builder.Property(cv => cv.ArastirmaOzeti).HasColumnName("arastirma_ozeti").HasColumnType("text").IsRequired();
        builder.Property(cv => cv.VideoPlani).HasColumnName("video_plani").HasColumnType("text").IsRequired();
        builder.Property(cv => cv.RevizeTalimati).HasColumnName("revize_talimati").HasColumnType("text");
        builder.Property(cv => cv.LlmModel).HasColumnName("llm_model").HasMaxLength(50).IsRequired();
        builder.Property(cv => cv.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();

        builder.Property(cv => cv.ToplamIddiaSayisi).HasColumnName("toplam_iddia_sayisi");
        builder.Property(cv => cv.DesteklenenSayisi).HasColumnName("desteklenen_sayisi");
        builder.Property(cv => cv.BelirsizSayisi).HasColumnName("belirsiz_sayisi");
        builder.Property(cv => cv.DesteklenmeyenSayisi).HasColumnName("desteklenmeyen_sayisi");
        builder.Property(cv => cv.DetayliRapor).HasColumnName("detayli_rapor").HasColumnType("text").HasDefaultValue("[]");
        builder.Property(cv => cv.GuvenSkorYuzde).HasColumnName("guven_skor_yuzde").HasPrecision(5, 2);
        builder.Property(cv => cv.QcTarihi).HasColumnName("qc_tarihi").HasColumnType("timestamptz");

        builder.HasIndex(cv => new { cv.ContentRequestId, cv.VersiyonNo })
               .HasDatabaseName("idx_content_versions_req_ver");

        builder.HasOne(cv => cv.ContentRequest)
               .WithMany(cr => cr.Versions)
               .HasForeignKey(cv => cv.ContentRequestId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_content_versions_request");
    }
}
