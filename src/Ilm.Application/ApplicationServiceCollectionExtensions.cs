using Ilm.Application.Approvals;
using Ilm.Application.Audit;
using Ilm.Application.Authority;
using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Application.Feasibility;
using Ilm.Application.Protection;
using Ilm.Application.Provisioning;
using Ilm.Application.Reconciliation;
using Ilm.Application.Security;
using Ilm.Application.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ilm.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddIlmApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IActiveConfigurationProvider, ActiveConfigurationProvider>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<AuditChainVerifier>();
        services.AddScoped<AuditForwardingService>();
        services.AddScoped<ApprovalService>();
        services.AddScoped<ConfigurationService>();
        services.AddScoped<AuthorityService>();
        services.AddScoped<ProtectionService>();
        services.AddScoped<ScopeEvaluator>();
        services.AddScoped<GuardedDirectoryWriter>();
        services.AddScoped<AppUserService>();
        services.AddScoped<Identity.IdentityLinkService>();
        services.AddScoped<IssuerMigrationService>();
        services.AddScoped<AlertService>();
        services.AddScoped<ManualTaskService>();
        services.AddScoped<ReconciliationService>();
        services.AddScoped<OktaAdFeasibilityHarness>();
        services.AddScoped<FeasibilityService>();

        services.AddScoped<ReadOnlyStrategy>();
        services.AddScoped<ContainmentOnlyLegacyStrategy>();
        services.AddScoped<DirectActiveDirectoryStrategy>();
        services.AddScoped<OktaApiProvisioningStrategy>();
        services.AddScoped<MigrationTransitionStrategy>();
        services.AddScoped<ManualControlledStrategy>();
        services.AddScoped<IIdentityProvisioningStrategy>(sp => sp.GetRequiredService<ReadOnlyStrategy>());
        services.AddScoped<IIdentityProvisioningStrategy>(sp => sp.GetRequiredService<ContainmentOnlyLegacyStrategy>());
        services.AddScoped<IIdentityProvisioningStrategy>(sp => sp.GetRequiredService<DirectActiveDirectoryStrategy>());
        services.AddScoped<IIdentityProvisioningStrategy>(sp => sp.GetRequiredService<OktaApiProvisioningStrategy>());
        services.AddScoped<IIdentityProvisioningStrategy>(sp => sp.GetRequiredService<MigrationTransitionStrategy>());
        services.AddScoped<IIdentityProvisioningStrategy>(sp => sp.GetRequiredService<ManualControlledStrategy>());
        services.AddScoped<StrategyRegistry>();
        return services;
    }
}
