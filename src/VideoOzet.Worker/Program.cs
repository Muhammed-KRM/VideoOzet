using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VideoOzet.Business;
using VideoOzet.Data;
using VideoOzet.Worker.Consumers;

// Load root .env file if present (same logic as API)
var envVars = new Dictionary<string, string>();
var searchDir = new DirectoryInfo(Directory.GetCurrentDirectory());
while (searchDir != null)
{
    var envPath = Path.Combine(searchDir.FullName, ".env");
    if (File.Exists(envPath))
    {
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
            var parts = trimmed.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var val = parts[1].Trim();
                Environment.SetEnvironmentVariable(key, val);
                envVars[key] = val;
            }
        }
        break;
    }
    searchDir = searchDir.Parent;
}

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddEnvironmentVariables();
        // Add .env values as in-memory configuration so IConfiguration can access them
        if (envVars.Count > 0)
        {
            config.AddInMemoryCollection(envVars!);
        }
    })
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // Add Data Layer
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
                               ?? throw new InvalidOperationException("DefaultConnection is missing.");
        services.AddDataLayer(connectionString);

        // Add Business Layer (which configures MassTransit internally)
        services.AddBusinessLayer(configuration, mt =>
        {
            // Register Consumers
            mt.AddConsumer<ExtractTranscriptConsumer>();
            mt.AddConsumer<SummarizeVideoConsumer>();
            mt.AddConsumer<IndexSummaryConsumer>();
            mt.AddConsumer<GenerateContentConsumer>();
            mt.AddConsumer<QualityCheckConsumer>();
            mt.AddConsumer<ExtractDokumanTextConsumer>();
            mt.AddConsumer<IndexDokumanConsumer>();
            
            // Phase 3 Consumers
            mt.AddConsumer<TopicAnalysisConsumer>();
            mt.AddConsumer<TopicAnalysisCompletedConsumer>();
            mt.AddConsumer<SeriesPlanConsumer>();
            mt.AddConsumer<SeriesVideoGenerationConsumer>();
            mt.AddConsumer<SeriesVideoGeneratedConsumer>();
            mt.AddConsumer<SeriesVideoRevisionConsumer>();
        });

    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("VideoOzet Worker is starting...");

await host.RunAsync();
