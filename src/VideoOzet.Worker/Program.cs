using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VideoOzet.Business;
using VideoOzet.Data;
using VideoOzet.Worker.Consumers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddEnvironmentVariables();
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
        });

    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("VideoOzet Worker is starting...");

await host.RunAsync();
