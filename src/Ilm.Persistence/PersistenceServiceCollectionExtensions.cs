using Ilm.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ilm.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddIlmPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var connectionString = configuration.GetConnectionString(options.ApplicationConnectionName);
        services.AddSingleton(options);

        if (options.Provider == DatabaseProvider.PostgreSql)
        {
            services.AddDbContext<PostgresIlmDbContext>(o => o.UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", PostgresIlmDbContext.Schema)));
            services.AddScoped<IlmDbContext>(sp => sp.GetRequiredService<PostgresIlmDbContext>());
        }
        else
        {
            services.AddDbContext<SqliteIlmDbContext>(o => o.UseSqlite(connectionString));
            services.AddScoped<IlmDbContext>(sp => sp.GetRequiredService<SqliteIlmDbContext>());
        }

        services.AddScoped<IIlmDbContext>(sp => sp.GetRequiredService<IlmDbContext>());
        services.AddScoped<IDistributedLock, EfDistributedLock>();
        return services;
    }

    /// <summary>Creates a context bound to the migration identity's connection string.</summary>
    public static IlmDbContext CreateMigrationContext(IConfiguration configuration, Application.Audit.IAuditKeyProvider keys)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var cs = configuration.GetConnectionString(options.MigrationConnectionName) ?? configuration.GetConnectionString(options.ApplicationConnectionName);
        return options.Provider == DatabaseProvider.PostgreSql
            ? new PostgresIlmDbContext(new DbContextOptionsBuilder<PostgresIlmDbContext>().UseNpgsql(cs, n => n.MigrationsHistoryTable("__EFMigrationsHistory", PostgresIlmDbContext.Schema)).Options, keys)
            : new SqliteIlmDbContext(new DbContextOptionsBuilder<SqliteIlmDbContext>().UseSqlite(cs).Options, keys);
    }
}
