using Ilm.Application;
using Ilm.Application.Abstractions;
using Ilm.Application.Reconciliation;
using Ilm.Infrastructure;
using Ilm.Infrastructure.Development;
using Ilm.Modules.Leaver;
using Ilm.Modules.ReadOnly;
using Ilm.Persistence;
using Ilm.Web.Authentication;
using Ilm.Web.Authorization;
using Ilm.Web.Cli;
using Ilm.Web.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var problems = StartupValidator.Validate(builder.Configuration, builder.Environment);
if (problems.Count > 0)
{
    throw new InvalidOperationException("ILM refused to start:\n - " + string.Join("\n - ", problems));
}

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.UseUtcTimestamp = true;
    o.IncludeScopes = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ilm-portal"))
    .WithTracing(t =>
    {
        t.AddSource(IlmTelemetry.Name).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            t.AddOtlpExporter();
        }
    })
    .WithMetrics(m =>
    {
        m.AddMeter(IlmTelemetry.Name).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            m.AddOtlpExporter();
        }
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddIlmApplication();
builder.Services.AddIlmPersistence(builder.Configuration);
builder.Services.AddIlmInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddIlmReadOnlyModule();
builder.Services.AddIlmLeaverModule();
builder.Services.AddIlmAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddIlmAuthorization();
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AllowAnonymousToPage("/Account/SignIn");
    o.Conventions.AllowAnonymousToPage("/Account/SignInFailed");
    o.Conventions.AllowAnonymousToPage("/Account/SignedOut");
    o.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "__Host-ilm-af";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions>(o =>
{
    o.Cookie.Name = "__Host-ilm-td";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.IsEssential = true;
});
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<ConnectorsHealthCheck>("connectors", tags: ["ready"]);
builder.Services.AddSingleton(builder.Configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>() ?? new WorkerOptions());
builder.Services.AddHostedService<IlmBackgroundWorker>();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var proxy in builder.Configuration.GetSection("Ilm:KnownProxies").Get<string[]>() ?? [])
    {
        o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    }
});

var app = builder.Build();

if (CommandRunner.IsCommand(args))
{
    Environment.ExitCode = await CommandRunner.RunAsync(app, args);
    return;
}

await InitialiseAsync(app);

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

if (app.Services.GetService<MockOidcProvider>() is not null)
{
    app.MapMockOidc();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        });
    },
}).AllowAnonymous();
app.MapRazorPages();

await app.RunAsync();

static async Task InitialiseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    var config = app.Configuration;
    if (config.GetValue<bool>("Ilm:Development:MigrateOnStartup"))
    {
        await sp.GetRequiredService<IlmDbContext>().Database.MigrateAsync();
    }

    if (config.GetValue<bool>("Ilm:Development:SeedOnStartup") && (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")))
    {
        await sp.GetRequiredService<DevelopmentSeeder>().SeedAsync(CancellationToken.None);
        await sp.GetRequiredService<ReconciliationService>().RunAsync("startup", ActorContext.System("startup"), CancellationToken.None);
    }

    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    {
        var active = await sp.GetRequiredService<Ilm.Application.Configuration.IActiveConfigurationProvider>().GetAsync(CancellationToken.None);
        if (active.Document.Connectors.Any(c => c.Implementation == "Mock"))
        {
            throw new InvalidOperationException("ILM refused to start: the active configuration uses mock directory connectors in production.");
        }
    }
}

/// <summary>Entry point marker for WebApplicationFactory in tests.</summary>
public partial class Program;
