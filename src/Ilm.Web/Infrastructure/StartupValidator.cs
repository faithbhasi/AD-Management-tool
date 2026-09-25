using Ilm.Infrastructure;
using Ilm.Infrastructure.Okta.Http;
using Ilm.Persistence;
using Ilm.Web.Authentication;

namespace Ilm.Web.Infrastructure;

/// <summary>
/// Refuses to start in production with development shortcuts: mock OIDC, mock Okta, SQLite, unencrypted
/// database connections, non-HTTPS URLs, development session mocks or a missing redirect allowlist.
/// </summary>
public static class StartupValidator
{
    public static IReadOnlyList<string> Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var problems = new List<string>();
        var production = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        var auth = configuration.GetSection(IlmAuthenticationOptions.SectionName).Get<IlmAuthenticationOptions>() ?? new IlmAuthenticationOptions();
        var db = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

        problems.AddRange(ConnectionSecurityValidator.Validate(db.Provider, configuration.GetConnectionString(db.ApplicationConnectionName), production));

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
