using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class VideoSummaryConfiguration : IEntityTypeConfiguration<VideoSummary>
{
    public void Configure(EntityTypeBuilder<VideoSummary> builder)
    {
        builder.ToTable("video_summaries");
        builder.HasKey(vs => vs.Id);
        
        builder.Property(vs => vs.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(vs => vs.VideoId).HasColumnName("video_id").IsRequired();
        builder.Property(vs => vs.OzetMetni).HasColumnName("ozet_metni").HasColumnType("text").IsRequired();
        builder.Property(vs => vs.KonuBasliklari).HasColumnName("konu_basliklari").HasColumnType("jsonb").HasDefaultValue("[]").IsRequired();
        builder.Property(vs => vs.KonuEtiketleri).HasColumnName("konu_etiketleri").HasColumnType("jsonb").HasDefaultValue("[]").IsRequired();
        builder.Property(vs => vs.LlmModel).HasColumnName("llm_model").HasMaxLength(50).IsRequired();
        builder.Property(vs => vs.TokenKullanimi).HasColumnName("token_kullanimi").HasDefaultValue(0).IsRequired();
        builder.Property(vs => vs.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(vs => vs.VideoId).IsUnique().HasDatabaseName("idx_video_summaries_video_id");

        builder.HasOne(vs => vs.Video)
               .WithOne(v => v.Summary)
               .HasForeignKey<VideoSummary>(vs => vs.VideoId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_summaries_video");
    }
}
