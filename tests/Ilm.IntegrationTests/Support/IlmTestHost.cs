using Ilm.Application;
using Ilm.Application.Abstractions;
using Ilm.Application.Reconciliation;
using Ilm.Application.Security;
using Ilm.Infrastructure;
using Ilm.Infrastructure.Development;
using Ilm.Infrastructure.Directory.Mock;
using Ilm.Infrastructure.Okta.Mock;
using Ilm.Infrastructure.Sessions;
using Ilm.Modules.Leaver;
using Ilm.Modules.ReadOnly;
using Ilm.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Ilm.IntegrationTests.Support;

/// <summary>
/// The production service graph (application, persistence, infrastructure, modules) over a real SQLite database
/// with migrations, the fictional directory and the mock Okta org. No web layer.
/// </summary>
public sealed class IlmTestHost : IAsyncDisposable
{
    private readonly string root;

    private IlmTestHost(ServiceProvider services, string root, FakeTimeProvider time)
    {
        Services = services;
        this.root = root;
        Time = time;
    }

    public ServiceProvider Services { get; }

    public FakeTimeProvider Time { get; }

    public InMemoryDirectoryStore Directory => Services.GetRequiredService<InMemoryDirectoryStore>();

    public MockOktaOrg Okta => Services.GetRequiredService<MockOktaOrg>();

    public string SinkPath => Path.Combine(root, "audit.jsonl");

    public string DatabasePath => Path.Combine(root, "ilm.db");

    public MockSessionConnector Session(Domain.Leaver.SessionSystem system) =>
        Services.GetServices<MockSessionConnector>().First(s => s.System == system);

    public static async Task<IlmTestHost> CreateAsync(bool seed = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "ilm-it-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Ilm"] = $"Data Source={Path.Combine(root, "ilm.db")};Pooling=False",
            ["Ilm:Database:Provider"] = "Sqlite",
            ["Ilm:Okta:Mode"] = "Mock",
            ["Ilm:Sessions:UseDevelopmentMocks"] = "true",
            ["Ilm:Audit:JsonlSinkPath"] = Path.Combine(root, "audit.jsonl"),
            ["Ilm:Audit:DevelopmentKeyPath"] = Path.Combine(root, "audit.key"),
            ["Ilm:Platform:RuntimeIdentitySids:0"] = FictionalIds.RuntimeGmsaSid,
            ["Ilm:Platform:HostComputerSids:0"] = FictionalIds.PortalHostSid,
            ["Ilm:Platform:HostDnsNames:0"] = FictionalIds.PortalHostDns,
            ["Ilm:Platform:DatabaseServiceIdentitySids:0"] = FictionalIds.DatabaseServiceSid,
            ["Ilm:Platform:BreakGlassSids:0"] = FictionalIds.BreakGlassSid,
            ["Ilm:Platform:ManagedPasswordRetrieverSids:0"] = FictionalIds.GmsaRetrieversGroupSid,
            ["Ilm:Platform:ManagedPasswordRetrieversVerified"] = "true",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddIlmApplication();
        services.AddIlmPersistence(configuration);
        services.AddIlmInfrastructure(configuration, new TestEnvironment(root));
        services.AddIlmReadOnlyModule();
        services.AddIlmLeaverModule();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var host = new IlmTestHost(provider, root, time);

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IlmDbContext>().Database.MigrateAsync();
            if (seed)
            {
                await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<ReconciliationService>().RunAsync("test-setup", ActorContext.System("test"), CancellationToken.None);
            }
        }

        return host;
    }

    public AsyncServiceScope Scope() => Services.CreateAsyncScope();

    public async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> work)
        where TService : notnull
    {
        await using var scope = Scope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>Signs in a fictional operator and resolves roles server-side without cache, as a privileged commit would.</summary>
    public async Task<ActorContext> ActorAsync(string subject)
    {
        await using var scope = Scope();
        var op = FictionalOkta.Operators.First(o => o.Subject == subject);
        var user = await scope.ServiceProvider.GetRequiredService<AppUserService>().SignInAsync("https://localhost/mock-oidc", subject, op.DisplayName, op.Email, CancellationToken.None);
        var roles = await scope.ServiceProvider.GetRequiredService<IPrivilegedRoleResolver>().ResolveAsync(new OperatorIdentity("https://localhost/mock-oidc", subject), bypassCache: true, CancellationToken.None);
        return new ActorContext
        {
            AppUserId = user.Id,
            Issuer = "https://localhost/mock-oidc",
            Subject = subject,
            DisplayName = op.DisplayName,
            Roles = roles.Roles,
            RolesFreshlyResolved = roles.Succeeded,
        };
    }

    public async Task<Guid> PersonIdAsync(string displayName)
    {
        await using var scope = Scope();
        return (await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().Persons.FirstAsync(p => p.DisplayName == displayName)).Id;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort clean-up of the temporary database.
        }
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Ilm.IntegrationTests";

        public string ContentRootPath { get; set; } = root;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public static class Operators
{
    public const string Olivia = FictionalIds.OperatorOlivia;
    public const string Adrian = FictionalIds.ApproverAdrian;
    public const string Sasha = FictionalIds.SecuritySasha;
    public const string Casey = FictionalIds.ConfigCasey;
    public const string Audrey = FictionalIds.AuditorAudrey;
}
