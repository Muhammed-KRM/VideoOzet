using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class GeneratedContentConfiguration : IEntityTypeConfiguration<GeneratedContent>
{
    public void Configure(EntityTypeBuilder<GeneratedContent> builder)
    {
        builder.ToTable("generated_contents");
        builder.HasKey(gc => gc.Id);
        
        builder.Property(gc => gc.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(gc => gc.ContentRequestId).HasColumnName("content_request_id").IsRequired();
        builder.Property(gc => gc.ArastirmaOzeti).HasColumnName("arastirma_ozeti").HasColumnType("text").IsRequired();
        builder.Property(gc => gc.VideoPlani).HasColumnName("video_plani").HasColumnType("text").IsRequired();
        builder.Property(gc => gc.KullanilanKaynaklar).HasColumnName("kullanilan_kaynaklar").HasColumnType("jsonb").HasDefaultValue("[]").IsRequired();
        builder.Property(gc => gc.LlmModel).HasColumnName("llm_model").HasMaxLength(50).IsRequired();
        builder.Property(gc => gc.TokenKullanimi).HasColumnName("token_kullanimi").HasDefaultValue(0).IsRequired();
        builder.Property(gc => gc.UretimSuresiMs).HasColumnName("uretim_suresi_ms").HasDefaultValue(0).IsRequired();
        builder.Property(gc => gc.OlusturmaTarihi).HasColumnName("olusturma_tarihi").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(gc => gc.ContentRequestId).IsUnique().HasDatabaseName("idx_generated_contents_request_id");

        builder.HasOne(gc => gc.ContentRequest)
               .WithOne(cr => cr.GeneratedContent)
               .HasForeignKey<GeneratedContent>(gc => gc.ContentRequestId)
               .OnDelete(DeleteBehavior.Cascade)
               .HasConstraintName("fk_generated_contents_request");
    }
}
