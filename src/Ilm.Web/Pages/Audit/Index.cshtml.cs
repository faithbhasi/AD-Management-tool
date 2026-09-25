using System.Text;
using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Application.Security;
using Ilm.Domain.Audit;
using Ilm.Web.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages.Audit;

[RequirePermission(Permission.ViewAudit)]
public sealed class IndexModel(IIlmDbContext db, AuditChainVerifier verifier, IEnumerable<IAuditSinkReader> sinks, IAuditWriter audit) : IlmPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ActionFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? OperationId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Subject { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Target { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }

    public IReadOnlyList<AuditRecord> Records { get; private set; } = [];

    public AuditVerificationResult? Verification { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => Records = await Query().Take(500).ToListAsync(cancellationToken);

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        if (!Can(Permission.ExportAudit))
        {
            return Forbid();
        }

        var records = await Query().Take(10_000).ToListAsync(cancellationToken);
        audit.Append(new AuditEvent { Action = "AuditExported", Result = "Succeeded", RequestedValues = new { ActionFilter, OperationId, Subject, Target, From, To, count = records.Count } }, Actor);
        await db.SaveChangesAsync(cancellationToken);
        return File(Encoding.UTF8.GetBytes(CsvExporter.Export(records)), "text/csv", "ilm-audit-export.csv");
    }

    public async Task<IActionResult> OnPostVerifyAsync(CancellationToken cancellationToken)
    {
        var all = await db.AuditRecords.AsNoTracking().OrderBy(r => r.Sequence).ToListAsync(cancellationToken);
        var sink = sinks.FirstOrDefault();
        Verification = verifier.Verify(all, sink is null ? null : await sink.ReadAllAsync(cancellationToken));
        Records = await Query().Take(500).ToListAsync(cancellationToken);
        return Page();
    }

    private IQueryable<AuditRecord> Query()
    {
        var q = db.AuditRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(ActionFilter))
        {
            q = q.Where(r => r.Action.Contains(ActionFilter));
        }

        if (OperationId is { } op)
        {
            q = q.Where(r => r.OperationId == op);
        }

        if (!string.IsNullOrWhiteSpace(Subject))
        {
            q = q.Where(r => r.ActorSubject == Subject);
        }

        if (!string.IsNullOrWhiteSpace(Target))
        {
            q = q.Where(r => r.TargetStableId != null && r.TargetStableId.Contains(Target));
        }

        if (From is { } from)
        {
            var f = DateTime.SpecifyKind(from, DateTimeKind.Utc);
            q = q.Where(r => r.TimestampUtc >= f);
        }

        if (To is { } to)
        {
            var t = DateTime.SpecifyKind(to, DateTimeKind.Utc);
            q = q.Where(r => r.TimestampUtc <= t);
        }

        return OperationId is null ? q.OrderByDescending(r => r.Sequence) : q.OrderBy(r => r.Sequence);
    }
}
