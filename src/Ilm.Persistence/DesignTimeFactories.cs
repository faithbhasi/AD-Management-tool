using Ilm.Application.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ilm.Persistence;

internal sealed class DesignTimeAuditKeys : IAuditKeyProvider
{
    public AuditKey CurrentKey { get; } = new("design-time", new byte[32]);

    public AuditKey? FindKey(string keyId) => CurrentKey;
}

/// <summary>Used only by <c>dotnet ef</c> to generate SQLite migrations.</summary>
internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteIlmDbContext>
{
    public SqliteIlmDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteIlmDbContext>()
            .UseSqlite("Data Source=design-time.db", o => o.MigrationsAssembly(typeof(SqliteIlmDbContext).Assembly.FullName))
            .Options, new DesignTimeAuditKeys());
}

/// <summary>Used only by <c>dotnet ef</c> to generate PostgreSQL migrations. No connection is opened.</summary>
internal sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresIlmDbContext>
{
    public PostgresIlmDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresIlmDbContext>()
            .UseNpgsql("Host=localhost;Database=ilm_design_time", o => o.MigrationsHistoryTable("__EFMigrationsHistory", PostgresIlmDbContext.Schema))
            .Options, new DesignTimeAuditKeys());
}
