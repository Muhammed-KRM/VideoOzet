using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoOzet.Data.Context;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using VideoOzet.IntegrationTests.Endpoints;

namespace VideoOzet.IntegrationTests.Setup;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("test_VideoOzet")
        .WithUsername("testuser")
        .WithPassword("testpass123")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _dbContainer.GetConnectionString(),
                ["Redis:Configuration"] = _redisContainer.GetConnectionString(),
                ["Jwt:Key"] = "SuperSecretKeyForIntegrationTesting12345!",
                ["Jwt:Issuer"] = "VideoOzetTesting",
                ["Jwt:Audience"] = "VideoOzetClient"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Orijinal AppDbContext'i kaldırıp yerine Testcontainers DbContext'ini ekliyoruz.
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(_dbContainer.GetConnectionString()));
        });
    }

    public new async Task DisposeAsync()
    {
        // Entegrasyon testleri raporunu oluştur
        EndpointTestReporter.GenerateReport();

        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
