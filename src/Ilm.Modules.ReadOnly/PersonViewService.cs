using Ilm.Application.Abstractions;
using Ilm.Application.Authority;
using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Application.Protection;
using Ilm.Application.Security;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Identity;
using Ilm.Domain.Protection;
using Ilm.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Modules.ReadOnly;

/// <summary>Person-centric view: the person, every linked account, link evidence, authority per attribute set and protection.</summary>
public sealed class PersonViewService(
    IIlmDbContext db,
    IDirectoryConnectorRegistry registry,
    ScopeEvaluator scopes,
    ProtectionService protection,
    AuthorityService authority,
    IActiveConfigurationProvider configuration)
{
    public async Task<IReadOnlyList<Person>> SearchAsync(ActorContext actor, string? text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Permissions.Has(actor, Permission.ViewPeople))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Viewing people requires a directory role.");
        }

        var query = db.Persons.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim();
            query = query.Where(p => p.DisplayName.Contains(t) || p.PersonIdentifier.Contains(t));
        }

        return await query.OrderBy(p => p.DisplayName).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<PersonView> GetAsync(ActorContext actor, Guid personId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Permissions.Has(actor, Permission.ViewPeople))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Viewing people requires a directory role.");
        }

        var person = await db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Person not found.");
        var identities = await db.ExternalIdentities.AsNoTracking().Where(i => i.PersonId == personId).ToListAsync(cancellationToken);
        var links = await db.IdentityLinks.AsNoTracking().Where(l => l.PersonId == personId).OrderByDescending(l => l.CreatedUtc).ToListAsync(cancellationToken);
        var migration = await db.MigrationStates.AsNoTracking().FirstOrDefaultAsync(m => m.PersonId == personId, cancellationToken);
        var activeLeaver = await db.LeaverRequests.AsNoTracking().Where(r => r.PersonId == personId && r.IsActive).FirstOrDefaultAsync(cancellationToken);
        var config = await configuration.GetAsync(cancellationToken);

        var views = new List<IdentityView>();
        var anyInScope = identities.All(i => i.System != SystemKind.ActiveDirectory);
        foreach (var identity in identities.OrderBy(i => i.System).ThenBy(i => i.StableObjectId, StringComparer.Ordinal))
        {
            ProtectionDecision? decision = null;
            string? dn = null;
            if (identity.System == SystemKind.ActiveDirectory && identity.ConnectorId is not null && identity.ObjectGuid is not null)
            {
                decision = await protection.EvaluateAsync(identity.ConnectorId, identity.ObjectGuid.Value, cancellationToken);
                try
                {
                    dn = (await registry.GetReader(identity.ConnectorId).GetByGuidAsync(identity.ObjectGuid.Value, null, cancellationToken))?.DistinguishedName;
                }
                catch (IOException)
                {
                    dn = null;
                }

                if (dn is not null && !anyInScope)
                {
                    anyInScope = (await scopes.EvaluateAsync(actor, AppRole.LifecycleOperator, identity.ConnectorId, dn, cancellationToken)).InScope;
                }
            }

            var link = links.Where(l => l.TargetIdentityId == identity.Id && l.Confidence != LinkConfidence.Rejected).OrderByDescending(l => l.Confidence).FirstOrDefault();
            views.Add(new IdentityView(identity, link, decision, await authority.ResolveAllAsync(identity, LifecycleAction.Leaver, cancellationToken), dn));
        }

        var actions = PermittedActionsEvaluator.ForPerson(actor, config, activeLeaver is not null, anyInScope);
        return new PersonView(person, views, links, migration, activeLeaver, actions);
    }
}
