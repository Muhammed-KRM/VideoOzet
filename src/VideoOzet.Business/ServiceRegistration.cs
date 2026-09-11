using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using FluentValidation;
using MassTransit;
using Minio;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Business.Infrastructure.Storage;

namespace VideoOzet.Business;

public static class ServiceRegistration
{
    public static IServiceCollection AddBusinessLayer(this IServiceCollection services, IConfiguration configuration, Action<IBusRegistrationConfigurator>? configureMassTransit = null)
    {
        services.AddAutoMapper(cfg => cfg.AddMaps(Assembly.GetExecutingAssembly()));
        
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        // Minio Registration
        services.AddMinio(configureClient => configureClient
            .WithEndpoint(configuration["Minio:Endpoint"])
            .WithCredentials(configuration["Minio:AccessKey"], configuration["Minio:SecretKey"])
            .WithSSL(configuration.GetValue<bool>("Minio:UseSSL"))
            .Build());

        services.AddScoped<IFileStorageService, MinioFileService>();

        // Services Registration
        services.AddScoped<IEgitimService, EgitimManager>();
        services.AddScoped<IVideoService, VideoManager>();
        services.AddScoped<IDokumanService, DokumanService>();
        services.AddScoped<IAudioExtractor, VideoOzet.Business.Infrastructure.Media.FfmpegAudioExtractor>();
        services.AddHttpClient<ISttProvider, VideoOzet.Business.Infrastructure.AI.OpenAIWhisperProvider>()
            .AddStandardResilienceHandler();
        services.AddScoped<IGeminiProvider, VideoOzet.Business.Infrastructure.AI.GeminiSummarizer>();
        services.AddScoped<ITextChunker, VideoOzet.Business.Infrastructure.AI.TextChunker>();
        services.AddScoped<IEmbeddingProvider, VideoOzet.Business.Infrastructure.AI.OpenAIEmbeddingProvider>();
        services.AddScoped<ISynthesisProvider, VideoOzet.Business.Infrastructure.AI.ClaudeSynthesisProvider>();
        services.AddScoped<ILogService, LogManager>();

        // Redis Registration
        var redisConn = configuration["Redis:ConnectionString"] ?? "localhost:6379";
        var redisOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConn);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(StackExchange.Redis.ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<ICacheService, VideoOzet.Business.Infrastructure.Cache.RedisCacheService>();

        // MassTransit (RabbitMQ) Registration
        services.AddMassTransit(x =>
        {
            configureMassTransit?.Invoke(x); // Allows Worker to add consumers
            
            x.UsingRabbitMq((context, cfg) =>
            {
                var host = configuration["RabbitMQ:Host"] ?? "localhost";
                var portStr = configuration["RabbitMQ:Port"];
                ushort port = ushort.TryParse(portStr, out var p) ? p : (ushort)5672;

                cfg.Host(host, port, "/", h =>
                {
                    h.Username(configuration["RabbitMQ:Username"] ?? "guest");
                    h.Password(configuration["RabbitMQ:Password"] ?? "guest");
                });

                // Resilience: Automatic exponential retry for transient errors
                cfg.UseMessageRetry(r => r.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)));
                
                cfg.ConfigureEndpoints(context); // Auto-configures consumers
            });
        });

        return services;
    }
}
