using System.Data.Common;

namespace Ilm.Persistence;

/// <summary>
/// Refuses insecure database connections in production: PostgreSQL only, TLS with full certificate
/// verification, no trust-all certificates, and no well-known superuser accounts.
/// </summary>
public static class ConnectionSecurityValidator
{
    public static IReadOnlyList<string> Validate(DatabaseProvider provider, string? connectionString, bool isProduction)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            problems.Add("No database connection string is configured.");
            return problems;
        }

        if (!isProduction)
        {
            return problems;
        }

        if (provider != DatabaseProvider.PostgreSql)
        {
            problems.Add("Production requires PostgreSQL; SQLite is for isolated development only.");
            return problems;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var sslMode = Get(builder, "SSL Mode", "SslMode", "sslmode");
        if (!string.Equals(sslMode, "VerifyFull", StringComparison.OrdinalIgnoreCase) && !string.Equals(sslMode, "verify-full", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Production database connections must use SSL Mode=VerifyFull (TLS with certificate and host-name verification).");
        }

        var trust = Get(builder, "Trust Server Certificate", "TrustServerCertificate");
        if (string.Equals(trust, "true", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Trust Server Certificate=true is not permitted in production.");
        }

        var user = Get(builder, "Username", "User ID", "UserId", "User", "uid");
        if (user is not null && (string.Equals(user, "postgres", StringComparison.OrdinalIgnoreCase) || string.Equals(user, "ilm_migrator", StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"The application must not connect as '{user}'; use the least-privilege application role.");
        }

        return problems;
    }

    private static string? Get(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value is not null)
            {
                return value.ToString();
            }
        }

        return null;
    }
}
