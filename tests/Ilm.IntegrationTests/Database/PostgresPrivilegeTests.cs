using System.Security.Cryptography;
using Ilm.Application.Audit;
using Ilm.Infrastructure.Audit;
using Ilm.IntegrationTests.Support;
using Ilm.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ilm.IntegrationTests.Database;

/// <summary>
/// Runs the real role and grant scripts against a disposable PostgreSQL and proves the application identity
/// cannot change the schema or rewrite history. Skipped when ILM_TEST_POSTGRES is not set.
/// </summary>
public sealed class PostgresPrivilegeTests
{
    private static string Repo(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ilm.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, relative);
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Application_identity_has_dml_only_and_history_is_append_only()
    {
        var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable));
        var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var database = "ilm_test_" + suffix;
        var migratorRole = "ilm_migrator_" + suffix;
        var appRole = "ilm_app_" + suffix;
        var migratorPassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var appPassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        // Apply the shipped scripts with test-unique role names.
        var roles = (await File.ReadAllTextAsync(Repo("deploy/postgres/00-create-roles-and-database.sql")))
            .Replace("ilm_migrator", migratorRole, StringComparison.Ordinal).Replace("ilm_app", appRole, StringComparison.Ordinal)
            .Replace("ilm_auditor_ro", "ilm_auditor_ro_" + suffix, StringComparison.Ordinal).Replace("ilm_dba", "ilm_dba_" + suffix, StringComparison.Ordinal)
            .Replace(":'migrator_password'", $"'{migratorPassword}'", StringComparison.Ordinal).Replace(":'app_password'", $"'{appPassword}'", StringComparison.Ordinal)
            .Replace("CREATE DATABASE ilm ", $"CREATE DATABASE {database} ", StringComparison.Ordinal)
            .Replace("ON DATABASE ilm ", $"ON DATABASE {database} ", StringComparison.Ordinal);
        var (serverPart, dbPart) = SplitAtConnect(roles);
        // Strip comment lines first (comments contain semicolons), then run statement by statement.
        var serverSql = string.Join('\n', serverPart.Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        foreach (var statement in serverSql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await ExecAsync(admin.ConnectionString, statement);
        }

        var adminOnDb = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database };
        await ExecAsync(adminOnDb.ConnectionString, dbPart);

        var migrator = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database, Username = migratorRole, Password = migratorPassword };
        var app = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database, Username = appRole, Password = appPassword };
        var keys = new StaticAuditKeyProvider(RandomNumberGenerator.GetBytes(32));

        await using (var ctx = new PostgresIlmDbContext(new DbContextOptionsBuilder<PostgresIlmDbContext>().UseNpgsql(migrator.ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", PostgresIlmDbContext.Schema)).Options, keys))
        {
            await ctx.Database.MigrateAsync();
        }

        var grants = (await File.ReadAllTextAsync(Repo("deploy/postgres/20-grants-after-migration.sql")))
            .Replace("ilm_app", appRole, StringComparison.Ordinal).Replace("ilm_auditor_ro", "ilm_auditor_ro_" + suffix, StringComparison.Ordinal);
        await ExecAsync(migrator.ConnectionString, string.Join('\n', grants.Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal))));

        // The application identity can append audit through the normal path.
        await using (var ctx = new PostgresIlmDbContext(new DbContextOptionsBuilder<PostgresIlmDbContext>().UseNpgsql(app.ConnectionString).Options, keys))
        {
            ctx.AuditRecords.Add(new Domain.Audit.AuditRecord { TimestampUtc = DateTime.UtcNow, Action = "PgTest", Result = "ok" });
            ctx.AuditRecords.Add(new Domain.Audit.AuditRecord { TimestampUtc = DateTime.UtcNow, Action = "PgTest2", Result = "ok" });
            await ctx.SaveChangesAsync();
        }

        // Verify what PostgreSQL actually stored (a fresh context, no tracked instances): the chain must survive
        // the round trip, including timestamptz's microsecond precision.
        await using (var ctx = new PostgresIlmDbContext(new DbContextOptionsBuilder<PostgresIlmDbContext>().UseNpgsql(app.ConnectionString).Options, keys))
        {
            var records = await ctx.AuditRecords.AsNoTracking().OrderBy(r => r.Sequence).ToListAsync();
            Assert.Equal(2, records.Count);
            var verification = new AuditChainVerifier(keys).Verify(records);
            Assert.True(verification.IsValid, string.Join("; ", verification.Problems));
        }

        // ...but cannot change the schema or rewrite history.
        string[] forbidden =
        [
            "CREATE TABLE ilm.evil(id int)",
            "ALTER TABLE ilm.\"AuditRecords\" ADD COLUMN x int",
            "DROP TABLE ilm.\"Persons\"",
            "UPDATE ilm.\"AuditRecords\" SET \"Result\" = 'x'",
            "DELETE FROM ilm.\"AuditRecords\"",
            "TRUNCATE ilm.\"LeaverTransitions\"",
        ];
        foreach (var sql in forbidden)
        {
            await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(app.ConnectionString, sql));
        }

        // Even the schema owner is stopped by the append-only trigger.
        var ownerEx = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(migrator.ConnectionString, "UPDATE ilm.\"AuditRecords\" SET \"Result\" = 'x'"));
        Assert.Contains("append-only", ownerEx.MessageText, StringComparison.Ordinal);

        await ExecAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        foreach (var role in new[] { appRole, migratorRole, "ilm_auditor_ro_" + suffix, "ilm_dba_" + suffix })
        {
            await ExecAsync(admin.ConnectionString, $"DROP ROLE IF EXISTS {role}");
        }
    }

    private static (string Server, string Database) SplitAtConnect(string script)
    {
        var index = script.IndexOf("\\connect", StringComparison.Ordinal);
        var afterConnect = script.IndexOf('\n', index);
        return (script[..index], script[(afterConnect + 1)..]);
    }
}
