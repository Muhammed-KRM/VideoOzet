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
            .WithSSL(configuration.GetValue<bool>("Minio:UseSSL")));

        services.AddScoped<IFileStorageService, MinioFileService>();

        // Services Registration
        services.AddScoped<IEgitimService, EgitimManager>();
        services.AddScoped<IVideoService, VideoManager>();
        services.AddScoped<IAudioExtractor, VideoOzet.Business.Infrastructure.Media.FfmpegAudioExtractor>();
        services.AddHttpClient<ISttProvider, VideoOzet.Business.Infrastructure.AI.OpenAIWhisperProvider>();
        services.AddScoped<IGeminiProvider, VideoOzet.Business.Infrastructure.AI.GeminiSummarizer>();
        services.AddScoped<ITextChunker, VideoOzet.Business.Infrastructure.AI.TextChunker>();
        services.AddScoped<IEmbeddingProvider, VideoOzet.Business.Infrastructure.AI.OpenAIEmbeddingProvider>();
        services.AddScoped<ISynthesisProvider, VideoOzet.Business.Infrastructure.AI.ClaudeSynthesisProvider>();
        services.AddScoped<ILogService, LogManager>();

        // Redis Registration
        var redisConn = configuration["Redis:ConnectionString"] ?? "localhost:6379";
        services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn));
        services.AddSingleton<ICacheService, VideoOzet.Business.Infrastructure.Cache.RedisCacheService>();

        // MassTransit (RabbitMQ) Registration
        services.AddMassTransit(x =>
        {
            configureMassTransit?.Invoke(x); // Allows Worker to add consumers
            
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(configuration["RabbitMQ:Username"] ?? "guest");
                    h.Password(configuration["RabbitMQ:Password"] ?? "guest");
                });
                
                cfg.ConfigureEndpoints(context); // Auto-configures consumers
            });
        });

        return services;
    }
}
