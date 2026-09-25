using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Domain.Audit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ilm.Infrastructure.Audit;

/// <summary>
/// Appends each sealed audit record (including its hash and MAC) to a JSON Lines file on a separate volume
/// or share. The file is opened in append mode only. Readable back for chain comparison.
/// </summary>
public sealed class JsonlFileAuditForwarder : IAuditForwarder, IAuditSinkReader
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string path;

    public JsonlFileAuditForwarder(IOptions<AuditOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        var configured = options.Value.JsonlSinkPath ?? throw new InvalidOperationException("No JSONL sink path configured.");
        path = Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public string SinkName => "jsonl-file";

    public async Task ForwardAsync(IReadOnlyList<AuditRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream);
            foreach (var r in records)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(r, IlmJson.Compact).AsMemory(), cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<long, ForwardedAuditEntry>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, ForwardedAuditEntry>();
        if (!File.Exists(path))
        {
            return result;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var sequence = root.GetProperty(nameof(AuditRecord.Sequence)).GetInt64();
            // A batch can be forwarded twice (crash between the sink write and the checkpoint update). The first entry
            // for a sequence is the one the append-only sink received first, so it is authoritative.
            result.TryAdd(sequence, new ForwardedAuditEntry(sequence, root.GetProperty(nameof(AuditRecord.Hash)).GetString()!, root.GetProperty(nameof(AuditRecord.Mac)).GetString()!));
        }

        return result;
    }
}

/// <summary>Posts audit records to a SIEM HTTP collector. The token comes from the environment, never configuration files.</summary>
public sealed class HttpSiemAuditForwarder(IHttpClientFactory httpClients, IOptions<AuditOptions> options) : IAuditForwarder
{
    public string SinkName => "siem-http";

    public async Task ForwardAsync(IReadOnlyList<AuditRecord> records, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var endpoint = o.SiemEndpoint ?? throw new InvalidOperationException("SIEM endpoint not configured.");
        var token = Environment.GetEnvironmentVariable(o.SiemTokenEnvironmentVariable)
            ?? throw new InvalidOperationException($"SIEM token not present in {o.SiemTokenEnvironmentVariable}.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint))
        {
            Content = JsonContent.Create(records.Select(r => new { r.Sequence, r.EventId, r.TimestampUtc, r.Action, r.Result, r.OperationId, r.ActorIssuer, r.ActorSubject, r.TargetStableId, r.Hash, r.Mac, r.PreviousHash, record = r }), options: IlmJson.Compact),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-ILM-Batch-Last-Sequence", records.Count == 0 ? "0" : records[^1].Sequence.ToString(CultureInfo.InvariantCulture));
        using var response = await httpClients.CreateClient("siem").SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
