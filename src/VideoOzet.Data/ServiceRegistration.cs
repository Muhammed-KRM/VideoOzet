using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Data.Context;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Data;

public static class ServiceRegistration
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services, string connectionString)
    {
        // Add DbContext
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, o => o.UseVector()));

        // Add Repositories
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

        
        // Diğer repositoryler eklenecek
        // services.AddScoped<IMessageRepository, MessageRepository>();

        return services;
    }
}
