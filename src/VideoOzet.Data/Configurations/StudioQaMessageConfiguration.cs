using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class StudioQaMessageConfiguration : IEntityTypeConfiguration<StudioQaMessage>
{
    public void Configure(EntityTypeBuilder<StudioQaMessage> builder)
    {
        builder.ToTable("studio_qa_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.EgitimId)
            .HasColumnName("egitim_id")
            .IsRequired();

        builder.Property(x => x.ContentRequestId)
            .HasColumnName("content_request_id");

        builder.Property(x => x.BolumNo)
            .HasColumnName("bolum_no");

        builder.Property(x => x.KaynakId)
            .HasColumnName("kaynak_id");

        builder.Property(x => x.Kapsam)
            .HasColumnName("kapsam")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Rol)
            .HasColumnName("rol")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Mesaj)
            .HasColumnName("mesaj")
            .IsRequired();

        builder.Property(x => x.OneriJson)
            .HasColumnName("oneri_json");

        builder.Property(x => x.KullanilanBaglamOzeti)
            .HasColumnName("kullanilan_baglam_ozeti")
            .HasMaxLength(500);

        builder.Property(x => x.OlusturmaTarihi)
            .HasColumnName("olusturma_tarihi")
            .IsRequired();

        builder.HasIndex(x => new { x.ContentRequestId, x.BolumNo, x.OlusturmaTarihi })
            .HasDatabaseName("idx_studio_qa_request_bolum");

        builder.HasIndex(x => new { x.KaynakId, x.OlusturmaTarihi })
            .HasDatabaseName("idx_studio_qa_kaynak");

        builder.HasIndex(x => new { x.EgitimId, x.Kapsam, x.OlusturmaTarihi })
            .HasDatabaseName("idx_studio_qa_egitim_kapsam");
    }
}
