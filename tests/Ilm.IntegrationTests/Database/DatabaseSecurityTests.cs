using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Domain.Audit;
using Ilm.IntegrationTests.Support;
using Ilm.Modules.Leaver;
using Ilm.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ilm.IntegrationTests.Database;

public sealed class DatabaseSecurityTests : IAsyncLifetime
{
    private IlmTestHost host = null!;

    public async Task InitializeAsync() => host = await IlmTestHost.CreateAsync();

    public async Task DisposeAsync() => await host.DisposeAsync();

    private async Task<(IReadOnlyList<AuditRecord> Records, AuditVerificationResult Result)> VerifyAsync(bool withSink)
    {
        await using var scope = host.Scope();
        var forwarding = scope.ServiceProvider.GetRequiredService<AuditForwardingService>();
        await forwarding.ForwardPendingAsync(CancellationToken.None);
        var records = await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().AuditRecords.AsNoTracking().OrderBy(r => r.Sequence).ToListAsync();
        var sink = withSink ? await scope.ServiceProvider.GetServices<IAuditSinkReader>().First().ReadAllAsync(CancellationToken.None) : null;
        return (records, scope.ServiceProvider.GetRequiredService<AuditChainVerifier>().Verify(records, sink));
    }

    [Fact]
    public async Task Persisted_audit_chain_verifies_against_the_off_box_copy()
    {
        var (records, result) = await VerifyAsync(withSink: true);
        Assert.True(records.Count >= 3);
        Assert.True(result.IsValid, string.Join("; ", result.Problems));
        Assert.True(File.Exists(host.SinkPath));
    }

    [Fact]
    public async Task Application_cannot_modify_or_delete_audit_records()
    {
        await using var scope = host.Scope();
        var db = scope.ServiceProvider.GetRequiredService<IlmDbContext>();
        var record = await db.AuditRecords.OrderBy(r => r.Sequence).FirstAsync();
        record.Result = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.AuditRecords.Remove(await db.AuditRecords.OrderBy(r => r.Sequence).FirstAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_triggers_block_raw_update_and_delete()
    {
        var olivia = await host.ActorAsync(Operators.Olivia);
        await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(async w => await w.CreateAsync(
            new CreateLeaverCommand(await host.PersonIdAsync("Alex Example"), "Trigger test", "CHG-20002", Domain.Leaver.LeaverUrgency.Urgent, null, "trigger-test"), olivia, CancellationToken.None));
        await using var connection = new SqliteConnection($"Data Source={host.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        foreach (var sql in new[] { "UPDATE \"AuditRecords\" SET \"Result\" = 'x'", "DELETE FROM \"AuditRecords\"", "UPDATE \"LeaverTransitions\" SET \"Reason\" = 'x'", "DELETE FROM \"LeaverTransitions\"" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var ex = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains("append-only", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Dba_who_bypasses_triggers_and_rewrites_history_is_detected()
    {
        await VerifyAsync(withSink: true);
        await using (var connection = new SqliteConnection($"Data Source={host.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TRIGGER \"AuditRecords_no_update\"; UPDATE \"AuditRecords\" SET \"Result\" = 'Rewritten' WHERE \"Sequence\" = 2;";
            await command.ExecuteNonQueryAsync();
        }

        var (_, result) = await VerifyAsync(withSink: true);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.FirstBrokenSequence);
    }

    [Fact]
    public async Task Forwarding_lag_beyond_the_threshold_raises_one_alert()
    {
        await using var scope = host.Scope();
        var sp = scope.ServiceProvider;
        var forwarding = sp.GetRequiredService<AuditForwardingService>();
        var alerts = sp.GetRequiredService<Application.Tasks.AlertService>();
        var db = sp.GetRequiredService<IIlmDbContext>();
        Assert.True(await forwarding.GetLagAsync(CancellationToken.None) > 0);

        Assert.False(await forwarding.AlertOnLagAsync(long.MaxValue, alerts, CancellationToken.None));
        Assert.True(await forwarding.AlertOnLagAsync(0, alerts, CancellationToken.None));
        Assert.False(await forwarding.AlertOnLagAsync(0, alerts, CancellationToken.None));
        Assert.Equal(1, await db.Alerts.CountAsync(a => a.Category == AuditForwardingService.LagAlertCategory));

        await forwarding.ForwardPendingAsync(CancellationToken.None);
        Assert.False(await forwarding.AlertOnLagAsync(0, alerts, CancellationToken.None));
    }

    [Fact]
    public async Task Sensitive_values_are_absent_from_audit_and_the_database()
    {
        const string secret = "S3cr3t-Hunter2-Value";
        var olivia = await host.ActorAsync(Operators.Olivia);
        await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(async w => await w.CreateAsync(
            new CreateLeaverCommand(await host.PersonIdAsync("Alex Example"), $"Leaving; password={secret} token={secret}", "CHG-20001", Domain.Leaver.LeaverUrgency.Urgent, null, "secret-test"), olivia, CancellationToken.None));
        await using var scope = host.Scope();
        var records = await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().AuditRecords.AsNoTracking().ToListAsync();
        Assert.DoesNotContain(records, r => AuditChain.Canonicalize(r).Contains(secret, StringComparison.Ordinal));
        var bytes = await File.ReadAllBytesAsync(host.DatabasePath);
        Assert.DoesNotContain(secret, System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_requires_encrypted_postgresql_connection()
    {
        Assert.NotEmpty(ConnectionSecurityValidator.Validate(DatabaseProvider.Sqlite, "Data Source=x.db", isProduction: true));
        Assert.NotEmpty(ConnectionSecurityValidator.Validate(DatabaseProvider.PostgreSql, "Host=db;Database=ilm;Username=ilm_app", isProduction: true));
        Assert.NotEmpty(ConnectionSecurityValidator.Validate(DatabaseProvider.PostgreSql, "Host=db;Database=ilm;Username=ilm_app;SSL Mode=Require;Trust Server Certificate=true", isProduction: true));
        Assert.NotEmpty(ConnectionSecurityValidator.Validate(DatabaseProvider.PostgreSql, "Host=db;Database=ilm;Username=postgres;SSL Mode=VerifyFull", isProduction: true));
        Assert.Empty(ConnectionSecurityValidator.Validate(DatabaseProvider.PostgreSql, "Host=db.corp.example.test;Database=ilm;Username=ilm_app;SSL Mode=VerifyFull", isProduction: true));
    }
}
