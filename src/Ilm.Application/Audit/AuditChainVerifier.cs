using Ilm.Domain.Audit;

namespace Ilm.Application.Audit;

public sealed record AuditVerificationResult(bool IsValid, long RecordsChecked, long? FirstBrokenSequence, IReadOnlyList<string> Problems);

/// <summary>A record as held by an off-box sink, used to detect database rewrites.</summary>
public sealed record ForwardedAuditEntry(long Sequence, string Hash, string Mac);

/// <summary>
/// Verifies the audit chain: contiguous sequences, correct previous-hash links, recomputed hashes, valid MACs,
/// and (optionally) agreement with the forwarded copy held outside the database.
/// </summary>
public sealed class AuditChainVerifier(IAuditKeyProvider keys)
{
    public AuditVerificationResult Verify(IEnumerable<AuditRecord> orderedRecords, IReadOnlyDictionary<long, ForwardedAuditEntry>? sinkCopy = null)
    {
        ArgumentNullException.ThrowIfNull(orderedRecords);
        var problems = new List<string>();
        long? firstBroken = null;
        long expectedSequence = 1;
        var previousHash = AuditChain.GenesisHash;
        long count = 0;
        long lastSequence = 0;

        foreach (var r in orderedRecords)
        {
            count++;
            lastSequence = r.Sequence;

            void Fail(string message)
            {
                problems.Add($"#{r.Sequence}: {message}");
                firstBroken ??= r.Sequence;
            }

            if (r.Sequence != expectedSequence)
            {
                Fail($"sequence gap (expected {expectedSequence}); records were deleted or inserted.");
            }

            if (!string.Equals(r.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                Fail("previous-hash link broken.");
            }

            var recomputed = AuditChain.ComputeHash(r);
            if (!string.Equals(recomputed, r.Hash, StringComparison.Ordinal))
            {
                Fail("content does not match its hash; the record was modified.");
            }

            var key = keys.FindKey(r.MacKeyId);
            if (key is null)
            {
                Fail($"MAC key '{r.MacKeyId}' is not available; authenticity cannot be confirmed.");
            }
            else if (!string.Equals(AuditChain.ComputeMac(key, r.Hash), r.Mac, StringComparison.Ordinal))
            {
                Fail("MAC is invalid; the chain was recomputed without the audit key.");
            }

            if (sinkCopy is not null && sinkCopy.TryGetValue(r.Sequence, out var forwarded)
                && (!string.Equals(forwarded.Hash, r.Hash, StringComparison.Ordinal) || !string.Equals(forwarded.Mac, r.Mac, StringComparison.Ordinal)))
            {
                Fail("differs from the copy forwarded off-box.");
            }

            previousHash = r.Hash;
            expectedSequence = r.Sequence + 1;
        }

        if (sinkCopy is not null)
        {
            foreach (var forwarded in sinkCopy.Values.Where(f => f.Sequence > lastSequence).OrderBy(f => f.Sequence).Take(1))
            {
                problems.Add($"#{forwarded.Sequence}: present in the off-box sink but missing from the database (tail truncated or restored from an older backup).");
                firstBroken ??= forwarded.Sequence;
            }
        }

        return new AuditVerificationResult(problems.Count == 0, count, firstBroken, problems);
    }
}
