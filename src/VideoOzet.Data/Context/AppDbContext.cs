using System.Reflection;
using Microsoft.EntityFrameworkCore;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }


    public virtual DbSet<Egitim> Egitimler { get; set; } = null!;
    public virtual DbSet<Video> Videolar { get; set; } = null!;
    public virtual DbSet<Dokuman> Dokumanlar { get; set; } = null!;
    public virtual DbSet<DokumanMetin> DokumanMetinleri { get; set; } = null!;
    public virtual DbSet<VideoTranscript> VideoTranscripts { get; set; } = null!;
    public virtual DbSet<VideoSummary> VideoSummaries { get; set; } = null!;
    public virtual DbSet<ContentRequest> ContentRequests { get; set; } = null!;
    public virtual DbSet<GeneratedContent> GeneratedContents { get; set; } = null!;
    public virtual DbSet<ContentVersion> ContentVersions { get; set; } = null!;
    public virtual DbSet<QcResult> QcResults { get; set; } = null!;
    public virtual DbSet<PipelineLog> PipelineLogs { get; set; } = null!;
    public virtual DbSet<VideoChunkDocument> VideoChunkDocuments { get; set; } = null!;
    
    public virtual DbSet<EndpointLog> EndpointLogs { get; set; } = null!;
    public virtual DbSet<FunctionLog> FunctionLogs { get; set; } = null!;
    
    // Çoklu Video Serisi (Series Planning)
    public virtual DbSet<KonuAnalizi> KonuAnalizleri { get; set; } = null!;
    public virtual DbSet<SeriPlani> SeriPlanlari { get; set; } = null!;
    public virtual DbSet<SeriBolum> SeriBolumler { get; set; } = null!;
    public virtual DbSet<BolumRevizyonu> BolumRevizyonlari { get; set; } = null!;
    public virtual DbSet<KaynakKonuCikarimi> KaynakKonuCikarimlari { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Add pgvector extension if using PostgreSQL
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            modelBuilder.HasPostgresExtension("vector");
        }
        // Apply all configurations in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ConfigureClientGeneratedGuidKeys(modelBuilder);

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            modelBuilder.Ignore<VideoChunkDocument>();
        }
        else if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            ConfigureSqliteCompatibility(modelBuilder);
        }
    }

    private static void ConfigureSqliteCompatibility(ModelBuilder modelBuilder)
    {
        var chunkEntity = modelBuilder.Entity<VideoChunkDocument>();
        chunkEntity.Property(x => x.Embedding)
            .HasColumnType("TEXT")
            .HasConversion(
                v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v.ToArray(), (System.Text.Json.JsonSerializerOptions?)null),
                s => string.IsNullOrWhiteSpace(s) ? null : new Pgvector.Vector(System.Text.Json.JsonSerializer.Deserialize<float[]>(s) ?? Array.Empty<float>()));

        var chunkType = modelBuilder.Model.FindEntityType(typeof(VideoChunkDocument));
        if (chunkType != null)
        {
            var embProp = chunkType.FindProperty(nameof(VideoChunkDocument.Embedding));
            if (embProp != null)
            {
                var indexesToRemove = chunkType.GetIndexes().Where(ix => ix.Properties.Contains(embProp)).ToList();
                foreach (var ix in indexesToRemove)
                {
                    chunkType.RemoveIndex(ix);
                }
            }
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var colType = property.GetColumnType();
                if (!string.IsNullOrEmpty(colType) &&
                    (colType.Contains("jsonb", StringComparison.OrdinalIgnoreCase) ||
                     colType.Contains("timestamptz", StringComparison.OrdinalIgnoreCase) ||
                     colType.Contains("interval", StringComparison.OrdinalIgnoreCase) ||
                     colType.Contains("vector", StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType(null);
                }

                var defaultSql = property.GetDefaultValueSql();
                if (!string.IsNullOrEmpty(defaultSql) &&
                    (defaultSql.Contains("gen_random_uuid", StringComparison.OrdinalIgnoreCase) ||
                     defaultSql.Contains("now()", StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetDefaultValueSql(null);
                }
            }
        }
    }

    /// <summary>
    /// Tüm entity'ler Id'yi istemci tarafında üretir (<c>Id = Guid.NewGuid()</c>).
    /// EF Core ise Guid anahtarları varsayılan olarak "store-generated" (ValueGeneratedOnAdd) kabul eder.
    /// Bu uyumsuzluk yüzünden, navigation koleksiyonuna eklenen yeni bir alt kayıt
    /// (örn. <c>seriPlani.SeriBolumler.Add(...)</c>) anahtarı dolu olduğu için "mevcut kayıt" sanılır,
    /// INSERT yerine UPDATE üretilir ve 0 satır etkilendiği için DbUpdateConcurrencyException fırlatılır.
    /// Anahtarları ValueGeneratedNever olarak işaretlemek EF'e gerçeği söyler: keşfedilen yeni entity = Added.
    /// Not: DB tarafındaki gen_random_uuid() default'ları korunur; EF her zaman istemci değerini gönderir.
    /// </summary>
    private static void ConfigureClientGeneratedGuidKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned()) continue;

            var pk = entityType.FindPrimaryKey();
            if (pk == null || pk.Properties.Count != 1) continue;

            var keyProperty = pk.Properties[0];
            if (keyProperty.ClrType == typeof(Guid))
            {
                keyProperty.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }
}
