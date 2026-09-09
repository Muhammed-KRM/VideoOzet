using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class PipelineLogConfiguration : IEntityTypeConfiguration<PipelineLog>
{
    public void Configure(EntityTypeBuilder<PipelineLog> builder)
    {
        builder.ToTable("pipeline_logs");
        builder.HasKey(pl => pl.Id);
        
        builder.Property(pl => pl.Id).HasColumnName("id").UseIdentityColumn(); // bigserial
        builder.Property(pl => pl.VideoId).HasColumnName("video_id");
        builder.Property(pl => pl.EgitimId).HasColumnName("egitim_id");
        builder.Property(pl => pl.ContentRequestId).HasColumnName("content_request_id");
        builder.Property(pl => pl.Asama).HasColumnName("asama").IsRequired();
        builder.Property(pl => pl.Durum).HasColumnName("durum").HasMaxLength(20).IsRequired();
        builder.Property(pl => pl.BaslangicZamani).HasColumnName("baslangic_zamani").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();
        builder.Property(pl => pl.BitisZamani).HasColumnName("bitis_zamani").HasColumnType("timestamptz");
        builder.Property(pl => pl.SureMs).HasColumnName("sure_ms");
        builder.Property(pl => pl.HataMesaji).HasColumnName("hata_mesaji").HasColumnType("text");
        builder.Property(pl => pl.HataDetayi).HasColumnName("hata_detayi").HasColumnType("text");
        builder.Property(pl => pl.GirdiMetadata).HasColumnName("girdi_metadata").HasColumnType("jsonb");
        builder.Property(pl => pl.CiktiMetadata).HasColumnName("cikti_metadata").HasColumnType("jsonb");
        builder.Property(pl => pl.TraceId).HasColumnName("trace_id").HasMaxLength(50);
        builder.Property(pl => pl.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(pl => pl.VideoId).HasDatabaseName("idx_pipeline_logs_video_id");
        builder.HasIndex(pl => pl.EgitimId).HasDatabaseName("idx_pipeline_logs_egitim_id");
        builder.HasIndex(pl => pl.Asama).HasDatabaseName("idx_pipeline_logs_asama");
        builder.HasIndex(pl => pl.Durum).HasDatabaseName("idx_pipeline_logs_durum");
        builder.HasIndex(pl => pl.CreatedAt).HasDatabaseName("idx_pipeline_logs_created_at").IsDescending();
        builder.HasIndex(pl => pl.ContentRequestId).HasDatabaseName("idx_pipeline_logs_content_request");
    }
}
