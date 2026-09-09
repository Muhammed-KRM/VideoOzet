using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class VideoTranscriptConfiguration : IEntityTypeConfiguration<VideoTranscript>
{
    public void Configure(EntityTypeBuilder<VideoTranscript> builder)
    {
        builder.ToTable("video_transcripts");
        builder.HasKey(vt => vt.Id);
        
        builder.Property(vt => vt.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(vt => vt.VideoId).HasColumnName("video_id").IsRequired();
        builder.Property(vt => vt.HamMetin).HasColumnName("ham_metin").HasColumnType("text").IsRequired();
        builder.Property(vt => vt.ZamanDamgalari).HasColumnName("zaman_damgalari").HasColumnType("jsonb");
        builder.Property(vt => vt.Dil).HasColumnName("dil").HasMaxLength(10).HasDefaultValue("tr").IsRequired();
        builder.Property(vt => vt.KelimeSayisi).HasColumnName("kelime_sayisi").HasDefaultValue(0).IsRequired();
        builder.Property(vt => vt.SttModel).HasColumnName("stt_model").HasMaxLength(50).IsRequired();
        builder.Property(vt => vt.SttSuresiMs).HasColumnName("stt_suresi_ms").HasDefaultValue(0).IsRequired();
        builder.Property(vt => vt.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(vt => vt.VideoId).IsUnique().HasDatabaseName("idx_video_transcripts_video_id");

        builder.HasOne(vt => vt.Video)
               .WithOne(v => v.Transcript)
               .HasForeignKey<VideoTranscript>(vt => vt.VideoId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_transcripts_video");
    }
}
