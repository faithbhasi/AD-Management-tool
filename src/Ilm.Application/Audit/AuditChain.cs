using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ilm.Domain.Audit;

namespace Ilm.Application.Audit;

/// <summary>Holds the key used to MAC audit hashes. The key lives outside the database.</summary>
public interface IAuditKeyProvider
{
    AuditKey CurrentKey { get; }

    AuditKey? FindKey(string keyId);
}

public sealed record AuditKey(string KeyId, byte[] Material);

/// <summary>
/// Canonicalisation, hashing and MAC for the audit chain. The canonical form is a JSON array of every
/// field in a fixed order, so any change to any stored value changes the hash.
/// </summary>
public static class AuditChain
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public static string Canonicalize(AuditRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartArray();
            w.WriteNumberValue(r.Sequence);
            w.WriteStringValue(r.EventId.ToString("D"));
            w.WriteStringValue(r.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
            w.WriteStringValue(r.PreviousHash);
            Str(w, r.OperationId?.ToString("D"));
            Str(w, r.CorrelationId?.ToString("D"));
            Str(w, r.IdempotencyKey);
            Str(w, r.ActorIssuer);
            Str(w, r.ActorSubject);
            Str(w, r.AppUserId?.ToString("D"));
            Str(w, r.EffectiveRoles);
            Str(w, r.Action);
            Str(w, r.TargetStableId);
            Str(w, r.Domain);
            Str(w, r.Forest);
            Str(w, r.OuGuid);
            Str(w, r.AuthorityDecision);
            Str(w, r.ProtectionDecision);
            Str(w, r.ScopeDecision);
            Str(w, r.Approval);
            Str(w, r.ConfigurationVersion?.ToString(CultureInfo.InvariantCulture));
            Str(w, r.BeforeValues);
            Str(w, r.RequestedValues);
            Str(w, r.AppliedValues);
            Str(w, r.SelectedConnector);
            Str(w, r.SelectedDomainController);
            Str(w, r.AttemptedActions);
            Str(w, r.VerifiedActions);
            Str(w, r.WorkflowState);
            Str(w, r.Result);
            Str(w, r.DurationMs?.ToString(CultureInfo.InvariantCulture));
            Str(w, r.ExceptionCategory);
            Str(w, r.ReconciliationResults);
            Str(w, r.MacKeyId);
            w.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string ComputeHash(AuditRecord record) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(record))));

    public static string ComputeMac(AuditKey key, string hash)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToHexStringLower(HMACSHA256.HashData(key.Material, Encoding.UTF8.GetBytes(hash)));
    }

    /// <summary>Assigns sequence, previous hash, hash and MAC. Called once, when the record is persisted.</summary>
    public static void Seal(AuditRecord record, long sequence, string previousHash, AuditKey key)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(key);
        record.Sequence = sequence;
        record.PreviousHash = previousHash;
        record.MacKeyId = key.KeyId;
        record.Hash = ComputeHash(record);
        record.Mac = ComputeMac(key, record.Hash);
    }

    private static void Str(Utf8JsonWriter w, string? value)
    {
        if (value is null)
        {
            w.WriteNullValue();
        }
        else
        {
            w.WriteStringValue(value);
        }
    }
}
