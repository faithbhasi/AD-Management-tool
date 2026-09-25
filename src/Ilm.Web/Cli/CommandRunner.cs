using System.Text.Json;
using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Application.Configuration;
using Ilm.Application.Feasibility;
using Ilm.Application.Reconciliation;
using Ilm.Domain.Configuration;
using Ilm.Domain.Feasibility;
using Ilm.Domain.Protection;
using Ilm.Infrastructure.Development;
using Ilm.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Cli;

/// <summary>
/// Operational verbs: migrate (with the separate migration identity), seed-dev, verify-audit,
/// feasibility, check-config and export-dev-config.
/// </summary>
public static class CommandRunner
{
    public static readonly string[] Verbs = ["migrate", "seed-dev", "verify-audit", "feasibility", "check-config", "bootstrap-config", "export-dev-config"];

    public static bool IsCommand(string[] args) => args.Length > 0 && Verbs.Contains(args[0], StringComparer.Ordinal);

    public const string Usage = """
        Usage: Ilm.Web <verb> [options]
          migrate                                   apply migrations with the migration identity
          seed-dev                                  seed fictional data (Development only)
          verify-audit                              verify the audit chain and the forwarded copy
          feasibility --mode mock|pcatest [--output <file>] [--options <json>]
          check-config --file <json>                validate an ILM configuration document
          bootstrap-config --file <json> --change <ticket>
                                                    create configuration version 1 (only when none exists)
          export-dev-config [--output <file>]       write the fictional bootstrap configuration
        """;

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            return await RunCoreAsync(app, args);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            await Console.Error.WriteLineAsync($"{args[0]}: {ex.Message}");
            await Console.Error.WriteLineAsync(Usage);
            return 2;
        }
    }

    private static async Task<int> RunCoreAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var ct = CancellationToken.None;
        switch (args[0])
        {
            case "migrate":
                await using (var migrationContext = PersistenceServiceCollectionExtensions.CreateMigrationContext(app.Configuration, sp.GetRequiredService<IAuditKeyProvider>()))
                {
                    await migrationContext.Database.MigrateAsync(ct);
                }

                Console.WriteLine("Migrations applied with the migration identity.");
                return 0;

            case "seed-dev":
                if (!app.Environment.IsDevelopment())
                {
                    Console.Error.WriteLine("seed-dev is only permitted in the Development environment.");
                    return 2;
                }

                await sp.GetRequiredService<DevelopmentSeeder>().SeedAsync(ct);
                await sp.GetRequiredService<ReconciliationService>().RunAsync("seed-dev", ActorContext.System("seed-dev"), ct);
                Console.WriteLine("Fictional development data seeded and reconciled.");
                return 0;

            case "verify-audit":
                var db = sp.GetRequiredService<IIlmDbContext>();
                var sink = sp.GetServices<IAuditSinkReader>().FirstOrDefault();
                var sinkCopy = sink is null ? null : await sink.ReadAllAsync(ct);
                var records = await db.AuditRecords.AsNoTracking().OrderBy(r => r.Sequence).ToListAsync(ct);
                var result = sp.GetRequiredService<AuditChainVerifier>().Verify(records, sinkCopy);
                Console.WriteLine(result.IsValid ? $"Audit chain valid: {result.RecordsChecked} record(s)." : $"AUDIT CHAIN BROKEN at #{result.FirstBrokenSequence}:");
                foreach (var p in result.Problems.Take(50))
                {
                    Console.WriteLine("  " + p);
                }

                return result.IsValid ? 0 : 3;

            case "feasibility":
                return await RunFeasibilityAsync(sp, args, ct);

            case "check-config":
                var path = Arg(args, "--file") ?? throw new ArgumentException("--file is required.");
                var document = ConfigurationSerializer.Deserialize(await File.ReadAllTextAsync(path, ct));
                var issues = ConfigurationValidator.Validate(document, new PlatformIdentities { Complete = true }, DateTime.UtcNow);
                foreach (var issue in issues)
                {
                    Console.WriteLine($"{issue.Code}: {issue.Message}");
                }

                Console.WriteLine(issues.Count == 0 ? "Configuration is valid." : $"{issues.Count} issue(s).");
                return issues.Count == 0 ? 0 : 4;

            case "bootstrap-config":
                var bootstrapPath = Arg(args, "--file") ?? throw new ArgumentException("--file is required.");
                var change = Arg(args, "--change") ?? throw new ArgumentException("--change <ticket> is required.");
                var bootstrapJson = await File.ReadAllTextAsync(bootstrapPath, ct);
                var source = $"change {change}, sha256 {Hashing.Sha256Hex(bootstrapJson)}";
                try
                {
                    var created = await sp.GetRequiredService<ConfigurationService>().BootstrapAsync(ConfigurationSerializer.Deserialize(bootstrapJson), source, ct);
                    Console.WriteLine(created
                        ? $"Configuration version 1 created and active ({source})."
                        : "A configuration already exists. Changes go through propose, approve and activate in the portal.");
                    return created ? 0 : 5;
                }
                catch (Domain.Common.DomainException ex) when (ex.Category == Domain.Common.SafeErrorCategory.ValidationFailed)
                {
                    await Console.Error.WriteLineAsync(ex.Message);
                    return 4;
                }

            case "export-dev-config":
                var output = Arg(args, "--output") ?? "config/bootstrap.development.json";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output, ConfigurationSerializer.Serialize(FictionalConfiguration.Bootstrap()), ct);
                Console.WriteLine($"Wrote {output}.");
                return 0;
        }

        return 1;
    }

    private static async Task<int> RunFeasibilityAsync(IServiceProvider sp, string[] args, CancellationToken ct)
    {
        var mode = string.Equals(Arg(args, "--mode"), "pcatest", StringComparison.OrdinalIgnoreCase) ? FeasibilityMode.Pcatest : FeasibilityMode.Mock;
        var output = Arg(args, "--output") ?? "OKTA-AD-FEASIBILITY-REPORT.md";
        FeasibilityOptions options;
        if (Arg(args, "--options") is { } optionsPath)
        {
            options = JsonSerializer.Deserialize<FeasibilityOptions>(await File.ReadAllTextAsync(optionsPath, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new ArgumentException("Invalid options file.");
        }
        else if (mode == FeasibilityMode.Mock)
        {
            options = MockFeasibilityOptions();
        }
        else
        {
            Console.Error.WriteLine("A PCATEST run needs --options <file> describing the tested org, integration and assignment.");
            return 2;
        }

        var service = sp.GetRequiredService<FeasibilityService>();
        var run = await service.RunAsync(options, mode, ActorContext.System("feasibility-cli"), ct);
        await File.WriteAllTextAsync(output, run.ReportMarkdown, ct);
        Console.WriteLine($"Feasibility run {run.Id:D}: {FeasibilityReportGenerator.Label(run.Recommendation)}. Report written to {output} (sha256 {run.ReportSha256}).");
        return 0;
    }

    public static FeasibilityOptions MockFeasibilityOptions() => new()
    {
        Environment = "Mock (in-process, not PCATEST)",
        OktaOrg = "https://okta.example.test (mock)",
        AdIntegration = "Mock Okta AD Agent → corp.example.test",
        AssignmentMechanism = "Group",
        AssignmentId = FictionalIds.GroupAdProvisioning,
        TargetConnectorId = FictionalIds.TargetConnector,
        ExpectedOuDistinguishedName = "OU=Finance,OU=Staff,OU=ILM Managed,DC=corp,DC=example,DC=test",
        SyntheticLoginPrefix = "ilmfeas-",
        SyntheticEmailDomain = "example.test",
        Department = "Finance",
        SourceProfileSettings = "Mock: Okta is the profile source for pushed attributes (simulated).",
        PushMappings = "Mock: login→userPrincipalName, email→mail and proxyAddresses, department→department, managerId→manager (simulated).",
        PasswordBehaviourNotes = "Mock: no password is pushed; initial password behaviour must be observed in PCATEST.",
        LicensingNotes = "Mock: none. Licensing dependencies (for example Okta Workflows or Lifecycle Management) must be confirmed for PCATEST.",
        PollInterval = TimeSpan.Zero,
        MaxPolls = 6,
    };

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
