using System.Reflection;
using Ilm.Application.Provisioning;
using Ilm.Domain.Authority;

namespace Ilm.UnitTests.Architecture;

public sealed class ArchitectureRulesTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ilm.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Only_approved_types_obtain_the_raw_directory_write_channel()
    {
        string[] allowed = ["GuardedDirectoryWriter.cs", "DirectActiveDirectoryStrategy.cs", "AdTargetStateReconciler.cs", "IDirectoryConnector.cs", "DirectoryConnectorRegistry.cs"];
        var offenders = System.IO.Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("GetWriteChannel(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(f => !allowed.Contains(f))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void All_six_strategies_exist_and_implement_the_interface()
    {
        var kinds = typeof(IIdentityProvisioningStrategy).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIdentityProvisioningStrategy).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();
        foreach (var kind in Enum.GetNames<StrategyKind>())
        {
            Assert.Contains(kind + "Strategy", kinds);
        }

        var methods = typeof(IIdentityProvisioningStrategy).GetMethods().Select(m => m.Name).ToList();
        Assert.Contains("BuildPlanAsync", methods);
        Assert.Contains("ValidatePlanAsync", methods);
        Assert.Contains("ExecuteAsync", methods);
        Assert.Contains("ReconcileAsync", methods);
    }

    [Fact]
    public void Solution_stays_compact()
    {
        var projects = System.IO.Directory.EnumerateFiles(RepoRoot(), "*.csproj", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
        Assert.Equal(
            new[] { "Ilm.Application", "Ilm.Domain", "Ilm.EndToEndTests", "Ilm.Infrastructure", "Ilm.IntegrationTests", "Ilm.Modules.Leaver", "Ilm.Modules.ReadOnly", "Ilm.Persistence", "Ilm.SecurityTests", "Ilm.UnitTests", "Ilm.Web" },
            projects);
    }

    [Fact]
    public void Domain_has_no_infrastructure_dependencies()
    {
        var references = typeof(AuthorityRule).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(references, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || r.StartsWith("System.DirectoryServices", StringComparison.Ordinal) || r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Disabled_capabilities_have_typed_interfaces()
    {
        Assert.NotNull(typeof(Ilm.Application.Okta.IOktaUserClient).GetMethod("CreateUserAsync"));
        Assert.NotNull(typeof(Ilm.Application.Directory.IDirectoryWriteChannel).GetMethod("CreateDisabledUserAsync"));
        Assert.NotNull(typeof(Ilm.Application.Sessions.ISessionConnector).GetMethod("RevokeAsync", BindingFlags.Public | BindingFlags.Instance));
    }
}
