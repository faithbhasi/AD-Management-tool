using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ilm.Web.Infrastructure;

/// <summary>OpenTelemetry-compatible activity source and metrics. Only identifiers and outcomes are recorded, never personal data.</summary>
public static class IlmTelemetry
{
    public const string Name = "Ilm.Portal";

    public static readonly ActivitySource Activities = new(Name);

    private static readonly Meter Meter = new(Name);

    public static readonly Counter<long> LeaverOperations = Meter.CreateCounter<long>("ilm.leaver.operations", description: "Leaver workflow operations by outcome state.");

    public static readonly Counter<long> ConfigurationOperations = Meter.CreateCounter<long>("ilm.configuration.operations");

    public static readonly Counter<long> AuditForwarded = Meter.CreateCounter<long>("ilm.audit.forwarded");

    public static readonly Counter<long> WorkerRuns = Meter.CreateCounter<long>("ilm.worker.runs");
}
