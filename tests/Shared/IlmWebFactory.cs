using System.Net;
using System.Text.RegularExpressions;
using Ilm.Infrastructure.Development;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ilm.TestSupport;

/// <summary>
/// Hosts the real web application (Testing environment) with SQLite, the mock OIDC provider, the mock Okta org and
/// the fictional directory. Background workers are disabled so tests control time and ordering.
/// </summary>
public sealed class IlmWebFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://localhost/mock-oidc";
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilm-web-" + Guid.NewGuid().ToString("N"));

    public IlmWebFactory()
    {
        Directory.CreateDirectory(root);
    }

    public string Root => root;

    public Dictionary<string, string?> Overrides { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Ilm"] = $"Data Source={Path.Combine(root, "ilm.db")};Pooling=False",
            ["ConnectionStrings:IlmMigration"] = $"Data Source={Path.Combine(root, "ilm.db")};Pooling=False",
            ["Ilm:Database:Provider"] = "Sqlite",
            ["Ilm:Authentication:Mode"] = "MockOidc",
            ["Ilm:Authentication:Issuer"] = Issuer,
            ["Ilm:Authentication:ClientId"] = "ilm-portal-test",
            ["Ilm:Authentication:PublicBaseUrl"] = "https://localhost",
            ["Ilm:Authentication:AllowedRedirectUris:0"] = "https://localhost/signin-oidc",
            ["Ilm:Okta:Mode"] = "Mock",
            ["Ilm:Audit:JsonlSinkPath"] = Path.Combine(root, "audit.jsonl"),
            ["Ilm:Audit:DevelopmentKeyPath"] = Path.Combine(root, "audit.key"),
            ["Ilm:Platform:RuntimeIdentitySids:0"] = FictionalIds.RuntimeGmsaSid,
            ["Ilm:Platform:HostComputerSids:0"] = FictionalIds.PortalHostSid,
            ["Ilm:Platform:HostDnsNames:0"] = FictionalIds.PortalHostDns,
            ["Ilm:Platform:DatabaseServiceIdentitySids:0"] = FictionalIds.DatabaseServiceSid,
            ["Ilm:Platform:BreakGlassSids:0"] = FictionalIds.BreakGlassSid,
            ["Ilm:Platform:ManagedPasswordRetrieverSids:0"] = FictionalIds.GmsaRetrieversGroupSid,
            ["Ilm:Platform:ManagedPasswordRetrieversVerified"] = "true",
            ["Ilm:Sessions:UseDevelopmentMocks"] = "true",
            ["Ilm:Workers:Enabled"] = "false",
            ["Ilm:Development:MigrateOnStartup"] = "true",
            ["Ilm:Development:SeedOnStartup"] = "true",
        };
        foreach (var (k, v) in Overrides)
        {
            settings[k] = v;
        }

        foreach (var (k, v) in settings)
        {
            builder.UseSetting(k, v);
        }
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>Signs in through the full OIDC code + PKCE flow against the in-process mock provider.</summary>
    public async Task<HttpClient> SignInAsync(string subject)
    {
        var client = Browser();
        var challenge = await client.GetAsync("/Account/SignIn");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        var page = await client.GetAsync(authorize);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var form = System.Web.HttpUtility.ParseQueryString(authorize.Query);
        var fields = form.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => form[k]!);
        fields["subject"] = subject;
        var code = await client.PostAsync("/mock-oidc/authorize", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, code.StatusCode);
        var callback = await client.GetAsync(code.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return client;
    }

    public static string Token(string html)
    {
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        Assert.True(m.Success, "antiforgery token not found");
        return m.Groups[1].Value;
    }

    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string pageUrl, string postUrl, Dictionary<string, string> fields)
    {
        var html = await (await client.GetAsync(pageUrl)).Content.ReadAsStringAsync();
        fields["__RequestVerificationToken"] = Token(html);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
