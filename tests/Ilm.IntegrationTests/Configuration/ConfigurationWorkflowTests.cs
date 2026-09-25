using Ilm.Application.Abstractions;
using Ilm.Application.Configuration;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;
using Ilm.Domain.Protection;
using Ilm.Infrastructure.Development;
using Ilm.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Ilm.IntegrationTests.Configuration;

public sealed class ConfigurationWorkflowTests : IAsyncLifetime
{
    private IlmTestHost host = null!;

    public async Task InitializeAsync() => host = await IlmTestHost.CreateAsync();

    public async Task DisposeAsync() => await host.DisposeAsync();

    private async Task<IlmConfigurationDocument> Active()
    {
        await using var scope = host.Scope();
        var doc = (await scope.ServiceProvider.GetRequiredService<IActiveConfigurationProvider>().GetAsync(CancellationToken.None)).Document;
        return ConfigurationSerializer.Deserialize(ConfigurationSerializer.Serialize(doc));
    }

    private Task<ConfigurationVersion> Propose(IlmConfigurationDocument doc, ActorContext actor) =>
        host.WithAsync<ConfigurationService, ConfigurationVersion>(s => s.ProposeAsync(ConfigurationSerializer.Serialize(doc), "test change", actor, CancellationToken.None));

    [Fact]
    public async Task Protection_change_requires_a_different_security_approver_then_activates()
    {
        var casey = await host.ActorAsync(Operators.Casey);
        var sasha = await host.ActorAsync(Operators.Sasha);
        var adrian = await host.ActorAsync(Operators.Adrian);
        var doc = await Active();
        doc.ProtectionAdditions.Add(new ProtectedObjectDefinition { MatchOn = ProtectedObjectMatch.SamAccountName, Value = "alex.example", Category = ProtectionCategory.Tier0Adjacent, Source = ProtectionSource.ImportedAttackPath, SourceReference = "test" });
        var proposed = await Propose(doc, casey);
        Assert.Equal(ConfigurationVersionStatus.AwaitingApproval, proposed.Status);

        // A Lifecycle Approver is not a Security Approver.
        await Assert.ThrowsAsync<DomainException>(() => host.WithAsync<ConfigurationService, ConfigurationVersion>(s => s.ApproveAsync(proposed.Version, true, null, adrian, CancellationToken.None)));
        var approved = await host.WithAsync<ConfigurationService, ConfigurationVersion>(s => s.ApproveAsync(proposed.Version, true, null, sasha, CancellationToken.None));
        Assert.Equal(ConfigurationVersionStatus.Approved, approved.Status);
        var active = await host.WithAsync<ConfigurationService, ConfigurationVersion>(s => s.ActivateAsync(proposed.Version, casey, CancellationToken.None));
        Assert.Equal(ConfigurationVersionStatus.Active, active.Status);
        Assert.Equal(1, active.SupersedesVersion);

        // The new protection applies immediately.
        await using var scope = host.Scope();
        var decision = await scope.ServiceProvider.GetRequiredService<Application.Protection.ProtectionService>().EvaluateAsync(FictionalIds.TargetConnector, FictionalIds.For("corp:user:alex.example"), CancellationToken.None);
        Assert.Equal(ProtectionStatus.Protected, decision.Status);
    }

    [Fact]
    public async Task Proposer_with_both_roles_still_cannot_approve_own_change()
    {
        var casey = await host.ActorAsync(Operators.Casey);
        var both = casey with { Roles = new Dictionary<Domain.Security.AppRole, IReadOnlyCollection<string>>(casey.Roles) { [Domain.Security.AppRole.SecurityApprover] = [] } };
        var proposed = await Propose(await Active(), both);
        var ex = await Assert.ThrowsAsync<DomainException>(() => host.WithAsync<ConfigurationService, ConfigurationVersion>(s => s.ApproveAsync(proposed.Version, true, null, both, CancellationToken.None)));
        Assert.Contains("own request", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scope_containing_the_runtime_identity_is_rejected()
    {
        var casey = await host.ActorAsync(Operators.Casey);
        var doc = await Active();
        doc.Scopes.Add(new ScopeDefinition { Id = "corp-platform", ConnectorId = FictionalIds.TargetConnector, OuObjectGuid = FictionalIds.CorpPlatformOu.ToString("D"), DisplayName = "platform" });
        var proposed = await Propose(doc, casey);
        Assert.Equal(ConfigurationVersionStatus.ValidationFailed, proposed.Status);
        Assert.Contains("RUNTIME_IDENTITY_MANAGEABLE", proposed.ValidationIssuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scopes_on_a_newly_added_connector_are_verified_against_that_directory()
    {
        var casey = await host.ActorAsync(Operators.Casey);
        var doc = await Active();
        doc.Connectors.Add(new DirectoryConnectorDefinition
        {
            Id = "legacy-c",
            DisplayName = "Legacy forest C (legacy-c.example.test)",
            ForestDnsName = "legacy-c.example.test",
            DomainDnsName = "legacy-c.example.test",
            Role = ForestRole.Legacy,
            Mode = ConnectorMode.ReadOnlyLegacy,
            Implementation = "Mock",
            DomainControllers = ["lgcdc01.legacy-c.example.test"],
        });
        doc.Scopes.Add(new ScopeDefinition { Id = "legacy-c-staff", ConnectorId = "legacy-c", OuObjectGuid = FictionalIds.For("legacy-c:ou:missing").ToString("D"), DisplayName = "missing" });
        var proposed = await Propose(doc, casey);
        Assert.Equal(ConfigurationVersionStatus.ValidationFailed, proposed.Status);
        Assert.Matches("SCOPE_UNRESOLVED|SCOPE_UNVERIFIABLE", proposed.ValidationIssuesJson);
    }

    [Fact]
    public async Task Bootstrap_only_creates_the_first_version_and_needs_a_source_reference()
    {
        var doc = await Active();
        await Assert.ThrowsAsync<DomainException>(() => host.WithAsync<ConfigurationService, bool>(s => s.BootstrapAsync(doc, " ", CancellationToken.None)));
        Assert.False(await host.WithAsync<ConfigurationService, bool>(s => s.BootstrapAsync(doc, "change CHG-1", CancellationToken.None)));
    }

    [Fact]
    public async Task Legacy_write_activation_and_okta_provisioning_are_rejected()
    {
        var casey = await host.ActorAsync(Operators.Casey);
        var doc = await Active();
        doc.Connectors.First(c => c.Role == ForestRole.Legacy).Mode = ConnectorMode.WriteTarget;
        doc.FeatureFlags[nameof(Feature.OktaUserProvisioning)] = true;
        var proposed = await Propose(doc, casey);
        Assert.Equal(ConfigurationVersionStatus.ValidationFailed, proposed.Status);
        Assert.Contains("LEGACY_GENERALLY_WRITABLE", proposed.ValidationIssuesJson, StringComparison.Ordinal);
        Assert.Contains("OKTA_PROVISIONING_WITHOUT_FEASIBILITY", proposed.ValidationIssuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feasibility_reference_must_point_to_an_approved_pcatest_report()
    {
        var casey = await host.ActorAsync(Operators.Casey);
        var doc = await Active();
        doc.FeatureFlags[nameof(Feature.OktaUserProvisioning)] = true;
        doc.FeasibilityApprovals.Add(new FeasibilityApprovalReference { FeasibilityRunId = Guid.NewGuid(), ReportSha256 = "abc", Recommendation = Domain.Feasibility.FeasibilityRecommendation.Go, ApprovedBy = "someone" });
        var proposed = await Propose(doc, casey);
        Assert.Contains("FEASIBILITY_REFERENCE_INVALID", proposed.ValidationIssuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_administrator_cannot_propose()
    {
        var olivia = await host.ActorAsync(Operators.Olivia);
        await Assert.ThrowsAsync<DomainException>(() => Propose(FictionalConfiguration.Bootstrap(), olivia));
    }
}
