using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class VideoChunkDocumentConfiguration : IEntityTypeConfiguration<VideoChunkDocument>
{
    public void Configure(EntityTypeBuilder<VideoChunkDocument> builder)
    {
        builder.ToTable("video_chunk_documents");
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.VideoId).HasColumnName("video_id").IsRequired(false);
        builder.Property(x => x.DokumanId).HasColumnName("dokuman_id").IsRequired(false);
        builder.Property(x => x.EgitimId).HasColumnName("egitim_id").IsRequired();
        builder.Property(x => x.StartTimeMs).HasColumnName("start_time_ms").IsRequired();
        builder.Property(x => x.EndTimeMs).HasColumnName("end_time_ms").IsRequired();
        builder.Property(x => x.Text).HasColumnName("text").HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.ChunkIndex).HasColumnName("chunk_index").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ChunkTipi).HasColumnName("chunk_tipi").HasConversion<int>().HasDefaultValue(VideoOzet.Data.Enums.ChunkTipi.HamMetin).IsRequired();
        builder.Property(x => x.KaynakBaslik).HasColumnName("kaynak_baslik").HasMaxLength(300).HasDefaultValue("").IsRequired();
        builder.Property(x => x.BolumBasligi).HasColumnName("bolum_basligi").HasMaxLength(300);
        builder.Property(x => x.KaynakDil).HasColumnName("kaynak_dil").HasMaxLength(16).HasDefaultValue("tr").IsRequired();
        builder.Property(x => x.KullanimTuru).HasColumnName("kullanim_turu").HasConversion<int>().HasDefaultValue(VideoOzet.Data.Enums.KaynakKullanimTuru.DogrudanReferans).IsRequired();
        
        // 1536 boyutlu embedding s�tunu (OpenAI text-embedding-3-small)
        builder.Property(x => x.Embedding)
               .HasColumnName("embedding")
               .HasColumnType("vector(1024)");

        // HNSW index ile cosine similarity aramas�
        builder.HasIndex(x => x.Embedding)
               .HasMethod("hnsw")
               .HasOperators("vector_cosine_ops");
               
        builder.HasIndex(x => x.EgitimId);
        builder.HasIndex(x => new { x.EgitimId, x.ChunkTipi });
    }
}
