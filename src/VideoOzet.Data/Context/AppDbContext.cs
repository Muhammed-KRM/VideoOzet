using System.Reflection;
using Microsoft.EntityFrameworkCore;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }


    public DbSet<Egitim> Egitimler { get; set; } = null!;
    public DbSet<Video> Videolar { get; set; } = null!;
    public DbSet<VideoTranscript> VideoTranscripts { get; set; } = null!;
    public DbSet<VideoSummary> VideoSummaries { get; set; } = null!;
    public DbSet<ContentRequest> ContentRequests { get; set; } = null!;
    public DbSet<GeneratedContent> GeneratedContents { get; set; } = null!;
    public DbSet<QcResult> QcResults { get; set; } = null!;
    public DbSet<PipelineLog> PipelineLogs { get; set; } = null!;
    public DbSet<VideoChunkDocument> VideoChunkDocuments { get; set; } = null!;
    
    public DbSet<EndpointLog> EndpointLogs { get; set; } = null!;
    public DbSet<FunctionLog> FunctionLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Add pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Apply all configurations in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
