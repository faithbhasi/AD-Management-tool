namespace Ilm.Persistence;

public enum DatabaseProvider
{
    Sqlite = 0,
    PostgreSql,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Ilm:Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Name of the connection string used by the application identity (DML only).</summary>
    public string ApplicationConnectionName { get; set; } = "Ilm";

    /// <summary>Name of the connection string used by the separate migration identity (schema owner).</summary>
    public string MigrationConnectionName { get; set; } = "IlmMigration";
}
