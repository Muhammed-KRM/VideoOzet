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
        builder.Property(x => x.VideoId).HasColumnName("video_id").IsRequired();
        builder.Property(x => x.EgitimId).HasColumnName("egitim_id").IsRequired();
        builder.Property(x => x.StartTimeMs).HasColumnName("start_time_ms").IsRequired();
        builder.Property(x => x.EndTimeMs).HasColumnName("end_time_ms").IsRequired();
        builder.Property(x => x.Text).HasColumnName("text").HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();
        
        // 1536 boyutlu embedding sütunu (OpenAI text-embedding-3-small)
        builder.Property(x => x.Embedding)
               .HasColumnName("embedding")
               .HasColumnType("vector(1536)");

        // HNSW index ile cosine similarity aramasý
        builder.HasIndex(x => x.Embedding)
               .HasMethod("hnsw")
               .HasOperators("vector_cosine_ops");
               
        builder.HasIndex(x => x.EgitimId);
    }
}
