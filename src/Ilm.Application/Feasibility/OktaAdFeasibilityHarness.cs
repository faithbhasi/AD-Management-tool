using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Ilm.Application.Okta;

namespace Ilm.Application.Feasibility;

/// <summary>
/// Executes the 26 PCATEST checks for the Okta → AD provisioning path using synthetic users only.
/// It records observations; it never hardcodes an expected provisioning delay.
/// </summary>
public sealed class OktaAdFeasibilityHarness(
    IOktaUserClient users,
    IOktaLifecycleClient lifecycle,
    IOktaSessionClient sessions,
    IOktaGroupClient groups,
    IOktaApplicationClient applications,
    IOktaSystemLogClient systemLog,
    IOktaFeasibilityCleanup cleanup,
    IAdTargetStateReconciler adState,
    IFeasibilityDirectoryProbe probe,
    IEnumerable<IFeasibilityEnvironmentControl> environmentControls,
    TimeProvider time)
{
    public static readonly IReadOnlyList<string> CheckTitles =
    [
        "Create a staged Okta test user through the API",
        "Assign the user to the group or application that drives AD provisioning",
        "Verify whether an AD object is created",
        "Record the provisioning delay",
        "Verify target OU selection",
        "Verify sAMAccountName",
        "Verify UPN",
        "Verify mail and proxy addresses",
        "Verify department",
        "Verify manager",
        "Verify account enabled state",
        "Verify initial password behaviour",
        "Verify group memberships",
        "Verify duplicate-user behaviour",
        "Verify existing-AD-user matching behaviour",
        "Verify suspension behaviour",
        "Verify deactivation behaviour",
        "Verify whether deactivation disables AD",
        "Verify whether reactivation re-enables AD",
        "Verify whether an ILM AD change is overwritten by the next Okta push",
        "Verify Okta API and System Log correlation",
        "Verify failure when the Okta AD Agent is unavailable",
        "Verify retry and duplicate prevention",
        "Verify that an existing Okta user is linked rather than duplicated",
        "Verify that factors and application assignments remain intact",
        "Verify rollback and clean-up of synthetic test users",
    ];

    public async Task<FeasibilityRunResult> RunAsync(FeasibilityOptions options, bool isMock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var run = new RunState(options, time.GetUtcNow().UtcDateTime);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var login = $"{options.SyntheticLoginPrefix}{suffix}@{options.SyntheticEmailDomain}";
        var request = new OktaCreateUserRequest(login, login, "Synthetic", "Tester" + suffix, options.Department, options.ManagerOktaId, null);

        // 1. Create staged user
        OktaUser? user = null;
        await Check(run, 1, async () =>
        {
            var created = await users.CreateUserAsync(request, activate: false, cancellationToken);
            if (!created.Succeeded)
            {
                return (CheckOutcome.Failed, $"Create failed: HTTP {created.StatusCode} {created.OktaErrorCode}.");
            }

            user = created.Value!;
            run.CreatedOktaUsers.Add(user.Id);
            return user.Status == OktaUserStatus.Staged
                ? (CheckOutcome.Passed, $"Created {user.Id} in STAGED state.")
                : (CheckOutcome.Failed, $"Created {user.Id} but status is {user.Status}, not STAGED.");
        });

        if (user is null)
        {
            return Finish(run, isMock, "The staged user could not be created; later checks were not run.");
        }

        var createdUser = user;
        var factorsBefore = await users.GetFactorTypesAsync(createdUser.Id, cancellationToken);

        // 2. Assignment
        await Check(run, 2, async () =>
        {
            var result = string.Equals(options.AssignmentMechanism, "Application", StringComparison.OrdinalIgnoreCase)
                ? await applications.AssignUserToApplicationAsync(options.AssignmentId, createdUser.Id, cancellationToken)
                : await groups.AssignUserToGroupAsync(options.AssignmentId, createdUser.Id, cancellationToken);
            return result.Succeeded
                ? (CheckOutcome.Passed, $"Assigned through {options.AssignmentMechanism} {options.AssignmentId}.")
                : (CheckOutcome.Failed, $"Assignment failed: HTTP {result.StatusCode} {result.OktaErrorCode}.");
        });

        // 3 and 4. AD object creation and delay
        var lookup = new AdTargetLookup(null, login, login, null);
        var sw = Stopwatch.StartNew();
        var (ad, activatedForProvisioning) = await PollForObjectAsync(run, createdUser, lookup, cancellationToken);
        sw.Stop();
        Record(run, 3, ad.Found ? CheckOutcome.Passed : CheckOutcome.Failed,
            ad.Found
                ? $"AD object {ad.ObjectGuid:D} created ({ad.MatchCount} match). {(activatedForProvisioning ? "Provisioning required the Okta user to be activated first." : "Provisioned while STAGED.")}"
                : "No AD object appeared within the polling window. Creating an Okta user did not create an AD user.",
            sw.ElapsedMilliseconds);
        Record(run, 4, ad.Found ? CheckOutcome.Observed : CheckOutcome.Inconclusive,
            ad.Found ? $"Observed delay {sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s over {run.LastPollCount} poll(s). No expected duration is assumed." : "No delay could be measured.",
            sw.ElapsedMilliseconds);

        if (ad.Found)
        {
            VerifyAttributes(run, options, request, ad);
        }
        else
        {
            for (var n = 5; n <= 13; n++)
            {
                Record(run, n, CheckOutcome.Skipped, "No AD object to inspect.", 0);
            }
        }

        // 14. Duplicate user
        await Check(run, 14, async () =>
        {
            var dup = await users.CreateUserAsync(request, activate: false, cancellationToken);
            if (dup.Succeeded)
            {
                run.CreatedOktaUsers.Add(dup.Value!.Id);
                return (CheckOutcome.Failed, $"Okta accepted a duplicate login and created {dup.Value.Id}.");
            }

            var matches = await users.SearchUsersAsync(new OktaUserSearch(null, login, null), cancellationToken);
            return matches.Succeeded && matches.Value!.Count == 1
                ? (CheckOutcome.Passed, $"Duplicate create rejected (HTTP {dup.StatusCode} {dup.OktaErrorCode}); one user remains.")
                : (CheckOutcome.Inconclusive, "Duplicate rejected but the follow-up search was inconclusive.");
        });

        // 15. Existing AD user matching
        await Check(run, 15, async () =>
        {
            if (string.IsNullOrWhiteSpace(options.ExistingAdUserLogin))
            {
                return (CheckOutcome.Skipped, "No pre-existing synthetic AD user was supplied (ExistingAdUserLogin).");
            }

            var existingLookup = new AdTargetLookup(null, options.ExistingAdUserLogin, options.ExistingAdUserLogin, null);
            var before = await adState.ReadAsync(options.TargetConnectorId, existingLookup, cancellationToken);
            var created = await users.CreateUserAsync(request with { Login = options.ExistingAdUserLogin, Email = options.ExistingAdUserLogin }, activate: true, cancellationToken);
            if (!created.Succeeded)
            {
                return (CheckOutcome.Observed, $"Okta refused the matching user (HTTP {created.StatusCode} {created.OktaErrorCode}).");
            }

            run.CreatedOktaUsers.Add(created.Value!.Id);
            await groups.AssignUserToGroupAsync(options.AssignmentId, created.Value.Id, cancellationToken);
            var after = await PollUntilAsync(() => adState.ReadAsync(options.TargetConnectorId, existingLookup, cancellationToken), s => s.MatchCount != before.MatchCount || s.Found, options, run, cancellationToken);
            return after.MatchCount > before.MatchCount
                ? (CheckOutcome.Failed, $"A second AD object was created ({after.MatchCount} matches); Okta did not match the existing user.")
                : (CheckOutcome.Observed, $"Existing AD object retained ({after.MatchCount} match); Okta matched or refused rather than duplicating.");
        });

        // Ensure the user is active for lifecycle and push checks.
        var current = await users.GetUserAsync(createdUser.Id, cancellationToken);
        if (current.Value?.Status == OktaUserStatus.Staged)
        {
            await lifecycle.ActivateAsync(createdUser.Id, cancellationToken);
        }

        // 20. Push overwrite (while active)
        await Check(run, 20, async () =>
        {
            if (!ad.Found || ad.ObjectGuid is null)
            {
                return (CheckOutcome.Skipped, "No AD object to probe.");
            }

            var probeValue = "ILM-PROBE-" + suffix;
            if (!await probe.SetProbeAttributeAsync(options.TargetConnectorId, ad.ObjectGuid.Value, SamPrefix(options), options.ProbeAttribute, probeValue, cancellationToken))
            {
                return (CheckOutcome.Inconclusive, "The directory probe refused or failed to write the synthetic attribute.");
            }

            await users.UpdateProfileAsync(createdUser.Id, new Dictionary<string, string> { ["title"] = "Synthetic push trigger " + suffix }, cancellationToken);
            var after = await PollUntilAsync(() => adState.ReadAsync(options.TargetConnectorId, lookup, cancellationToken), s => !string.Equals(s.Department, probeValue, StringComparison.Ordinal), options, run, cancellationToken);
            if (!string.Equals(after.Department, probeValue, StringComparison.Ordinal))
            {
                run.Conditions.Add($"Okta push overwrites ILM changes to '{options.ProbeAttribute}': ILM must never write Okta-mapped attributes for Okta-mastered users.");
                return (CheckOutcome.Observed, $"Overwritten: '{options.ProbeAttribute}' reverted to '{after.Department}' after the next push.");
            }

            return (CheckOutcome.Observed, $"Retained: '{options.ProbeAttribute}' kept the ILM value after the profile update (attribute may not be push-mapped).");
        });

        var appsBefore = await users.GetAssignedApplicationIdsAsync(createdUser.Id, cancellationToken);

        // 16. Suspension
        await Check(run, 16, async () =>
        {
            var r = await lifecycle.SuspendAsync(createdUser.Id, cancellationToken);
            if (!r.Succeeded)
            {
                return (CheckOutcome.Failed, $"Suspend failed: HTTP {r.StatusCode}.");
            }

            var s = await PollUntilAsync(() => adState.ReadAsync(options.TargetConnectorId, lookup, cancellationToken), x => x.Enabled == false, options, run, cancellationToken);
            await lifecycle.UnsuspendAsync(createdUser.Id, cancellationToken);
            return (CheckOutcome.Observed, s.Enabled == false
                ? "Suspension disabled the AD account."
                : "Suspension did not change the AD account; containment must not rely on suspension alone.");
        });

        // Session revocation (reported under deactivation behaviour and the session section)
        var sessionResult = await sessions.RevokeSessionsAsync(createdUser.Id, revokeOAuthTokens: true, cancellationToken);
        run.SessionObservation = sessionResult.Succeeded
            ? $"Okta session revocation accepted (HTTP {sessionResult.StatusCode}). Downstream application sessions are not proven closed."
            : $"Okta session revocation failed (HTTP {sessionResult.StatusCode}).";

        // 17 and 18. Deactivation
        await Check(run, 17, async () =>
        {
            var r = await lifecycle.DeactivateAsync(createdUser.Id, cancellationToken);
            var state = await lifecycle.GetLifecycleStateAsync(createdUser.Id, cancellationToken);
            return r.Succeeded && state.Value == OktaUserStatus.Deprovisioned
                ? (CheckOutcome.Passed, "Okta user is DEPROVISIONED.")
                : (CheckOutcome.Failed, $"Deactivation result HTTP {r.StatusCode}; status {state.Value}.");
        });

        await Check(run, 18, async () =>
        {
            if (!ad.Found)
            {
                return (CheckOutcome.Skipped, "No AD object.");
            }

            var s = await PollUntilAsync(() => adState.ReadAsync(options.TargetConnectorId, lookup, cancellationToken), x => x.Enabled == false, options, run, cancellationToken);
            if (s.Enabled == false)
            {
                return (CheckOutcome.Passed, "Deactivation disabled the AD account.");
            }

            run.Conditions.Add("Okta deactivation did not disable AD: ILM must verify AD and apply the approved emergency directory override or manual containment.");
            return (CheckOutcome.Failed, "Deactivation did not disable the AD account within the polling window.");
        });

        // 19. Reactivation
        await Check(run, 19, async () =>
        {
            var r = await lifecycle.ActivateAsync(createdUser.Id, cancellationToken);
            if (!r.Succeeded)
            {
                return (CheckOutcome.Observed, $"Reactivation refused (HTTP {r.StatusCode}).");
            }

            var s = await PollUntilAsync(() => adState.ReadAsync(options.TargetConnectorId, lookup, cancellationToken), x => x.Enabled == true, options, run, cancellationToken);
            if (s.Enabled == true)
            {
                run.Conditions.Add("Reactivation in Okta re-enables AD: reactivation must be treated as an access grant with approval.");
            }

            return (CheckOutcome.Observed, s.Enabled == true ? "Reactivation re-enabled the AD account." : "Reactivation did not re-enable the AD account.");
        });

        // 21. System Log correlation
        await Check(run, 21, async () =>
        {
            var log = await systemLog.QueryAsync(run.StartedUtc, createdUser.Id, null, cancellationToken);
            if (!log.Succeeded)
            {
                return (CheckOutcome.Failed, "System Log query failed.");
            }

            var types = log.Value!.Select(e => e.EventType).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
            return types.Count > 0
                ? (CheckOutcome.Passed, $"{log.Value!.Count} event(s) correlated to {createdUser.Id}: {string.Join(", ", types)}.")
                : (CheckOutcome.Failed, "No System Log events could be correlated to the synthetic user.");
        });

        // 22. Agent outage
        await Check(run, 22, async () =>
        {
            var control = environmentControls.FirstOrDefault(c => c.CanSimulateAgentOutage);
            if (control is null && !options.OperatorConfirmedAgentOutageWindow)
            {
                return (CheckOutcome.Skipped, "Requires an operator-controlled Okta AD Agent outage window (OperatorConfirmedAgentOutageWindow).");
            }

            control?.SetAgentAvailable(false);
            try
            {
                var outageLogin = $"{options.SyntheticLoginPrefix}{suffix}o@{options.SyntheticEmailDomain}";
                var created = await users.CreateUserAsync(request with { Login = outageLogin, Email = outageLogin }, activate: true, cancellationToken);
                if (!created.Succeeded)
                {
                    return (CheckOutcome.Inconclusive, "Could not create the outage probe user.");
                }

                run.CreatedOktaUsers.Add(created.Value!.Id);
                await groups.AssignUserToGroupAsync(options.AssignmentId, created.Value.Id, cancellationToken);
                var state = await PollUntilAsync(() => adState.ReadAsync(options.TargetConnectorId, new AdTargetLookup(null, outageLogin, outageLogin, null), cancellationToken), s => s.Found, options with { MaxPolls = Math.Min(options.MaxPolls, 5) }, run, cancellationToken);
                var log = await systemLog.QueryAsync(run.StartedUtc, created.Value.Id, null, cancellationToken);
                var failures = log.Value?.Count(e => !string.Equals(e.Outcome, "SUCCESS", StringComparison.OrdinalIgnoreCase)) ?? 0;
                run.Conditions.Add("During an Okta AD Agent outage no AD object is created; ILM must keep the request pending and alert rather than report success.");
                return state.Found
                    ? (CheckOutcome.Failed, "An AD object appeared despite the simulated outage; the outage was not effective.")
                    : (CheckOutcome.Passed, $"No AD object during the outage; {failures} non-success System Log event(s) recorded.");
            }
            finally
            {
                control?.SetAgentAvailable(true);
            }
        });

        // 23. Retry without duplication
        await Check(run, 23, async () =>
        {
            var retryLogin = $"{options.SyntheticLoginPrefix}{suffix}r@{options.SyntheticEmailDomain}";
            var retryRequest = request with { Login = retryLogin, Email = retryLogin };
            var attempts = await Task.WhenAll(
                users.CreateUserAsync(retryRequest, activate: false, cancellationToken),
                users.CreateUserAsync(retryRequest, activate: false, cancellationToken));
            foreach (var a in attempts.Where(a => a.Succeeded))
            {
                run.CreatedOktaUsers.Add(a.Value!.Id);
            }

            var search = await users.SearchUsersAsync(new OktaUserSearch(null, retryLogin, null), cancellationToken);
            return search.Succeeded && search.Value!.Count == 1
                ? (CheckOutcome.Passed, "Concurrent retries produced exactly one Okta user.")
                : (CheckOutcome.Failed, $"Concurrent retries produced {search.Value?.Count ?? -1} users.");
        });

        // 24. Link rather than duplicate
        await Check(run, 24, async () =>
        {
            var search = await users.SearchUsersAsync(new OktaUserSearch(login, null, null), cancellationToken);
            return search.Succeeded && search.Value!.Count == 1 && search.Value[0].Id == createdUser.Id
                ? (CheckOutcome.Passed, "Search by email finds the existing user, so ILM links instead of creating.")
                : (CheckOutcome.Failed, "Search did not return exactly the existing user; ILM could duplicate.");
        });

        // 25. Factors and app assignments intact
        await Check(run, 25, async () =>
        {
            var factorsAfter = await users.GetFactorTypesAsync(createdUser.Id, cancellationToken);
            var appsAfter = await users.GetAssignedApplicationIdsAsync(createdUser.Id, cancellationToken);
            if (!factorsBefore.Succeeded || !factorsAfter.Succeeded || !appsBefore.Succeeded || !appsAfter.Succeeded)
            {
                return (CheckOutcome.Inconclusive, "Factor or application assignment reads failed.");
            }

            var factorsSame = factorsBefore.Value!.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(factorsAfter.Value!.OrderBy(x => x, StringComparer.Ordinal));
            var appsSame = appsBefore.Value!.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(appsAfter.Value!.OrderBy(x => x, StringComparer.Ordinal));
            return factorsSame && appsSame
                ? (CheckOutcome.Passed, $"Factors ({factorsAfter.Value!.Count}) and application assignments ({appsAfter.Value!.Count}) unchanged by the lifecycle cycle.")
                : (CheckOutcome.Observed, $"Changed: factors {(factorsSame ? "same" : "differ")}, applications {(appsSame ? "same" : "differ")}. Reactivation may require re-enrolment or reassignment.");
        });

        // 26. Clean-up
        await Check(run, 26, async () =>
        {
            var failures = 0;
            foreach (var id in run.CreatedOktaUsers.Distinct(StringComparer.Ordinal))
            {
                await lifecycle.DeactivateAsync(id, cancellationToken);
                var deleted = await cleanup.DeleteSyntheticUserAsync(id, options.SyntheticLoginPrefix, cancellationToken);
                if (!deleted.Succeeded)
                {
                    failures++;
                }
            }

            if (ad.Found)
            {
                run.LeftForManualCleanup.Add($"AD object {ad.ObjectGuid:D} ({ad.DistinguishedName}): ILM never deletes directory objects; remove it through the lab clean-up procedure.");
            }

            return failures == 0
                ? (CheckOutcome.Passed, $"Deactivated and deleted {run.CreatedOktaUsers.Distinct(StringComparer.Ordinal).Count()} synthetic Okta user(s).")
                : (CheckOutcome.Failed, $"{failures} synthetic Okta user(s) could not be removed.");
        });

        return Finish(run, isMock, null);
    }

    private void VerifyAttributes(RunState run, FeasibilityOptions options, OktaCreateUserRequest request, AdTargetState ad)
    {
        Record(run, 5, options.ExpectedOuDistinguishedName is null
            ? CheckOutcome.Observed
            : string.Equals(ad.ParentOuDistinguishedName, options.ExpectedOuDistinguishedName, StringComparison.OrdinalIgnoreCase) ? CheckOutcome.Passed : CheckOutcome.Failed,
            $"Placed in {ad.ParentOuDistinguishedName}.", 0);

        var samOk = options.ExpectedSamPattern is null || (ad.SamAccountName is not null
            && Regex.IsMatch(ad.SamAccountName, options.ExpectedSamPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        Record(run, 6, options.ExpectedSamPattern is null ? CheckOutcome.Observed : samOk ? CheckOutcome.Passed : CheckOutcome.Failed, $"sAMAccountName '{ad.SamAccountName}'.", 0);
        Record(run, 7, string.Equals(ad.UserPrincipalName, request.Login, StringComparison.OrdinalIgnoreCase) ? CheckOutcome.Passed : CheckOutcome.Observed, $"UPN '{ad.UserPrincipalName}' (Okta login '{request.Login}').", 0);
        var proxyOk = ad.ProxyAddresses.Any(p => string.Equals(p, "SMTP:" + request.Email, StringComparison.OrdinalIgnoreCase));
        Record(run, 8, string.Equals(ad.Mail, request.Email, StringComparison.OrdinalIgnoreCase) && proxyOk ? CheckOutcome.Passed : CheckOutcome.Observed,
            $"mail '{ad.Mail}'; proxyAddresses [{string.Join(", ", ad.ProxyAddresses)}].", 0);
        Record(run, 9, string.Equals(ad.Department, request.Department, StringComparison.Ordinal) ? CheckOutcome.Passed : CheckOutcome.Failed, $"department '{ad.Department}'.", 0);
        Record(run, 10, request.ManagerId is null ? CheckOutcome.Skipped : ad.ManagerDistinguishedName is null ? CheckOutcome.Failed : CheckOutcome.Passed,
            request.ManagerId is null ? "No manager supplied (ManagerOktaId)." : $"manager '{ad.ManagerDistinguishedName ?? "(not set)"}'.", 0);
        Record(run, 11, CheckOutcome.Observed, $"AD account enabled: {ad.Enabled?.ToString() ?? "unknown"}.", 0);
        Record(run, 12, CheckOutcome.Observed, "ILM cannot and does not read passwords. Operator notes: " + options.PasswordBehaviourNotes, 0);
        var groupsOk = options.ExpectedGroupSids.All(g => ad.GroupSids.Contains(g, StringComparer.OrdinalIgnoreCase));
        Record(run, 13, options.ExpectedGroupSids.Count == 0 ? CheckOutcome.Observed : groupsOk ? CheckOutcome.Passed : CheckOutcome.Failed,
            $"{ad.GroupSids.Count} group(s): {string.Join(", ", ad.GroupSids)}.", 0);
    }

    private async Task<(AdTargetState State, bool ActivatedForProvisioning)> PollForObjectAsync(RunState run, OktaUser user, AdTargetLookup lookup, CancellationToken cancellationToken)
    {
        var activated = false;
        AdTargetState state = await adState.ReadAsync(run.Options.TargetConnectorId, lookup, cancellationToken);
        run.LastPollCount = 1;
        for (var i = 1; i < run.Options.MaxPolls && !state.Found; i++)
        {
            if (!activated && i == run.Options.MaxPolls / 2)
            {
                var status = await lifecycle.GetLifecycleStateAsync(user.Id, cancellationToken);
                if (status.Value == OktaUserStatus.Staged)
                {
                    await lifecycle.ActivateAsync(user.Id, cancellationToken);
                    activated = true;
                }
            }

            await Delay(run.Options.PollInterval, cancellationToken);
            state = await adState.ReadAsync(run.Options.TargetConnectorId, lookup, cancellationToken);
            run.LastPollCount = i + 1;
        }

        return (state, activated);
    }

    private async Task<AdTargetState> PollUntilAsync(Func<Task<AdTargetState>> read, Func<AdTargetState, bool> done, FeasibilityOptions options, RunState run, CancellationToken cancellationToken)
    {
        var state = await read();
        var polls = 1;
        while (!done(state) && polls < options.MaxPolls)
        {
            await Delay(options.PollInterval, cancellationToken);
            state = await read();
            polls++;
        }

        run.LastPollCount = polls;
        return state;
    }

    private Task Delay(TimeSpan interval, CancellationToken cancellationToken) =>
        interval <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(interval, time, cancellationToken);

    private static string SamPrefix(FeasibilityOptions options) => options.SyntheticLoginPrefix;

    private static async Task Check(RunState run, int number, Func<Task<(CheckOutcome Outcome, string Observation)>> body)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (outcome, observation) = await body();
            Record(run, number, outcome, observation, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Record(run, number, CheckOutcome.Inconclusive, $"Check raised {ex.GetType().Name}.", sw.ElapsedMilliseconds);
        }
    }

    private static void Record(RunState run, int number, CheckOutcome outcome, string observation, long durationMs) =>
        run.Results[number] = new FeasibilityCheckResult(number, CheckTitles[number - 1], outcome, observation, durationMs);

    private FeasibilityRunResult Finish(RunState run, bool isMock, string? abortReason)
    {
        for (var n = 1; n <= CheckTitles.Count; n++)
        {
            if (!run.Results.ContainsKey(n))
            {
                Record(run, n, CheckOutcome.Skipped, abortReason ?? "Not run.", 0);
            }
        }

        var unsupported = new List<string>
        {
            "Okta profile-source and push-mapping settings are recorded from operator observation; the harness cannot read all of them through supported APIs.",
            "Initial password behaviour is not observable by ILM, which never reads passwords.",
        };
        if (isMock)
        {
            unsupported.Insert(0, "This run used the in-process mock Okta org and mock directory. It is not evidence about PCATEST behaviour.");
        }

        return new FeasibilityRunResult(
            run.Options,
            isMock,
            run.StartedUtc,
            time.GetUtcNow().UtcDateTime,
            run.Results.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList(),
            run.Conditions.Distinct(StringComparer.Ordinal).ToList(),
            unsupported,
            run.LeftForManualCleanup,
            run.SessionObservation);
    }

    private sealed class RunState(FeasibilityOptions options, DateTime startedUtc)
    {
        public FeasibilityOptions Options { get; } = options;

        public DateTime StartedUtc { get; } = startedUtc;

        public Dictionary<int, FeasibilityCheckResult> Results { get; } = [];

        public List<string> CreatedOktaUsers { get; } = [];

        public List<string> Conditions { get; } = [];

        public List<string> LeftForManualCleanup { get; } = [];

        public string SessionObservation { get; set; } = "Not run.";

        public int LastPollCount { get; set; }
    }
}
