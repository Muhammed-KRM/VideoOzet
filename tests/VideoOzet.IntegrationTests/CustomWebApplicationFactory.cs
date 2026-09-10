using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Testcontainers.RabbitMq;
using Testcontainers.Minio;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace VideoOzet.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg15")
        .WithDatabase("videoozet_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder().Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder().Build();

    private readonly MinioContainer _minioContainer = new MinioBuilder()
        .WithUsername("minioadmin")
        .WithPassword("minioadmin")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();
        await _minioContainer.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Optional: Mock external AI services here (Whisper/Gemini) if needed.
        });

        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgresContainer.GetConnectionString());
        builder.UseSetting("Redis:ConnectionString", _redisContainer.GetConnectionString());
        builder.UseSetting("RabbitMQ:Host", _rabbitMqContainer.Hostname);
        builder.UseSetting("RabbitMQ:Port", _rabbitMqContainer.GetMappedPublicPort(5672).ToString());
        builder.UseSetting("RabbitMQ:Username", RabbitMqBuilder.DefaultUsername);
        builder.UseSetting("RabbitMQ:Password", RabbitMqBuilder.DefaultPassword);
        builder.UseSetting("Minio:Endpoint", $"{_minioContainer.Hostname}:{_minioContainer.GetMappedPublicPort(9000)}");
        builder.UseSetting("Minio:AccessKey", "minioadmin");
        builder.UseSetting("Minio:SecretKey", "minioadmin");
        builder.UseSetting("Minio:UseSSL", "false");
        builder.UseSetting("ApiKey", "test-valid-key");
    }

    public new async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _rabbitMqContainer.DisposeAsync();
        await _minioContainer.DisposeAsync();
    }
}
