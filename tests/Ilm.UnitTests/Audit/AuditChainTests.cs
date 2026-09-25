using Ilm.Application.Audit;
using Ilm.Domain.Audit;
using Ilm.Infrastructure.Audit;

namespace Ilm.UnitTests.Audit;

public sealed class AuditChainTests
{
    private static readonly StaticAuditKeyProvider Keys = new(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray());

    private static List<AuditRecord> Chain(int count)
    {
        var records = new List<AuditRecord>();
        var previous = AuditChain.GenesisHash;
        for (var i = 1; i <= count; i++)
        {
            var r = new AuditRecord { TimestampUtc = new DateTime(2026, 9, 25, 10, 0, i, DateTimeKind.Utc), Action = "Test" + i, Result = "ok", ActorSubject = "00uTest", TargetStableId = "t" + i };
            AuditChain.Seal(r, i, previous, Keys.CurrentKey);
            previous = r.Hash;
            records.Add(r);
        }

        return records;
    }

    [Fact]
    public void Intact_chain_verifies() => Assert.True(new AuditChainVerifier(Keys).Verify(Chain(5)).IsValid);

    [Fact]
    public void Modified_record_is_detected()
    {
        var chain = Chain(5);
        chain[2].Result = "tampered";
        var result = new AuditChainVerifier(Keys).Verify(chain);
        Assert.False(result.IsValid);
        Assert.Equal(3, result.FirstBrokenSequence);
    }

    [Fact]
    public void Deleted_record_is_detected()
    {
        var chain = Chain(5);
        chain.RemoveAt(2);
        Assert.False(new AuditChainVerifier(Keys).Verify(chain).IsValid);
    }

    [Fact]
    public void Chain_recomputed_without_the_key_is_detected()
    {
        // A DBA rewrites record 2 and recomputes every hash, but cannot produce valid MACs.
        var chain = Chain(4);
        chain[1].Result = "rewritten";
        var forged = new StaticAuditKeyProvider(new byte[32], Keys.CurrentKey.KeyId);
        var previous = chain[0].Hash;
        foreach (var r in chain.Skip(1))
        {
            AuditChain.Seal(r, r.Sequence, previous, forged.CurrentKey);
            previous = r.Hash;
        }

        var result = new AuditChainVerifier(Keys).Verify(chain);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("MAC", StringComparison.Ordinal));
    }

    [Fact]
    public void Divergence_from_off_box_copy_is_detected()
    {
        var chain = Chain(3);
        var sink = chain.ToDictionary(r => r.Sequence, r => new ForwardedAuditEntry(r.Sequence, r.Hash, r.Mac));
        sink[2] = sink[2] with { Hash = new string('0', 64) };
        Assert.False(new AuditChainVerifier(Keys).Verify(chain, sink).IsValid);
    }

    [Fact]
    public void Truncated_tail_is_detected_against_the_sink()
    {
        var chain = Chain(5);
        var sink = chain.ToDictionary(r => r.Sequence, r => new ForwardedAuditEntry(r.Sequence, r.Hash, r.Mac));
        var result = new AuditChainVerifier(Keys).Verify(chain.Take(3), sink);
        Assert.False(result.IsValid);
        Assert.Equal(4, result.FirstBrokenSequence);
    }

    [Theory]
    [InlineData("password", true)]
    [InlineData("unicodePwd", true)]
    [InlineData("client_secret", true)]
    [InlineData("ms-Mcs-AdmPwd", true)]
    [InlineData("department", false)]
    public void Sensitive_property_names_are_recognised(string name, bool sensitive) => Assert.Equal(sensitive, AuditSanitizer.IsSensitiveName(name));

    [Fact]
    public void Secrets_never_reach_audit_values()
    {
        var json = AuditSanitizer.ToSafeJson(new
        {
            password = "Winter2026!",
            nested = new { apiToken = "abc123", note = "Authorization: Bearer eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjM0NSJ9.c2lnbmF0dXJl" },
            okta = "SSWS 00abcdefghijklmnop",
            list = new[] { "pwd=hunter2" },
        })!;
        Assert.DoesNotContain("Winter2026!", json, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", json, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiJ9", json, StringComparison.Ordinal);
        Assert.DoesNotContain("00abcdefghijklmnop", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("+cmd")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tTAB")]
    public void Csv_export_neutralises_formulas(string value)
    {
        var csv = CsvExporter.Export([new AuditRecord { Action = value, Result = "ok", Hash = "h" }]);
        Assert.Contains("\"'" + value.Replace("\"", "\"\"", StringComparison.Ordinal), csv, StringComparison.Ordinal);
    }
}
