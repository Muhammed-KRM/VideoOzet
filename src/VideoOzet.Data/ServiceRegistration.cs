using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Data.Context;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Data;

public static class ServiceRegistration
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            if (connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
                connectionString.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseNpgsql(connectionString, o => o.UseVector());
            }
        });

        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

        return services;
    }
}
