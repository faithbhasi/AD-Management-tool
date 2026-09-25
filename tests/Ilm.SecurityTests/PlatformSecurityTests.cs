using System.Net;
using System.Text.RegularExpressions;
using Ilm.Application.Abstractions;
using Ilm.Domain.Configuration;
using Ilm.Infrastructure.Development;
using Ilm.TestSupport;
using Ilm.Web.Authentication;
using Ilm.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ilm.SecurityTests;

public sealed class PlatformSecurityTests : IDisposable
{
    private readonly IlmWebFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ilm.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Ilm.Web";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Production_refuses_development_shortcuts()
    {
        var problems = StartupValidator.Validate(Config(new()
        {
            ["Ilm:Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Ilm"] = "Data Source=x.db",
            ["Ilm:Authentication:Mode"] = "MockOidc",
            ["Ilm:Authentication:Issuer"] = "http://localhost/mock-oidc",
            ["Ilm:Authentication:ClientId"] = "x",
            ["Ilm:Authentication:PublicBaseUrl"] = "http://localhost",
            ["Ilm:Okta:Mode"] = "Mock",
            ["Ilm:Sessions:UseDevelopmentMocks"] = "true",
            ["Ilm:Development:SeedOnStartup"] = "true",
        }), new Env("Production"));
        Assert.Contains(problems, p => p.Contains("PostgreSQL", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("mock OIDC", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("HTTPS", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Okta Live", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("session mocks", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("seeding", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("AllowedRedirectUris", StringComparison.Ordinal));
    }

    [Fact]
    public void Mock_oidc_cannot_be_registered_in_production()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddIlmAuthentication(Config(new() { ["Ilm:Authentication:Mode"] = "MockOidc" }), new Env("Production")));
    }

    [Fact]
    public void No_operator_password_form_and_no_bind_password_exist()
    {
        var root = RepoRoot();
        foreach (var page in Directory.EnumerateFiles(Path.Combine(root, "src", "Ilm.Web"), "*.cshtml", SearchOption.AllDirectories))
        {
            Assert.DoesNotMatch(new Regex("type\\s*=\\s*\"?password", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)), File.ReadAllText(page));
        }

        foreach (var source in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("new NetworkCredential", File.ReadAllText(source), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(typeof(IlmAuthenticationOptions).GetProperties(), p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(DirectoryConnectorDefinition).GetProperties(), p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Repository_contains_no_real_secrets_or_real_domains()
    {
        var root = RepoRoot();
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !f.Contains("App_Data", StringComparison.Ordinal)
                && Path.GetExtension(f) is ".cs" or ".json" or ".md" or ".sql" or ".ps1" or ".yml" or ".yaml" or ".config" or ".conf" or ".sh" or ".py" or ".cshtml" or ".props")
            .ToList();
        var secretPatterns = new[]
        {
            new Regex("-----BEGIN (RSA |EC |)PRIVATE KEY-----", RegexOptions.None, TimeSpan.FromSeconds(1)),
            new Regex("SSWS [A-Za-z0-9_-]{30,}", RegexOptions.None, TimeSpan.FromSeconds(1)),
            new Regex("AKIA[0-9A-Z]{16}", RegexOptions.None, TimeSpan.FromSeconds(1)),
            new Regex("xox[bap]-[0-9A-Za-z-]{10,}", RegexOptions.None, TimeSpan.FromSeconds(1)),
            new Regex("(?i)(client_secret|password)\"\\s*:\\s*\"[^\"]{6,}\"", RegexOptions.None, TimeSpan.FromSeconds(1)),
        };
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in secretPatterns)
            {
                Assert.False(pattern.IsMatch(text), $"Possible secret in {Path.GetRelativePath(root, file)}: {pattern}");
            }
        }

        foreach (var settings in Directory.EnumerateFiles(Path.Combine(root, "src", "Ilm.Web"), "appsettings*.json"))
        {
            foreach (Match host in Regex.Matches(File.ReadAllText(settings), "https?://([a-zA-Z0-9.-]+)", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                var name = host.Groups[1].Value;
                Assert.True(name == "localhost" || name.EndsWith("example.test", StringComparison.Ordinal), $"Non-fictional host {name} in {Path.GetFileName(settings)}");
            }
        }
    }

    [Fact]
    public void Database_grants_give_the_application_no_schema_owner_rights()
    {
        var root = RepoRoot();
        var roles = File.ReadAllText(Path.Combine(root, "deploy", "postgres", "00-create-roles-and-database.sql"));
        var grants = File.ReadAllText(Path.Combine(root, "deploy", "postgres", "20-grants-after-migration.sql"));
        Assert.Contains("CREATE ROLE ilm_app LOGIN PASSWORD :'app_password' NOSUPERUSER NOCREATEDB NOCREATEROLE", roles, StringComparison.Ordinal);
        Assert.Contains("AUTHORIZATION ilm_migrator", roles, StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT ALL", grants, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE UPDATE, DELETE, TRUNCATE ON ilm.\"AuditRecords\", ilm.\"LeaverTransitions\" FROM ilm_app", grants, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE", grants.Replace("CREATE TABLE", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        var response = await factory.Browser().GetAsync("/Account/SignedOut");
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Posts_without_antiforgery_token_are_rejected()
    {
        var olivia = await factory.SignInAsync(FictionalIds.OperatorOlivia);
        var response = await olivia.PostAsync("/Reconciliation?handler=Run", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoints_expose_no_details()
    {
        var body = await (await factory.Browser().GetAsync("/health/ready")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("corp.example.test", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Degraded:", body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await factory.Browser().GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Tier0_management_cannot_be_enabled_through_the_ui()
    {
        var casey = await factory.SignInAsync(FictionalIds.ConfigCasey);
        using (var scope = factory.Services.CreateScope())
        {
            var active = await scope.ServiceProvider.GetRequiredService<Application.Configuration.IActiveConfigurationProvider>().GetAsync(CancellationToken.None);
            var doc = Application.Configuration.ConfigurationSerializer.Serialize(active.Document).Replace("\"FeatureFlags\": {", "\"FeatureFlags\": {\n    \"Tier0Management\": true,", StringComparison.Ordinal);
            var response = await IlmWebFactory.PostFormAsync(casey, "/Admin/Configuration", "/Admin/Configuration?handler=Propose", new() { ["Summary"] = "try tier 0", ["DocumentJson"] = doc });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using var check = factory.Services.CreateScope();
        var version = await check.ServiceProvider.GetRequiredService<IIlmDbContext>().ConfigurationVersions.OrderByDescending(v => v.Version).FirstAsync();
        Assert.Equal(ConfigurationVersionStatus.ValidationFailed, version.Status);
        Assert.Contains("FEATURE_UNKNOWN", version.ValidationIssuesJson, StringComparison.Ordinal);

        // The flags form only offers defined features; unknown values are ignored.
        var flags = await IlmWebFactory.PostFormAsync(casey, "/Admin/Flags", "/Admin/Flags", new() { ["enabled"] = "Tier0Management", ["summary"] = "attempt" });
        Assert.Equal(HttpStatusCode.Redirect, flags.StatusCode);
        using var after = factory.Services.CreateScope();
        var latest = await after.ServiceProvider.GetRequiredService<IIlmDbContext>().ConfigurationVersions.OrderByDescending(v => v.Version).FirstAsync();
        Assert.DoesNotContain("Tier0Management", latest.DocumentJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_administrator_cannot_approve_and_security_approver_can()
    {
        var casey = await factory.SignInAsync(FictionalIds.ConfigCasey);
        await IlmWebFactory.PostFormAsync(casey, "/Admin/Protection", "/Admin/Protection", new()
        {
            ["analysisReference"] = "attack-path-test", ["csv"] = "Sid,S-1-5-21-1000000001-1000000002-1000000003-4001,DcSyncRights",
        });
        long version;
        using (var scope = factory.Services.CreateScope())
        {
            version = (await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().ConfigurationVersions.OrderByDescending(v => v.Version).FirstAsync()).Version;
        }

        var caseyTry = await IlmWebFactory.PostFormAsync(casey, "/Admin/Configuration", "/Admin/Configuration?handler=Decide", new() { ["version"] = version.ToString(System.Globalization.CultureInfo.InvariantCulture), ["approve"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, caseyTry.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal(ConfigurationVersionStatus.AwaitingApproval, (await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().ConfigurationVersions.SingleAsync(v => v.Version == version)).Status);
        }

        var sasha = await factory.SignInAsync(FictionalIds.SecuritySasha);
        await IlmWebFactory.PostFormAsync(sasha, "/Admin/Configuration", "/Admin/Configuration?handler=Decide", new() { ["version"] = version.ToString(System.Globalization.CultureInfo.InvariantCulture), ["approve"] = "true" });
        await IlmWebFactory.PostFormAsync(casey, "/Admin/Configuration", "/Admin/Configuration?handler=Activate", new() { ["version"] = version.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        using var check = factory.Services.CreateScope();
        Assert.Equal(ConfigurationVersionStatus.Active, (await check.ServiceProvider.GetRequiredService<IIlmDbContext>().ConfigurationVersions.SingleAsync(v => v.Version == version)).Status);
    }

    [Fact]
    public void Backup_and_dba_risk_are_documented()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "DATABASE-SECURITY.md"));
        foreach (var term in new[] { "Encryption at rest", "TLS", "Least-privilege application identity", "migration identity", "DBA", "Encrypted backups", "Restore testing", "Backup retention", "Row-level", "retention", "export", "tamper-evident", "off-box" })
        {
            Assert.Contains(term, doc, StringComparison.OrdinalIgnoreCase);
        }

        var threat = File.ReadAllText(Path.Combine(RepoRoot(), "THREAT-MODEL.md"));
        Assert.Contains("DBA rewrites audit history", threat, StringComparison.Ordinal);
        Assert.Contains("Backups leak or are restored", threat, StringComparison.Ordinal);
    }
}
