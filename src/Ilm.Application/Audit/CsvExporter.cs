using System.Globalization;
using System.Text;
using Ilm.Domain.Audit;

namespace Ilm.Application.Audit;

/// <summary>
/// Exports audit records as CSV with spreadsheet-formula neutralisation: any cell beginning with
/// =, +, -, @, tab or carriage return is prefixed with an apostrophe.
/// </summary>
public static class CsvExporter
{
    private static readonly string[] Header =
    [
        "Sequence", "TimestampUtc", "EventId", "OperationId", "CorrelationId", "ActorIssuer", "ActorSubject", "EffectiveRoles",
        "Action", "TargetStableId", "Forest", "Domain", "WorkflowState", "Result", "ExceptionCategory", "ConfigurationVersion",
        "SelectedConnector", "SelectedDomainController", "Hash",
    ];

    public static string Export(IEnumerable<AuditRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Header.Select(Cell)));
        foreach (var r in records)
        {
            string?[] row =
            [
                r.Sequence.ToString(CultureInfo.InvariantCulture),
                r.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                r.EventId.ToString("D"),
                r.OperationId?.ToString("D"),
                r.CorrelationId?.ToString("D"),
                r.ActorIssuer,
                r.ActorSubject,
                r.EffectiveRoles,
                r.Action,
                r.TargetStableId,
                r.Forest,
                r.Domain,
                r.WorkflowState,
                r.Result,
                r.ExceptionCategory,
                r.ConfigurationVersion?.ToString(CultureInfo.InvariantCulture),
                r.SelectedConnector,
                r.SelectedDomainController,
                r.Hash,
            ];
            sb.AppendLine(string.Join(",", row.Select(Cell)));
        }

        return sb.ToString();
    }

    public static string Neutralise(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n' ? "'" + value : value;
    }

    private static string Cell(string? value)
    {
        var neutral = AuditSanitizer.SanitizeText(Neutralise(value));
        return "\"" + neutral.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
