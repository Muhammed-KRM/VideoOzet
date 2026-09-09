using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class QcResultConfiguration : IEntityTypeConfiguration<QcResult>
{
    public void Configure(EntityTypeBuilder<QcResult> builder)
    {
        builder.ToTable("qc_results");
        builder.HasKey(qc => qc.Id);
        
        builder.Property(qc => qc.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(qc => qc.ContentRequestId).HasColumnName("content_request_id").IsRequired();
        builder.Property(qc => qc.ToplamIddiaSayisi).HasColumnName("toplam_iddia_sayisi").HasDefaultValue(0).IsRequired();
        builder.Property(qc => qc.DesteklenenSayisi).HasColumnName("desteklenen_sayisi").HasDefaultValue(0).IsRequired();
        builder.Property(qc => qc.BelirsizSayisi).HasColumnName("belirsiz_sayisi").HasDefaultValue(0).IsRequired();
        builder.Property(qc => qc.DesteklenmeyenSayisi).HasColumnName("desteklenmeyen_sayisi").HasDefaultValue(0).IsRequired();
        builder.Property(qc => qc.DetayliRapor).HasColumnName("detayli_rapor").HasColumnType("jsonb").HasDefaultValue("[]").IsRequired();
        builder.Property(qc => qc.GuvenSkorYuzde).HasColumnName("guven_skor_yuzde").HasColumnType("numeric(5,2)").HasDefaultValue(0).IsRequired();
        builder.Property(qc => qc.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(qc => qc.ContentRequestId).IsUnique().HasDatabaseName("idx_qc_results_request_id");

        builder.HasOne(qc => qc.ContentRequest)
               .WithOne(cr => cr.QcResult)
               .HasForeignKey<QcResult>(qc => qc.ContentRequestId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_qc_results_request");
    }
}
