using Ilm.Application.Abstractions;
using Ilm.Domain.Approvals;
using Ilm.Domain.Common;
using Ilm.Domain.Leaver;
using Ilm.Domain.Tasks;
using Ilm.Infrastructure.Development;
using Ilm.IntegrationTests.Support;
using Ilm.Modules.Leaver;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ilm.IntegrationTests.Leaver;

/// <summary>Leaver scenarios from the specification, through the real services, database and mocks.</summary>
public sealed class LeaverScenarioTests : IAsyncLifetime
{
    private IlmTestHost host = null!;
    private ActorContext olivia = null!;
    private ActorContext adrian = null!;
    private ActorContext sasha = null!;

    public async Task InitializeAsync()
    {
        host = await IlmTestHost.CreateAsync();
        olivia = await host.ActorAsync(Operators.Olivia);
        adrian = await host.ActorAsync(Operators.Adrian);
        sasha = await host.ActorAsync(Operators.Sasha);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private Task<LeaverOperationResult> Create(string person, string key, LeaverUrgency urgency = LeaverUrgency.Urgent) =>
        host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(async w =>
            await w.CreateAsync(new CreateLeaverCommand(await host.PersonIdAsync(person), "Fictional leaver", "CHG-10001", urgency, null, key), olivia, CancellationToken.None));

    private Task<LeaverOperationResult> Approve(Guid id, ActorContext approver) =>
        host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w => w.DecideAsync(id, true, "ok", approver, CancellationToken.None));

    private Task<LeaverOperationResult> Start(Guid id) =>
        host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w => w.StartContainmentAsync(id, olivia, CancellationToken.None));

    private Task<LeaverOperationResult> Reverify(Guid id) =>
        host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w => w.ReverifyAsync(id, olivia, CancellationToken.None));

    private async Task<LeaverRequest> Run(string person, ActorContext approver)
    {
        var created = await Create(person, "k-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(LeaverState.AwaitingApproval, created.Request.State);
        await Approve(created.Request.Id, approver);
        return (await Start(created.Request.Id)).Request;
    }

    private async Task<List<LeaverTransition>> History(Guid id)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().LeaverTransitions.Where(t => t.LeaverRequestId == id).OrderBy(t => t.Ordinal).ToListAsync();
    }

    private async Task<List<ManualTask>> Tasks(Guid id)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().ManualTasks.Where(t => t.LeaverRequestId == id).ToListAsync();
    }

    private bool AdDisabled(string connector, string sam) =>
        (host.Directory.Forest(connector).Objects[FictionalIds.For($"{connector}:user:{sam}")].UserAccountControl & 2) != 0;

    [Fact]
    public async Task Okta_authoritative_user_is_contained_through_okta_and_ad_is_verified_not_written()
    {
        var writesBefore = host.Directory.Forest(FictionalIds.TargetConnector).WriteCount;
        var request = await Run("Alex Example", adrian);
        Assert.Equal(LeaverState.SafelyContained, request.State);
        Assert.Equal("DEPROVISIONED", host.Okta.Users[FictionalIds.OktaAlex].Status);
        Assert.Equal(0, host.Okta.Users[FictionalIds.OktaAlex].ActiveSessions);
        Assert.True(AdDisabled(FictionalIds.TargetConnector, "alex.example"));
        Assert.Equal(writesBefore, host.Directory.Forest(FictionalIds.TargetConnector).WriteCount);
        Assert.Contains(request.Actions, a => a.Method == ContainmentMethod.VerifyOnly && a.Status == ContainmentActionStatus.Verified);

        var states = (await History(request.Id)).Select(t => t.NextState).ToList();
        Assert.Equal(
            [LeaverState.Validated, LeaverState.AwaitingApproval, LeaverState.Approved, LeaverState.ContainmentStarted, LeaverState.AuthenticationContained,
             LeaverState.SessionsRevoked, LeaverState.DirectoryAccountDisabled, LeaverState.ContainmentVerificationPending, LeaverState.SafelyContained],
            states);
    }

    [Fact]
    public async Task Every_transition_records_the_required_fields()
    {
        var request = await Run("Alex Example", adrian);
        foreach (var t in await History(request.Id))
        {
            Assert.Equal(request.Id, t.OperationId);
            Assert.NotEqual(Guid.Empty, t.CorrelationId);
            Assert.False(string.IsNullOrEmpty(t.IdempotencyKey));
            Assert.False(string.IsNullOrEmpty(t.Actor));
            Assert.Equal("CHG-10001", t.TicketReference);
            Assert.False(string.IsNullOrEmpty(t.Reason));
            Assert.True(t.ConfigurationVersion >= 1);
        }

        var safe = (await History(request.Id)).Single(t => t.NextState == LeaverState.SafelyContained);
        Assert.Contains("Okta", safe.AuthorityDecision, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(safe.ActionsVerified));
        // Only best-effort session systems (not integrated: VPN, application sessions) may remain open.
        Assert.All(safe.UnresolvedActions.Split(" | ", StringSplitOptions.RemoveEmptyEntries), u => Assert.True(u.Contains("Vpn", StringComparison.Ordinal) || u.Contains("Application", StringComparison.Ordinal), u));
        Assert.Contains(await History(request.Id), t => t.NextState == LeaverState.Approved && t.Approver != null);
    }

    [Fact]
    public async Task Legacy_ad_authoritative_user_without_target_identity_is_contained_by_containment_only_legacy()
    {
        var created = await Create("Casey Placeholder", "casey-1");
        await Assert.ThrowsAsync<DomainException>(() => Approve(created.Request.Id, adrian));
        await Approve(created.Request.Id, sasha);
        var request = (await Start(created.Request.Id)).Request;
        Assert.Equal(LeaverState.SafelyContained, request.State);
        Assert.True(AdDisabled(FictionalIds.LegacyAConnector, "casey.placeholder"));
        var action = Assert.Single(request.Actions, a => a.Method == ContainmentMethod.ContainmentOnlyLegacy);
        Assert.Equal(ContainmentStep.AuthenticationContainment, action.Step);
        Assert.Equal("lgadc01.legacy-a.example.test", action.SelectedDomainController);
        Assert.Equal(ContainmentActionStatus.Verified, action.Status);
    }

    [Fact]
    public async Task Target_ad_authoritative_user_falls_back_to_manual_while_direct_ad_is_disabled()
    {
        var request = await Run("Dana Fictional", adrian);
        Assert.Equal(LeaverState.ManualContainmentRequired, request.State);
        Assert.False(AdDisabled(FictionalIds.TargetConnector, "dana.fictional"));
        var tasks = await Tasks(request.Id);
        Assert.Contains(tasks, t => t.Kind == ManualTaskKind.ManualContainment && t.SlaDueUtc != null && t.RunbookMarkdown.Contains("dana.fictional", StringComparison.Ordinal));

        // A Tier 1 operator disables the accounts manually; ILM observes the result and closes the tasks.
        foreach (var sam in new[] { "dana.fictional", "adm-dana.fictional" })
        {
            host.Directory.Forest(FictionalIds.TargetConnector).Objects[FictionalIds.For($"corp:user:{sam}")].UserAccountControl |= 2;
        }

        // Recording one task triggers re-verification, which observes both accounts and closes both tasks.
        var first = tasks.First(t => t.Kind == ManualTaskKind.ManualContainment);
        await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w => w.RecordManualTaskAsync(first.Id, "CHG-10002 disabled via ADUC", olivia, CancellationToken.None));
        Assert.All((await Tasks(request.Id)).Where(t => t.Kind == ManualTaskKind.ManualContainment), t => Assert.Equal(ManualTaskStatus.Completed, t.Status));

        var after = await host.WithAsync<LeaverWorkflowService, LeaverRequest>(w => w.LoadAsync(request.Id, CancellationToken.None));
        Assert.Equal(LeaverState.SafelyContained, after.State);
    }

    [Fact]
    public async Task Unresolved_authority_creates_manual_runbook_alert_and_sla()
    {
        var request = await Run("Jordan Unmapped", adrian);
        Assert.Equal(LeaverState.ManualContainmentRequired, request.State);
        var action = Assert.Single(request.Actions, a => a.System == SystemKind.ActiveDirectory);
        Assert.Contains("Missing", action.AuthorityDecision, StringComparison.Ordinal);
        var task = Assert.Single(await Tasks(request.Id), t => t.ContainmentActionId == action.Id);
        Assert.NotNull(task.SlaDueUtc);
        Assert.Contains("Authority unresolved", task.RunbookMarkdown, StringComparison.Ordinal);
        await using var scope = host.Scope();
        Assert.Contains(await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().Alerts.ToListAsync(), a => a.OperationId == request.Id && a.Severity >= AlertSeverity.High);
    }

    [Fact]
    public async Task Two_identities_during_coexistence_are_both_contained()
    {
        var request = await Run("Bailey Sample", sasha);
        Assert.Equal(LeaverState.SafelyContained, request.State);
        Assert.True(AdDisabled(FictionalIds.TargetConnector, "bailey.sample"));
        Assert.True(AdDisabled(FictionalIds.LegacyAConnector, "bailey.sample"));
        Assert.Equal("DEPROVISIONED", host.Okta.Users[FictionalIds.OktaBailey].Status);
    }

    [Fact]
    public async Task Already_disabled_account_is_verified_without_a_write()
    {
        var before = host.Directory.Forest(FictionalIds.TargetConnector).WriteCount;
        var request = await Run("Finley Demo", adrian);
        Assert.Equal(LeaverState.SafelyContained, request.State);
        Assert.Equal(before, host.Directory.Forest(FictionalIds.TargetConnector).WriteCount);
    }

    [Theory]
    [InlineData("Kai Privileged")]
    [InlineData("Lee Nested")]
    [InlineData("Emerson Test")]
    [InlineData("Morgan Unknown")]
    public async Task Protected_or_unknown_accounts_are_never_written_and_get_tier0_tasks(string person)
    {
        var writes = host.Directory.Forests.Sum(f => f.WriteCount);
        var request = await Run(person, sasha);
        Assert.Equal(LeaverState.ManualContainmentRequired, request.State);
        Assert.Equal(writes, host.Directory.Forests.Sum(f => f.WriteCount));
        Assert.Contains(await Tasks(request.Id), t => t.Kind == ManualTaskKind.Tier0Containment && t.Severity == AlertSeverity.Critical);
    }

    [Fact]
    public async Task Session_revocation_failure_requires_manual_action_and_blocks_safely_contained()
    {
        host.Session(SessionSystem.Citrix).NextStatus = SessionRevocationStatus.Failed;
        var request = await Run("Alex Example", adrian);
        Assert.Equal(LeaverState.ManualContainmentRequired, request.State);
        Assert.Contains(request.Actions, a => a.SessionSystem == SessionSystem.Citrix && a.Status == ContainmentActionStatus.Failed && a.ManualTaskId != null);
    }

    [Fact]
    public async Task Okta_session_revocation_failure_is_not_silently_ignored()
    {
        host.Okta.FailSessionRevocation = true;
        var request = await Run("Harper Synthetic", adrian);
        Assert.NotEqual(LeaverState.SafelyContained, request.State);
        Assert.Contains(request.Actions, a => a.SessionSystem == SessionSystem.Okta && a.ManualTaskId != null);
    }

    [Fact]
    public async Task Ad_disable_failure_falls_back_to_manual_then_verifies()
    {
        host.Directory.Forest(FictionalIds.LegacyAConnector).FailNextWrites = 1;
        var created = await Create("Casey Placeholder", "casey-fail");
        await Approve(created.Request.Id, sasha);
        var request = (await Start(created.Request.Id)).Request;
        Assert.Equal(LeaverState.ManualContainmentRequired, request.State);
        Assert.Contains(request.Actions, a => a.Method == ContainmentMethod.ContainmentOnlyLegacy && a.Status == ContainmentActionStatus.Failed && a.ErrorCategory == SafeErrorCategory.InsufficientDirectoryRights && a.ManualTaskId != null);

        host.Directory.Forest(FictionalIds.LegacyAConnector).Objects[FictionalIds.For("legacy-a:user:casey.placeholder")].UserAccountControl |= 2;
        Assert.Equal(LeaverState.SafelyContained, (await Reverify(request.Id)).Request.State);
    }

    [Fact]
    public async Task Failure_before_any_change_is_recorded_then_falls_back_to_manual()
    {
        host.Okta.ApiAvailable = false;
        host.Session(SessionSystem.Entra).NextStatus = SessionRevocationStatus.Failed;
        host.Session(SessionSystem.Citrix).NextStatus = SessionRevocationStatus.Failed;
        var created = await Create("Alex Example", "outage-1");
        await Approve(created.Request.Id, adrian);
        var request = (await Start(created.Request.Id)).Request;
        var states = (await History(request.Id)).Select(t => t.NextState).ToList();
        Assert.Contains(LeaverState.FailedBeforeChange, states);
        Assert.Equal(LeaverState.ManualContainmentRequired, request.State);
        Assert.Contains(await Tasks(request.Id), t => t.Title.Contains("Okta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Change_that_cannot_be_verified_is_partially_contained()
    {
        // Simulate an out-of-band re-enable immediately after ILM's write: the write happened but verification fails.
        var forest = host.Directory.Forest(FictionalIds.LegacyAConnector);
        var created = await Create("Casey Placeholder", "casey-partial");
        await Approve(created.Request.Id, sasha);
        host.Directory.RegisterReadHook(() =>
        {
            var casey = forest.Objects[FictionalIds.For("legacy-a:user:casey.placeholder")];
            if (forest.WriteCount > 0)
            {
                casey.UserAccountControl = 512;
            }
        });
        var request = (await Start(created.Request.Id)).Request;
        Assert.Equal(LeaverState.PartiallyContained, request.State);
    }

    [Fact]
    public async Task Provisional_link_prevents_safely_contained_until_rejected()
    {
        var request = await Run("Harper Synthetic", adrian);
        Assert.Equal(LeaverState.ManualContainmentRequired, request.State);
        Assert.Contains(await Tasks(request.Id), t => t.Kind == ManualTaskKind.IdentityLinkConfirmation);

        await using (var scope = host.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIlmDbContext>();
            var link = await db.IdentityLinks.FirstAsync(l => l.Confidence == Domain.Identity.LinkConfidence.Provisional);
            await scope.ServiceProvider.GetRequiredService<Application.Identity.IdentityLinkService>().RejectAsync(link.Id, "Different person", adrian, CancellationToken.None);
        }

        Assert.Equal(LeaverState.SafelyContained, (await Reverify(request.Id)).Request.State);
    }

    [Fact]
    public async Task Provisional_link_approved_after_approval_becomes_a_verified_manual_containment()
    {
        var request = await Run("Harper Synthetic", adrian);
        Assert.Equal(LeaverState.ManualContainmentRequired, request.State);
        var legacyWrites = host.Directory.Forest(FictionalIds.LegacyBConnector).WriteCount;

        await using (var scope = host.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIlmDbContext>();
            var link = await db.IdentityLinks.FirstAsync(l => l.Confidence == Domain.Identity.LinkConfidence.Provisional);
            await scope.ServiceProvider.GetRequiredService<Application.Identity.IdentityLinkService>().ApproveAsync(link.Id, adrian, CancellationToken.None);
        }

        // The approved plan did not include the account: ILM adds a manual task for it and never writes to it itself.
        var afterApproval = (await Reverify(request.Id)).Request;
        Assert.Equal(LeaverState.ManualContainmentRequired, afterApproval.State);
        var late = Assert.Single(afterApproval.Actions, a => a.IdempotencyKey.Contains(":late:", StringComparison.Ordinal));
        Assert.Equal(ContainmentMethod.ManualControlled, late.Method);
        var task = Assert.Single(await Tasks(request.Id), t => t.ContainmentActionId == late.Id);
        Assert.Equal(ManualTaskKind.ManualContainment, task.Kind);
        Assert.Equal(legacyWrites, host.Directory.Forest(FictionalIds.LegacyBConnector).WriteCount);

        // Recording the task is not enough: ILM reads the directory, and the account is still enabled.
        var recorded = await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w => w.RecordManualTaskAsync(task.Id, "Disabled from the legacy console (CHG-10009)", olivia, CancellationToken.None));
        Assert.NotEqual(LeaverState.SafelyContained, recorded.Request.State);

        // The operator's change becomes visible in the directory; verification now passes.
        host.Directory.Forest(FictionalIds.LegacyBConnector).Objects[FictionalIds.For($"{FictionalIds.LegacyBConnector}:user:hsynthetic")].UserAccountControl |= 2;
        Assert.Equal(LeaverState.SafelyContained, (await Reverify(request.Id)).Request.State);
    }

    [Fact]
    public async Task Account_without_a_link_record_blocks_safely_contained_until_resolved()
    {
        Guid orphanId;
        await using (var scope = host.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIlmDbContext>();
            var person = await db.Persons.FirstAsync(p => p.DisplayName == "Finley Demo");
            var orphan = new Domain.Identity.ExternalIdentity
            {
                PersonId = person.Id,
                System = SystemKind.ActiveDirectory,
                ForestOrTenantId = "legacy-b.example.test",
                ConnectorId = FictionalIds.LegacyBConnector,
                StableObjectId = FictionalIds.For("orphan-account").ToString("D"),
                ObjectGuid = FictionalIds.For("orphan-account"),
                SamAccountName = "fdemo-old",
                Population = "legacy-b",
            };
            db.ExternalIdentities.Add(orphan);
            await db.SaveChangesAsync();
            orphanId = orphan.Id;
        }

        var request = await Run("Finley Demo", adrian);
        Assert.Equal(LeaverState.ManualContainmentRequired, request.State);
        var task = Assert.Single(await Tasks(request.Id), t => t.Kind == ManualTaskKind.IdentityLinkConfirmation);
        Assert.Contains("no link record", task.RunbookMarkdown, StringComparison.Ordinal);

        await using (var scope = host.Scope())
        {
            var links = scope.ServiceProvider.GetRequiredService<Application.Identity.IdentityLinkService>();
            var personId = await host.PersonIdAsync("Finley Demo");
            var link = await links.ProposeAsync(personId, orphanId, Domain.Identity.EvidenceType.EmailAttribute, Domain.Identity.LinkMethod.EmailMatch, "CHG-10010", 1, olivia, CancellationToken.None);
            Assert.Equal(Domain.Identity.LinkConfidence.Provisional, link.Confidence);
            Assert.Equal(LeaverState.ManualContainmentRequired, (await Reverify(request.Id)).Request.State);
            await links.RejectAsync(link.Id, "Belongs to someone else", adrian, CancellationToken.None);
        }

        Assert.Equal(LeaverState.SafelyContained, (await Reverify(request.Id)).Request.State);
    }

    [Fact]
    public async Task Repeated_idempotency_key_returns_the_same_request_and_conflicting_payload_is_refused()
    {
        var first = await Create("Alex Example", "same-key");
        var second = await Create("Alex Example", "same-key");
        Assert.Equal(first.Request.Id, second.Request.Id);
        Assert.False(second.Changed);
        var ex = await Assert.ThrowsAsync<DomainException>(() => host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(async w =>
            await w.CreateAsync(new CreateLeaverCommand(await host.PersonIdAsync("Alex Example"), "Different reason", "CHG-10001", LeaverUrgency.Urgent, null, "same-key"), olivia, CancellationToken.None)));
        Assert.Equal(SafeErrorCategory.IdempotencyConflict, ex.Category);
    }

    [Fact]
    public async Task Concurrent_requests_for_one_person_produce_exactly_one_active_leaver()
    {
        var personId = await host.PersonIdAsync("Gray Mock");
        var attempts = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            try
            {
                await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w => w.CreateAsync(new CreateLeaverCommand(personId, "Concurrent", "CHG-10003", LeaverUrgency.Urgent, null, "concurrent-" + i), olivia, CancellationToken.None));
                return true;
            }
            catch (DomainException)
            {
                return false;
            }
        })).ToList();
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(r => r));
        await using var scope = host.Scope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().LeaverRequests.CountAsync(r => r.PersonId == personId && r.IsActive));
    }

    [Fact]
    public async Task Expired_approval_cannot_be_used()
    {
        var created = await Create("Alex Example", "expire-1");
        host.Time.Advance(TimeSpan.FromHours(25));
        var ex = await Assert.ThrowsAsync<DomainException>(() => Approve(created.Request.Id, adrian));
        Assert.Equal(SafeErrorCategory.ApprovalInvalid, ex.Category);
        await using var scope = host.Scope();
        Assert.Equal(ApprovalStatus.Expired, (await scope.ServiceProvider.GetRequiredService<IIlmDbContext>().Approvals.FirstAsync(a => a.Id == created.Request.ApprovalId)).Status);
    }

    [Fact]
    public async Task Changed_plan_invalidates_prior_approval()
    {
        var created = await Create("Harper Synthetic", "plan-change");
        var originalApproval = created.Request.ApprovalId;
        await using (var scope = host.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIlmDbContext>();
            var link = await db.IdentityLinks.FirstAsync(l => l.Confidence == Domain.Identity.LinkConfidence.Provisional);
            await scope.ServiceProvider.GetRequiredService<Application.Identity.IdentityLinkService>().ApproveAsync(link.Id, sasha, CancellationToken.None);
        }

        var result = await Approve(created.Request.Id, adrian);
        Assert.Equal(LeaverState.AwaitingApproval, result.Request.State);
        Assert.NotEqual(originalApproval, result.Request.ApprovalId);
        await using var check = host.Scope();
        Assert.Equal(ApprovalStatus.Invalidated, (await check.ServiceProvider.GetRequiredService<IIlmDbContext>().Approvals.FirstAsync(a => a.Id == originalApproval)).Status);
    }

    [Fact]
    public async Task Requester_cannot_approve_own_request()
    {
        var created = await Create("Alex Example", "self-approve");
        var operatorWhoAlsoApproves = olivia with { Roles = new Dictionary<Domain.Security.AppRole, IReadOnlyCollection<string>>(olivia.Roles) { [Domain.Security.AppRole.LifecycleApprover] = ["corp-managed"] } };
        var ex = await Assert.ThrowsAsync<DomainException>(() => Approve(created.Request.Id, operatorWhoAlsoApproves));
        Assert.Equal(SafeErrorCategory.NotAuthorised, ex.Category);
    }

    [Fact]
    public async Task Containment_request_never_disappears_when_validation_fails()
    {
        var result = await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(async w =>
            await w.CreateAsync(new CreateLeaverCommand(await host.PersonIdAsync("Alex Example"), "x", "!", LeaverUrgency.Urgent, null, "bad-ticket"), olivia, CancellationToken.None));
        Assert.Equal(LeaverState.ValidationFailed, result.Request.State);
        await using var scope = host.Scope();
        var db = scope.ServiceProvider.GetRequiredService<IIlmDbContext>();
        Assert.True(await db.LeaverRequests.AnyAsync(r => r.Id == result.Request.Id && r.IsActive));
        Assert.Contains(await db.Alerts.ToListAsync(), a => a.OperationId == result.Request.Id);
    }

    [Fact]
    public async Task Scheduled_leaver_starts_when_due_and_contained_account_re_enabled_is_escalated()
    {
        var personId = await host.PersonIdAsync("Casey Placeholder");
        var created = await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w =>
            w.CreateAsync(new CreateLeaverCommand(personId, "Planned leaver", "CHG-10004", LeaverUrgency.Planned, host.Time.GetUtcNow().UtcDateTime.AddHours(2), "scheduled-1"), olivia, CancellationToken.None));
        var approved = await Approve(created.Request.Id, sasha);
        Assert.Equal(LeaverState.Scheduled, approved.Request.State);

        await host.WithAsync<LeaverMaintenanceService, bool>(async m => { await m.RunOnceAsync(CancellationToken.None); return true; });
        Assert.Equal(LeaverState.Scheduled, (await host.WithAsync<LeaverWorkflowService, LeaverRequest>(w => w.LoadAsync(created.Request.Id, CancellationToken.None))).State);

        host.Time.Advance(TimeSpan.FromHours(3));
        await host.WithAsync<LeaverMaintenanceService, bool>(async m => { await m.RunOnceAsync(CancellationToken.None); return true; });
        var contained = await host.WithAsync<LeaverWorkflowService, LeaverRequest>(w => w.LoadAsync(created.Request.Id, CancellationToken.None));
        Assert.Contains(contained.State, new[] { LeaverState.SafelyContained, LeaverState.RetentionActionsPending });

        // Out-of-band re-enable is detected by reconciliation and escalated.
        host.Directory.Forest(FictionalIds.LegacyAConnector).Objects[FictionalIds.For("legacy-a:user:casey.placeholder")].UserAccountControl = 512;
        await host.WithAsync<Application.Reconciliation.ReconciliationService, Domain.Reconciliation.ReconciliationRun>(r => r.RunAsync("test", ActorContext.System("test"), CancellationToken.None));
        var escalated = await host.WithAsync<LeaverWorkflowService, LeaverRequest>(w => w.LoadAsync(created.Request.Id, CancellationToken.None));
        Assert.Equal(LeaverState.ReconciliationRequired, escalated.State);
    }

    [Fact]
    public async Task Non_urgent_stages_never_delete_and_complete_after_reconciliation()
    {
        var request = await Run("Casey Placeholder", sasha);
        await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w => w.AdvanceAsync(request.Id, olivia, CancellationToken.None));
        var tasks = await Tasks(request.Id);
        Assert.Equal(8, tasks.Count(t => t.Kind == ManualTaskKind.NonUrgentLeaverStage));
        Assert.All(tasks.Where(t => t.Kind == ManualTaskKind.NonUrgentLeaverStage), t => Assert.Contains("Never:", t.RunbookMarkdown, StringComparison.Ordinal));
        Assert.Contains(tasks, t => t.RunbookMarkdown.Contains("Legal Hold", StringComparison.Ordinal) || t.Title.Contains("group", StringComparison.OrdinalIgnoreCase));

        foreach (var t in tasks.Where(t => t.Kind == ManualTaskKind.NonUrgentLeaverStage))
        {
            await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w => w.RecordManualTaskAsync(t.Id, "done", olivia, CancellationToken.None));
        }

        for (var i = 0; i < 3; i++)
        {
            await host.WithAsync<LeaverWorkflowService, LeaverOperationResult>(w => w.AdvanceAsync(request.Id, olivia, CancellationToken.None));
        }

        var final = await host.WithAsync<LeaverWorkflowService, LeaverRequest>(w => w.LoadAsync(request.Id, CancellationToken.None));
        Assert.Equal(LeaverState.Completed, final.State);
        Assert.True(host.Directory.Forest(FictionalIds.LegacyAConnector).Objects.ContainsKey(FictionalIds.For("legacy-a:user:casey.placeholder")));
    }
}
