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
    public virtual DbSet<QcResult> QcResults { get; set; } = null!;
    public virtual DbSet<PipelineLog> PipelineLogs { get; set; } = null!;
    public virtual DbSet<VideoChunkDocument> VideoChunkDocuments { get; set; } = null!;
    
    public virtual DbSet<EndpointLog> EndpointLogs { get; set; } = null!;
    public virtual DbSet<FunctionLog> FunctionLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Add pgvector extension if using PostgreSQL
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            modelBuilder.HasPostgresExtension("vector");
        }
        else if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            modelBuilder.Ignore<VideoChunkDocument>();
        }

        // Apply all configurations in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
