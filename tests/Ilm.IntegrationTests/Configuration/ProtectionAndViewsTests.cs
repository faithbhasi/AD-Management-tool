using Ilm.Application.Protection;
using Ilm.Domain.Common;
using Ilm.Domain.Directory;
using Ilm.Domain.Protection;
using Ilm.Infrastructure.Development;
using Ilm.IntegrationTests.Support;
using Ilm.Modules.ReadOnly;
using Microsoft.Extensions.DependencyInjection;

namespace Ilm.IntegrationTests.Configuration;

public sealed class ProtectionAndViewsTests : IAsyncLifetime
{
    private IlmTestHost host = null!;

    public async Task InitializeAsync() => host = await IlmTestHost.CreateAsync();

    public async Task DisposeAsync() => await host.DisposeAsync();

    private async Task<ProtectionDecision> Evaluate(string connector, Guid guid)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<ProtectionService>().EvaluateAsync(connector, guid, CancellationToken.None);
    }

    [Theory]
    [InlineData("corp", "corp:user:t0-kai.privileged", ProtectionCategory.ProtectedByAdminCount)]
    [InlineData("corp", "corp:user:lee.nested", ProtectionCategory.Tier0Group)]
    [InlineData("corp", "corp:gmsa:svc-ilm$", ProtectionCategory.PortalRuntimeIdentity)]
    [InlineData("corp", "corp:computer:ILM-WEB01", ProtectionCategory.ManagedPasswordRetriever)]
    [InlineData("corp", "corp:computer:DC01", ProtectionCategory.DomainController)]
    [InlineData("corp", "corp:computer:CA01", ProtectionCategory.CertificateAuthorityServer)]
    [InlineData("corp", "corp:computer:OKTA-AGT01", ProtectionCategory.OktaAgentServer)]
    [InlineData("legacy-a", "legacy-a:user:emerson.test", ProtectionCategory.Tier0Group)]
    public async Task Fictional_tier0_objects_are_protected(string connector, string key, ProtectionCategory category)
    {
        var decision = await Evaluate(connector, FictionalIds.For(key));
        Assert.Equal(ProtectionStatus.Protected, decision.Status);
        Assert.Contains(decision.Reasons, r => r.Category == category);
    }

    [Fact]
    public async Task Foreign_security_principal_is_resolved_across_forests() =>
        Assert.Contains((await Evaluate("legacy-a", FictionalIds.For("legacy-a:user:emerson.test"))).Reasons, r => r.Detail.Contains("-512", StringComparison.Ordinal));

    [Fact]
    public async Task Unavailable_forest_makes_cross_forest_protection_unknown()
    {
        host.Directory.Forest(FictionalIds.LegacyBConnector).Available = false;
        var decision = await Evaluate("corp", FictionalIds.For("corp:user:alex.example"));
        Assert.Equal(ProtectionStatus.Unknown, decision.Status);
    }

    [Fact]
    public async Task Ordinary_user_is_clear() =>
        Assert.Equal(ProtectionStatus.Clear, (await Evaluate("corp", FictionalIds.For("corp:user:alex.example"))).Status);

    [Fact]
    public async Task Views_are_scope_limited_and_explain_unavailable_actions()
    {
        var olivia = await host.ActorAsync(Operators.Olivia);
        await using var scope = host.Scope();
        var views = scope.ServiceProvider.GetRequiredService<DirectoryViewService>();
        var computers = await views.SearchAsync(olivia, DirectoryObjectKind.Computer, null, CancellationToken.None);
        Assert.DoesNotContain(computers, c => c.Name == "DC01");
        Assert.Contains(computers, c => c.Name == "WS-0001");
        await Assert.ThrowsAsync<DomainException>(() => views.GetObjectAsync(olivia, "corp", FictionalIds.For("corp:computer:DC01"), CancellationToken.None));

        var stale = await views.GetObjectAsync(olivia, "corp", FictionalIds.For("corp:computer:WS-0002"), CancellationToken.None);
        Assert.Contains(stale.Warnings, w => w.Contains("more than 90 days", StringComparison.Ordinal));
        Assert.Contains(stale.Actions, a => !a.Available && a.Reason.Contains("ComputerDisableAndMove", StringComparison.Ordinal));

        var person = await scope.ServiceProvider.GetRequiredService<PersonViewService>().GetAsync(olivia, await host.PersonIdAsync("Bailey Sample"), CancellationToken.None);
        Assert.Equal(3, person.Identities.Count);
        Assert.Contains(person.Actions, a => a.Name == "Initiate containment" && a.Available);
        Assert.Contains(person.Actions, a => a.Name == "Reset password" && !a.Available && a.Reason.Contains("feature flag", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reader_without_scope_role_cannot_view_people()
    {
        var dale = await host.ActorAsync(FictionalIds.DbaDale);
        await using var scope = host.Scope();
        await Assert.ThrowsAsync<DomainException>(() => scope.ServiceProvider.GetRequiredService<PersonViewService>().SearchAsync(dale, null, CancellationToken.None));
    }
}
