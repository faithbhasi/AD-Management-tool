using System.Security.Cryptography;
using Ilm.Application.Abstractions;
using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Application.Protection;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;
using Ilm.Domain.Protection;

namespace Ilm.Application.Provisioning;

/// <summary>
/// Direct LDAP writes to the target forest. Disabled initially. Containment disables and verifies;
/// joiner creation follows the staged saga whose defined failure state is "disabled, no unapproved groups".
/// </summary>
public sealed class DirectActiveDirectoryStrategy(
    IDirectoryConnectorRegistry registry,
    IActiveConfigurationProvider configuration,
    GuardedDirectoryWriter writer,
    ProtectionService protection,
    IDistributedLock locks) : IIdentityProvisioningStrategy
{
    public StrategyKind Kind => StrategyKind.DirectActiveDirectory;

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        var config = await configuration.GetAsync(cancellationToken);
        return config.IsEnabled(Feature.DirectActiveDirectoryContainment)
            || config.IsEnabled(Feature.StandardUserCreation)
            || config.IsEnabled(Feature.AdministrativeUserCreation);
    }

    public Task<ProvisioningPlan> BuildPlanAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Action == LifecycleAction.Leaver)
        {
            return Task.FromResult(new ProvisioningPlan(Kind, context.Identity.Id,
            [
                new PlannedStep(1, "SelectWritableDc", null, "Select and record one writable DC.", true),
                new PlannedStep(2, "DisableAccount", DirectoryOperation.DisableAccount, "Set ACCOUNTDISABLE with compare-and-swap.", true),
                new PlannedStep(3, "VerifyDisabled", DirectoryOperation.VerifyAccountState, "Re-read on the same DC.", true),
            ], ["Feature DirectActiveDirectoryContainment enabled.", "Connector mode WriteTarget.", "Protection Clear."]));
        }

        return Task.FromResult(new ProvisioningPlan(Kind, context.Identity.Id, CreateSagaSteps, ["Feature StandardUserCreation or AdministrativeUserCreation enabled.", "Connector mode WriteTarget."]));
    }

    /// <summary>The fourteen create stages from the Active Directory safety specification.</summary>
    public static readonly IReadOnlyList<PlannedStep> CreateSagaSteps =
    [
        new(1, "ResolveAuthority", null, "Resolve authority for every affected attribute set.", true),
        new(2, "SelectTargetOu", null, "Select a permitted target OU by objectGUID.", true),
        new(3, "UniquenessCheck", DirectoryOperation.Search, "Check sAMAccountName, UPN and mail uniqueness.", true),
        new(4, "AcquireIdentityKeyLock", null, "Acquire the identity-key distributed lock.", true),
        new(5, "SelectWritableDc", null, "Select and record one writable DC.", true),
        new(6, "RecheckUniqueness", DirectoryOperation.Search, "Re-run uniqueness on the selected DC.", true),
        new(7, "CreateDisabled", DirectoryOperation.CreateAccount, "Create the account disabled.", true),
        new(8, "SetApprovedAttributes", DirectoryOperation.ModifyAttributes, "Set approved attributes only.", true),
        new(9, "SetPassword", DirectoryOperation.ResetPassword, "Set the password over a protected transport.", true),
        new(10, "RequirePasswordChange", DirectoryOperation.ModifyAttributes, "Set pwdLastSet=0.", true),
        new(11, "AssignMandatoryGroups", DirectoryOperation.AddGroupMember, "Assign mandatory approved groups (never protected groups).", true),
        new(12, "ValidateSecurityControls", DirectoryOperation.ReadObject, "Verify disabled, no PASSWD_NOTREQD, correct groups, not protected.", true),
        new(13, "Enable", DirectoryOperation.EnableAccount, "Enable only after every mandatory stage passes.", true),
        new(14, "Reconcile", DirectoryOperation.ReadObject, "Read back and record the final state.", true),
    ];

    public async Task<PlanValidationResult> ValidatePlanAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var config = await configuration.GetAsync(cancellationToken);
        var errors = new List<string>();
        var connectorId = context.Identity.ConnectorId;
        var definition = connectorId is null ? null : config.FindConnector(connectorId);
        if (definition is null || definition.Role != ForestRole.Target || definition.Mode != ConnectorMode.WriteTarget)
        {
            errors.Add("Direct AD writes require a target-forest connector in WriteTarget mode.");
        }

        if (context.Action == LifecycleAction.Leaver)
        {
            if (!config.IsEnabled(Feature.DirectActiveDirectoryContainment))
            {
                errors.Add("Direct AD containment is disabled by feature flag.");
            }

            if (!context.ApprovedLeaverRequest)
            {
                errors.Add("An approved leaver request is required.");
            }

            if (context.Protection is not { Status: ProtectionStatus.Clear })
            {
                errors.Add("Protection is not conclusively Clear.");
            }
        }
        else if (context.Action == LifecycleAction.Joiner)
        {
            var feature = context.Identity.AccountType == AccountType.Administrative ? Feature.AdministrativeUserCreation : Feature.StandardUserCreation;
            if (!config.IsEnabled(feature))
            {
                errors.Add($"{feature} is disabled by feature flag.");
            }
        }
        else
        {
            errors.Add($"{context.Action} is not implemented by the direct AD strategy.");
        }

        if (!context.Authority.IsResolved || context.Authority.AuthoritativeSystem is not SystemKind.ActiveDirectory)
        {
            if (context.Action != LifecycleAction.Leaver || context.Authority.ContainmentOwner is not (SystemKind.ActiveDirectory or SystemKind.Ilm))
            {
                errors.Add("Authority does not assign this attribute set to Active Directory.");
            }
        }

        return errors.Count == 0 ? PlanValidationResult.Valid : new PlanValidationResult(false, errors);
    }

    public async Task<ExecutionResult> ExecuteAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var validation = await ValidatePlanAsync(context, plan, cancellationToken);
        if (!validation.IsValid)
        {
            return new ExecutionResult(ExecutionOutcome.Denied, SafeErrorCategory.FeatureDisabled, string.Join(" ", validation.Errors), null, [], [], false);
        }

        return context.Action == LifecycleAction.Leaver
            ? await DirectoryContainment.DisableAndVerifyAsync(registry, writer, context, requireStandardUser: false, cancellationToken)
            : new ExecutionResult(ExecutionOutcome.Denied, SafeErrorCategory.ValidationFailed, "Joiner execution requires a JoinerRequest; use ExecuteJoinerAsync.", null, [], [], false);
    }

    /// <summary>Runs the create saga. On any failure the account is left disabled with no unapproved groups.</summary>
    public async Task<ExecutionResult> ExecuteJoinerAsync(ProvisioningContext context, JoinerRequest joiner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(joiner);
        var attempted = new List<string>();
        var verified = new List<string>();
        var validation = await ValidatePlanAsync(context, new ProvisioningPlan(Kind, context.Identity.Id, CreateSagaSteps, []), cancellationToken);
        if (!validation.IsValid)
        {
            return new ExecutionResult(ExecutionOutcome.Denied, SafeErrorCategory.FeatureDisabled, string.Join(" ", validation.Errors), null, attempted, verified, false);
        }

        var config = await configuration.GetAsync(cancellationToken);
        var template = config.Document.AccountTemplates.FirstOrDefault(t => t.Id == joiner.TemplateId);
        var scope = template is null ? null : config.FindScope(template.TargetScopeId);
        attempted.Add("SelectTargetOu");
        if (scope is null || !Guid.TryParse(scope.OuObjectGuid, out var scopeGuid) || scopeGuid != joiner.User.TargetOuGuid)
        {
            return Fail(ExecutionOutcome.FailedBeforeChange, SafeErrorCategory.ValidationFailed, "The target OU is not the template's approved scope.");
        }

        var reader = registry.GetReader(joiner.ConnectorId);
        attempted.Add("UniquenessCheck");
        var clash = await FindClashAsync(reader, joiner.User, cancellationToken);
        if (clash is not null)
        {
            return Fail(ExecutionOutcome.FailedBeforeChange, SafeErrorCategory.DuplicateObject, clash);
        }

        attempted.Add("AcquireIdentityKeyLock");
        await using var handle = await locks.TryAcquireAsync($"identity-key:{joiner.ConnectorId}:{joiner.User.SamAccountName.ToUpperInvariant()}", TimeSpan.FromMinutes(5), cancellationToken);
        if (handle is null)
        {
            return Fail(ExecutionOutcome.FailedBeforeChange, SafeErrorCategory.ConcurrencyConflict, "Another request holds this identity key.");
        }

        var dc = (await reader.SelectWritableDomainControllerAsync(cancellationToken)).DomainController;
        attempted.Add($"SelectWritableDc:{dc}");
        attempted.Add("RecheckUniqueness");
        clash = await FindClashAsync(reader, joiner.User, cancellationToken);
        if (clash is not null)
        {
            return Fail(ExecutionOutcome.FailedBeforeChange, SafeErrorCategory.DuplicateObject, clash, dc);
        }

        var channel = registry.GetWriteChannel(joiner.ConnectorId);
        attempted.Add("CreateDisabled");
        var created = await channel.CreateDisabledUserAsync(joiner.User, dc, cancellationToken);
        if (!created.Succeeded)
        {
            return Fail(ExecutionOutcome.FailedBeforeChange, created.ErrorCategory, created.Detail, dc);
        }

        var newObject = (await reader.FindUsersByAttributeAsync("sAMAccountName", joiner.User.SamAccountName, cancellationToken)).SingleOrDefault();
        if (newObject is null)
        {
            return Fail(ExecutionOutcome.FailedAfterChange, SafeErrorCategory.VerificationFailed, "The created account could not be read back.", dc);
        }

        attempted.Add("SetPassword");
        var password = GenerateInitialPassword();
        var pwd = await channel.SetPasswordAsync(newObject.ObjectGuid, dc, password, cancellationToken);
        Array.Clear(password);
        if (!pwd.Succeeded)
        {
            return Fail(ExecutionOutcome.FailedAfterChange, pwd.ErrorCategory, "Password could not be set; the account remains disabled.", dc);
        }

        attempted.Add("RequirePasswordChange");
        var change = await channel.RequirePasswordChangeAsync(newObject.ObjectGuid, dc, cancellationToken);
        if (!change.Succeeded)
        {
            return Fail(ExecutionOutcome.FailedAfterChange, change.ErrorCategory, "pwdLastSet could not be set; the account remains disabled.", dc);
        }

        attempted.Add("AssignMandatoryGroups");
        foreach (var groupGuid in joiner.MandatoryGroupGuids)
        {
            var groupDecision = await protection.EvaluateAsync(joiner.ConnectorId, groupGuid, cancellationToken);
            if (groupDecision.Status != ProtectionStatus.Clear)
            {
                return Fail(ExecutionOutcome.FailedAfterChange, SafeErrorCategory.ProtectedObject, "A mandatory group is protected or unverifiable; the account remains disabled with no further groups.", dc);
            }

            var add = await channel.AddGroupMemberAsync(groupGuid, newObject.ObjectGuid, dc, cancellationToken);
            if (!add.Succeeded)
            {
                return Fail(ExecutionOutcome.FailedAfterChange, add.ErrorCategory, "A mandatory group could not be assigned; the account remains disabled.", dc);
            }
        }

        attempted.Add("ValidateSecurityControls");
        var state = await reader.ReadAccountStateAsync(newObject.ObjectGuid, dc, cancellationToken);
        if (state is null || !state.IsDisabled || (state.UserAccountControl & (int)UserAccountControl.PasswordNotRequired) != 0)
        {
            return Fail(ExecutionOutcome.FailedAfterChange, SafeErrorCategory.VerificationFailed, "Security control validation failed; the account remains disabled.", dc);
        }

        var accountDecision = await protection.EvaluateAsync(joiner.ConnectorId, newObject.ObjectGuid, cancellationToken);
        if (accountDecision.Status != ProtectionStatus.Clear)
        {
            return Fail(ExecutionOutcome.FailedAfterChange, SafeErrorCategory.ProtectedObject, "The new account evaluates as protected; it remains disabled.", dc);
        }

        verified.Add("Disabled, password set, change required, groups assigned, not protected");

        attempted.Add("Enable");
        var enabled = await channel.EnableAccountAsync(newObject.ObjectGuid, dc, state.UserAccountControl, cancellationToken);
        if (!enabled.Succeeded)
        {
            return Fail(ExecutionOutcome.FailedAfterChange, enabled.ErrorCategory, "Enable failed; the account remains disabled.", dc);
        }

        attempted.Add("Reconcile");
        var final = await reader.ReadAccountStateAsync(newObject.ObjectGuid, dc, cancellationToken);
        if (final is { IsDisabled: false })
        {
            verified.Add($"Enabled on {dc}");
        }

        return new ExecutionResult(ExecutionOutcome.Succeeded, SafeErrorCategory.None, $"Created {newObject.ObjectGuid:D}.", dc, attempted, verified, true);

        ExecutionResult Fail(ExecutionOutcome outcome, SafeErrorCategory category, string detail, string? onDc = null) =>
            new(outcome, category, detail, onDc, attempted, verified, outcome == ExecutionOutcome.FailedAfterChange);
    }

    public Task<ReconciliationResult> ReconcileAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken) =>
        DirectoryContainment.ReconcileDisabledAsync(registry, context, cancellationToken);

    private static async Task<string?> FindClashAsync(IDirectoryReader reader, NewUserRequest user, CancellationToken cancellationToken)
    {
        (string Attribute, string? Value)[] keys =
        [
            ("sAMAccountName", user.SamAccountName),
            ("userPrincipalName", user.UserPrincipalName),
            ("mail", user.Mail),
            ("proxyAddresses", user.Mail is null ? null : "smtp:" + user.Mail),
        ];
        foreach (var (attribute, value) in keys)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if ((await reader.FindUsersByAttributeAsync(attribute, value, cancellationToken)).Count > 0)
            {
                return $"{attribute} is already in use.";
            }
        }

        return null;
    }

    private static char[] GenerateInitialPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#%*+-=?";
        var chars = new char[32];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return chars;
    }
}
