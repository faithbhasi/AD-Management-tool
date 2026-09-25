using Ilm.Application.Abstractions;
using Ilm.Application.Authority;
using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Application.Protection;
using Ilm.Application.Security;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Directory;
using Ilm.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Modules.ReadOnly;

/// <summary>
/// Read-only directory views restricted to the operator's OU scopes. Searches run server-side from each scope root;
/// only allowlisted attributes are ever read.
/// </summary>
public sealed class DirectoryViewService(
    IIlmDbContext db,
    IDirectoryConnectorRegistry registry,
    ScopeEvaluator scopes,
    ProtectionService protection,
    AuthorityService authority,
    IActiveConfigurationProvider configuration,
    TimeProvider time)
{
    public static readonly TimeSpan StaleLogonThreshold = TimeSpan.FromDays(90);

    public async Task<IReadOnlyList<ScopeRoot>> GetScopeRootsAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var ids = ViewRoles.SelectMany(actor.ScopesFor).Distinct(StringComparer.OrdinalIgnoreCase);
        var resolved = await scopes.ResolveScopesAsync(ids, cancellationToken);
        return resolved.Select(s => new ScopeRoot(s.ScopeId, s.ConnectorId, s.OuGuid, s.DisplayName, s.DistinguishedName)).ToList();
    }

    public async Task<IReadOnlyList<DirectoryObject>> ListChildOusAsync(ActorContext actor, string connectorId, Guid ouGuid, CancellationToken cancellationToken)
    {
        var parent = await registry.GetReader(connectorId).GetByGuidAsync(ouGuid, null, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "OU not found.");
        await RequireScopeAsync(actor, connectorId, parent.DistinguishedName, cancellationToken);
        return await registry.GetReader(connectorId).ListChildOrganizationalUnitsAsync(ouGuid, cancellationToken);
    }

    public async Task<IReadOnlyList<DirectoryObject>> SearchAsync(ActorContext actor, DirectoryObjectKind kind, string? text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Permissions.Has(actor, Permission.ViewDirectory))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Viewing the directory requires a directory role.");
        }

        var results = new Dictionary<Guid, DirectoryObject>();
        foreach (var root in await GetScopeRootsAsync(actor, cancellationToken))
        {
            if (root.DistinguishedName is null)
            {
                continue;
            }

            try
            {
                var found = await registry.GetReader(root.ConnectorId).SearchAsync(new DirectorySearch(root.OuGuid, kind, text, 100), cancellationToken);
                foreach (var o in found)
                {
                    results.TryAdd(o.ObjectGuid, o);
                }
            }
            catch (IOException)
            {
                // A connector outage hides that scope's results; health checks surface the outage.
            }
        }

        return results.Values.OrderBy(o => o.SamAccountName ?? o.Name, StringComparer.OrdinalIgnoreCase).Take(200).ToList();
    }

    public async Task<DirectoryObjectView> GetObjectAsync(ActorContext actor, string connectorId, Guid objectGuid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var obj = await registry.GetReader(connectorId).GetByGuidAsync(objectGuid, null, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Object not found.");
        var scope = await RequireScopeAsync(actor, connectorId, obj.DistinguishedName, cancellationToken);
        var decision = await protection.EvaluateAsync(connectorId, objectGuid, cancellationToken);
        var config = await configuration.GetAsync(cancellationToken);
        var identity = await db.ExternalIdentities.AsNoTracking().FirstOrDefaultAsync(i => i.ObjectGuid == objectGuid && i.ConnectorId == connectorId, cancellationToken);
        var person = identity?.PersonId is { } pid ? await db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, cancellationToken) : null;
        var hasActiveLeaver = person is not null && await db.LeaverRequests.AnyAsync(r => r.PersonId == person.Id && r.IsActive, cancellationToken);
        var leaverAuthority = identity is null ? [] : await authority.ResolveAllAsync(identity, LifecycleAction.Leaver, cancellationToken);
        var joinerAuthority = identity is null ? [] : await authority.ResolveAllAsync(identity, LifecycleAction.Joiner, cancellationToken);

        var warnings = new List<string>();
        if (obj.LastLogonTimestampUtc is { } last && time.GetUtcNow().UtcDateTime - last > StaleLogonThreshold)
        {
            warnings.Add($"lastLogonTimestamp is {last:yyyy-MM-dd}, more than {StaleLogonThreshold.TotalDays:0} days ago.");
        }

        if (obj.LastLogonTimestampUtc is not null)
        {
            warnings.Add("lastLogonTimestamp is replicated with a deliberate lag (up to about 14 days by default), so it is only an approximation of the last logon.");
        }

        var actions = obj.Kind == DirectoryObjectKind.Computer
            ? PermittedActionsEvaluator.ForComputer(actor, config, decision)
            : PermittedActionsEvaluator.ForDirectoryUser(actor, config, decision, scope.InScope, person is not null, hasActiveLeaver);

        return new DirectoryObjectView(obj, decision, scope, person, identity, leaverAuthority, joinerAuthority, actions, warnings);
    }

    private static readonly AppRole[] ViewRoles = [AppRole.Reader, AppRole.LifecycleOperator, AppRole.LifecycleApprover, AppRole.SecurityApprover];

    private async Task<ScopeDecision> RequireScopeAsync(ActorContext actor, string connectorId, string dn, CancellationToken cancellationToken)
    {
        foreach (var role in ViewRoles.Where(actor.HasRole))
        {
            var decision = await scopes.EvaluateAsync(actor, role, connectorId, dn, cancellationToken);
            if (decision.InScope)
            {
                return decision;
            }
        }

        throw new DomainException(SafeErrorCategory.NotAuthorised, "The object is outside your OU scopes.");
    }
}
