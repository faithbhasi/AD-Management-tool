using System.Data.Common;
using System.Text.RegularExpressions;
using Ilm.Infrastructure;
using Ilm.Infrastructure.Okta.Http;
using Ilm.Persistence;
using Ilm.Web.Authentication;

namespace Ilm.Web.Infrastructure;

/// <summary>
/// Refuses to start in production with development shortcuts: mock OIDC, mock Okta, SQLite, unencrypted
/// database connections, non-HTTPS URLs, development session mocks or a missing redirect allowlist.
/// In every environment it refuses secrets placed directly in configuration: secrets are referenced by the name
/// of an environment variable (for example <c>ClientSecretEnvironmentVariable</c>), never stored as values.
/// </summary>
public static partial class StartupValidator
{
    [GeneratedRegex("(password|passwd|pwd|secret|token|apikey|api_key|privatekey|private_key|credential|sharedkey)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretKeyName();

    /// <summary>Configuration keys whose value would be a secret. Keys that name an environment variable are allowed.</summary>
    public static IReadOnlyList<string> FindSecretValues(IConfiguration configuration, bool production)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var problems = new List<string>();
        foreach (var (key, value) in configuration.AsEnumerable())
        {
            if (string.IsNullOrEmpty(value) || !(key.StartsWith("Ilm:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var leaf = key[(key.LastIndexOf(':') + 1)..];
            if (SecretKeyName().IsMatch(leaf))
            {
                problems.Add($"Configuration key '{key}' holds a secret value. Reference secrets by environment variable name instead.");
            }

            // Development may use a password for the local docker-compose database; production must not.
            if (production && key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase) && HasPassword(value))
            {
                problems.Add($"Connection string '{leaf}' contains a password. Use Kerberos (gss) with the gMSA for the application identity.");
            }
        }

        return problems;
    }

    private static bool HasPassword(string connectionString)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            return new[] { "Password", "Pwd" }.Any(k => builder.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v?.ToString()));
        }
        catch (ArgumentException)
        {
            return connectionString.Contains("password", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static IReadOnlyList<string> Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var problems = new List<string>();
        var production = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        var auth = configuration.GetSection(IlmAuthenticationOptions.SectionName).Get<IlmAuthenticationOptions>() ?? new IlmAuthenticationOptions();
        var db = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

        problems.AddRange(ConnectionSecurityValidator.Validate(db.Provider, configuration.GetConnectionString(db.ApplicationConnectionName), production));
        problems.AddRange(FindSecretValues(configuration, production));

        if (string.IsNullOrWhiteSpace(auth.Issuer) || string.IsNullOrWhiteSpace(auth.ClientId) || string.IsNullOrWhiteSpace(auth.PublicBaseUrl))
        {
            problems.Add("Authentication Issuer, ClientId and PublicBaseUrl are required.");
        }

        if (!auth.AllowedRedirectUris.Contains(auth.RedirectUri, StringComparer.Ordinal))
        {
            problems.Add("The computed redirect URI must appear exactly in AllowedRedirectUris.");
        }

        if (production)
        {
            if (auth.Mode != AuthenticationMode.Okta)
            {
                problems.Add("Production requires Okta OIDC authentication; the mock OIDC provider is development-only.");
            }

            if (!auth.Issuer.StartsWith("https://", StringComparison.Ordinal) || !auth.PublicBaseUrl.StartsWith("https://", StringComparison.Ordinal))
            {
                problems.Add("Production issuer and public base URL must use HTTPS.");
            }

            if (configuration.GetSection(OktaOptions.SectionName).GetValue<OktaMode>(nameof(OktaOptions.Mode)) != OktaMode.Live)
            {
                problems.Add("Production requires Okta Live mode; the mock Okta org is development-only.");
            }

            if (configuration.GetSection(SessionConnectorOptions.SectionName).GetValue<bool>(nameof(SessionConnectorOptions.UseDevelopmentMocks)))
            {
                problems.Add("Development session mocks cannot be used in production.");
            }

            if (configuration.GetValue<bool>("Ilm:Development:SeedOnStartup"))
            {
                problems.Add("Fictional development seeding cannot run in production.");
            }
        }

        return problems;
    }
}
