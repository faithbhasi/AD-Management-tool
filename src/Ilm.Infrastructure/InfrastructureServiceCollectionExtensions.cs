using Ilm.Application.Audit;
using Ilm.Application.Directory;
using Ilm.Application.Health;
using Ilm.Application.Okta;
using Ilm.Application.Protection;
using Ilm.Application.Security;
using Ilm.Application.Sessions;
using Ilm.Domain.Leaver;
using Ilm.Infrastructure.Audit;
using Ilm.Infrastructure.Development;
using Ilm.Infrastructure.Directory;
using Ilm.Infrastructure.Directory.Ldap;
using Ilm.Infrastructure.Directory.Mock;
using Ilm.Infrastructure.Health;
using Ilm.Infrastructure.Okta.Http;
using Ilm.Infrastructure.Okta.Mock;
using Ilm.Infrastructure.Platform;
using Ilm.Infrastructure.Security;
using Ilm.Infrastructure.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Ilm.Infrastructure;

public sealed class SessionConnectorOptions
{
    public const string SectionName = "Ilm:Sessions";

    /// <summary>Development only: use mock Entra and Citrix connectors instead of NotConfigured.</summary>
    public bool UseDevelopmentMocks { get; set; }
}

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddIlmInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<OktaOptions>(configuration.GetSection(OktaOptions.SectionName));
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));
        services.Configure<PlatformOptions>(configuration.GetSection(PlatformOptions.SectionName));
        services.Configure<SessionConnectorOptions>(configuration.GetSection(SessionConnectorOptions.SectionName));
        services.AddMemoryCache();

        var oktaMode = configuration.GetSection(OktaOptions.SectionName).GetValue<OktaMode>(nameof(OktaOptions.Mode));
        var auditOptions = configuration.GetSection(AuditOptions.SectionName).Get<AuditOptions>() ?? new AuditOptions();
        var sessionOptions = configuration.GetSection(SessionConnectorOptions.SectionName).Get<SessionConnectorOptions>() ?? new SessionConnectorOptions();

        // Fictional in-memory directory and Okta org (used by "Mock" connectors and Okta mock mode).
        var (store, mockOkta) = FictionalOkta.CreateEnvironment();
        services.AddSingleton(store);
        services.AddSingleton(mockOkta);
        services.AddSingleton<LdapConnectionFactory>();
        services.AddScoped<IDirectoryConnectorRegistry, DirectoryConnectorRegistry>();
        services.AddScoped<IAdTargetStateReconciler, AdTargetStateReconciler>();
        services.AddScoped<IFeasibilityDirectoryProbe, DirectoryFeasibilityProbe>();

        services.AddHttpClient("okta", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("okta-token", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("siem", c => c.Timeout = TimeSpan.FromSeconds(30));

        if (oktaMode == OktaMode.Live)
        {
            services.AddSingleton<IOktaAccessTokenProvider, PrivateKeyJwtTokenProvider>();
            services.AddScoped<OktaApiClient>();
            services.AddScoped<IOktaUserClient>(sp => sp.GetRequiredService<OktaApiClient>());
            services.AddScoped<IOktaLifecycleClient>(sp => sp.GetRequiredService<OktaApiClient>());
            services.AddScoped<IOktaSessionClient>(sp => sp.GetRequiredService<OktaApiClient>());
            services.AddScoped<IOktaGroupClient>(sp => sp.GetRequiredService<OktaApiClient>());
            services.AddScoped<IOktaApplicationClient>(sp => sp.GetRequiredService<OktaApiClient>());
            services.AddScoped<IOktaSystemLogClient>(sp => sp.GetRequiredService<OktaApiClient>());
            services.AddScoped<IOktaFeasibilityCleanup>(sp => sp.GetRequiredService<OktaApiClient>());
        }
        else
        {
            services.AddSingleton<IOktaUserClient>(mockOkta);
            services.AddSingleton<IOktaLifecycleClient>(mockOkta);
            services.AddSingleton<IOktaSessionClient>(mockOkta);
            services.AddSingleton<IOktaGroupClient>(mockOkta);
            services.AddSingleton<IOktaApplicationClient>(mockOkta);
            services.AddSingleton<IOktaSystemLogClient>(mockOkta);
            services.AddSingleton<IOktaFeasibilityCleanup>(mockOkta);
            services.AddSingleton<IFeasibilityEnvironmentControl>(mockOkta);
        }

        services.AddScoped<IPrivilegedRoleResolver, OktaPrivilegedRoleResolver>();

        services.AddScoped<ISessionConnector, OktaSessionConnector>();
        if (sessionOptions.UseDevelopmentMocks)
        {
            services.AddSingleton(new MockSessionConnector(SessionSystem.Entra));
            services.AddSingleton(new MockSessionConnector(SessionSystem.Citrix));
            services.AddSingleton<ISessionConnector>(sp => sp.GetServices<MockSessionConnector>().First(c => c.System == SessionSystem.Entra));
            services.AddSingleton<ISessionConnector>(sp => sp.GetServices<MockSessionConnector>().First(c => c.System == SessionSystem.Citrix));
        }
        else
        {
            services.AddSingleton<ISessionConnector>(new NotConfiguredSessionConnector(SessionSystem.Entra, "Entra sign-in session and refresh-token revocation (Microsoft Graph) is not integrated."));
            services.AddSingleton<ISessionConnector>(new NotConfiguredSessionConnector(SessionSystem.Citrix, "Citrix session logoff is not integrated."));
        }

        services.AddSingleton<ISessionConnector>(new NotConfiguredSessionConnector(SessionSystem.Vpn, "VPN session termination is not integrated."));
        services.AddSingleton<ISessionConnector>(new NotConfiguredSessionConnector(SessionSystem.Application, "Application-maintained sessions are not integrated; they may outlive Okta revocation."));

        services.AddSingleton<IAuditKeyProvider, AuditKeyProvider>();
        if (!string.IsNullOrWhiteSpace(auditOptions.JsonlSinkPath))
        {
            services.AddSingleton<JsonlFileAuditForwarder>();
            services.AddSingleton<IAuditForwarder>(sp => sp.GetRequiredService<JsonlFileAuditForwarder>());
            services.AddSingleton<IAuditSinkReader>(sp => sp.GetRequiredService<JsonlFileAuditForwarder>());
        }

        if (!string.IsNullOrWhiteSpace(auditOptions.SiemEndpoint))
        {
            services.AddSingleton<IAuditForwarder, HttpSiemAuditForwarder>();
        }

        services.AddSingleton<IPlatformIdentityProvider, PlatformIdentityProvider>();
        services.AddScoped<IConnectorHealthSource, ConnectorHealthSource>();
        services.AddScoped<DevelopmentSeeder>();
        services.TryAddSingleton(environment);
        return services;
    }
}
