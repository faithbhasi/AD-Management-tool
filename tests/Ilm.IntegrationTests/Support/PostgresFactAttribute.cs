namespace Ilm.IntegrationTests.Support;

/// <summary>Runs only when ILM_TEST_POSTGRES holds an administrator connection string to a disposable PostgreSQL.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string Variable = "ILM_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"PostgreSQL not available: set {Variable} to an administrator connection string for a disposable instance.";
        }
    }
}
