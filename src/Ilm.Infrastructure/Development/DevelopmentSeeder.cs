using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Application.Configuration;
using Ilm.Domain.Common;
using Ilm.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using static Ilm.Infrastructure.Development.FictionalConfiguration;
using static Ilm.Infrastructure.Development.FictionalIds;

namespace Ilm.Infrastructure.Development;

/// <summary>Seeds fictional people, identities, links and migration states. Development and tests only.</summary>
public sealed class DevelopmentSeeder(IIlmDbContext db, ConfigurationService configuration, IAuditWriter audit, TimeProvider time)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await configuration.BootstrapAsync(Bootstrap(), cancellationToken);
        if (await db.Persons.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;

        var alex = Person("Alex Example", "Finance", "Alpha Unit");
        Link(alex, Okta(OktaAlex, "alex.example@example.test"), EvidenceType.OktaImportMatch | EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);
        Link(alex, Ad(TargetConnector, "alex.example", PopulationTargetOkta, AccountType.Standard), EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);
        Migration(alex, SystemKind.Okta, MigrationStateKind.CutOver, "W0");

        var bailey = Person("Bailey Sample", "Engineering", "Alpha Unit");
        Link(bailey, Okta(OktaBailey, "bailey.sample@example.test"), EvidenceType.OktaImportMatch | EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);
        var baileyTarget = Ad(TargetConnector, "bailey.sample", PopulationTargetOkta, AccountType.Standard);
        Link(bailey, baileyTarget, EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);
        var baileyLegacy = Ad(LegacyAConnector, "bailey.sample", PopulationLegacyA, AccountType.Standard);
        Link(bailey, baileyLegacy, EvidenceType.MigrationMapping | EvidenceType.HumanAttestation, LinkConfidence.HumanApproved, now, counterpart: baileyTarget);
        Migration(bailey, SystemKind.Okta, MigrationStateKind.ApprovedTransition, "W1", baileyLegacy, baileyTarget);

        var casey = Person("Casey Placeholder", "Sales", "Beta Unit");
        var caseyLegacy = Ad(LegacyAConnector, "casey.placeholder", PopulationLegacyA, AccountType.Standard);
        Link(casey, caseyLegacy, EvidenceType.MigrationMapping, LinkConfidence.Authoritative, now);
        Migration(casey, SystemKind.ActiveDirectory, MigrationStateKind.NotStarted, "W3", caseyLegacy);

        var dana = Person("Dana Fictional", "Operations", "Alpha Unit");
        Link(dana, Ad(TargetConnector, "dana.fictional", PopulationTargetDirect, AccountType.Standard), EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);
        Link(dana, Ad(TargetConnector, "adm-dana.fictional", PopulationTargetAdmin, AccountType.Administrative), EvidenceType.HumanAttestation | EvidenceType.TicketReference, LinkConfidence.HumanApproved, now);
        Migration(dana, SystemKind.ActiveDirectory, MigrationStateKind.CutOver, "W0");

        var emerson = Person("Emerson Test", "Infrastructure", "Beta Unit");
        Link(emerson, Ad(LegacyAConnector, "emerson.test", PopulationLegacyA, AccountType.Standard), EvidenceType.MigrationMapping, LinkConfidence.Authoritative, now);

        var finley = Person("Finley Demo", "Finance", "Alpha Unit");
        Link(finley, Okta(OktaFinley, "finley.demo@example.test"), EvidenceType.OktaImportMatch | EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);
        Link(finley, Ad(TargetConnector, "finley.demo", PopulationTargetOkta, AccountType.Standard), EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);

        var gray = Person("Gray Mock", "Marketing", "Beta Unit");
        Link(gray, Ad(LegacyBConnector, "gray.mock", PopulationLegacyB, AccountType.Standard), EvidenceType.MigrationMapping, LinkConfidence.Authoritative, now);

        var harper = Person("Harper Synthetic", "Engineering", "Alpha Unit");
        Link(harper, Okta(OktaHarper, "harper.synthetic@example.test"), EvidenceType.OktaImportMatch | EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);
        Link(harper, Ad(TargetConnector, "harper.synthetic", PopulationTargetOkta, AccountType.Standard), EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);
        Link(harper, Ad(LegacyBConnector, "hsynthetic", PopulationLegacyB, AccountType.Standard), EvidenceType.EmailAttribute, IdentityLinkPolicy.AutomaticConfidence(EvidenceType.EmailAttribute, 1), now);

        var jordan = Person("Jordan Unmapped", "Operations", "Gamma Unit");
        Link(jordan, Ad(TargetConnector, "jordan.unmapped", PopulationUnmapped, AccountType.Standard), EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);

        var kai = Person("Kai Privileged", "Infrastructure", "Alpha Unit");
        Link(kai, Ad(TargetConnector, "t0-kai.privileged", PopulationTargetAdmin, AccountType.Administrative), EvidenceType.HumanAttestation | EvidenceType.TicketReference, LinkConfidence.HumanApproved, now);

        var lee = Person("Lee Nested", "Infrastructure", "Alpha Unit");
        Link(lee, Ad(TargetConnector, "lee.nested", PopulationTargetAdmin, AccountType.Administrative), EvidenceType.HumanAttestation | EvidenceType.TicketReference, LinkConfidence.HumanApproved, now);

        var morgan = Person("Morgan Unknown", "Operations", "Alpha Unit");
        Link(morgan, Ad(TargetConnector, "morgan.unknown", PopulationTargetDirect, AccountType.Standard), EvidenceType.SourceAnchor, LinkConfidence.Authoritative, now);

        var riley = Person("Riley Legacy", "Infrastructure", "Beta Unit");
        Link(riley, Ad(LegacyAConnector, "adm-riley.legacy", PopulationLegacyA, AccountType.Administrative), EvidenceType.MigrationMapping, LinkConfidence.Authoritative, now);

        audit.Append(new AuditEvent { Action = "DevelopmentDataSeeded", Result = "Succeeded", RequestedValues = new { persons = 13, note = "Fictional example.test data" } }, ActorContext.System("development-seeder"));
        await db.SaveChangesAsync(cancellationToken);
    }

    private Person Person(string name, string department, string entity)
    {
        var p = new Person { DisplayName = name, Department = department, BusinessEntity = entity, PersonIdentifierSource = PersonIdentifierSource.IlmIssued };
        db.Persons.Add(p);
        return p;
    }

    private ExternalIdentity Ad(string connectorId, string sam, string population, AccountType type)
    {
        var guid = For($"{connectorId}:user:{sam}");
        var forest = connectorId == TargetConnector ? "corp.example.test" : $"{connectorId}.example.test";
        var identity = new ExternalIdentity
        {
            System = SystemKind.ActiveDirectory,
            ForestOrTenantId = forest,
            ConnectorId = connectorId,
            StableObjectId = guid.ToString("D"),
            ObjectGuid = guid,
            SamAccountName = sam,
            UserPrincipalName = connectorId == TargetConnector ? $"{sam}@example.test" : $"{sam}@{forest}",
            State = IdentityState.Unknown,
            AccountType = type,
            Population = population,
        };
        db.ExternalIdentities.Add(identity);
        return identity;
    }

    private ExternalIdentity Okta(string id, string login)
    {
        var identity = new ExternalIdentity
        {
            System = SystemKind.Okta,
            ForestOrTenantId = "okta.example.test",
            StableObjectId = id,
            OktaIssuer = "https://okta.example.test",
            OktaSubject = id,
            UserPrincipalName = login,
            Mail = login,
            State = IdentityState.Active,
            AccountType = AccountType.Standard,
            Population = PopulationOktaWorkforce,
        };
        db.ExternalIdentities.Add(identity);
        return identity;
    }

    private void Link(Person person, ExternalIdentity identity, EvidenceType evidence, LinkConfidence confidence, DateTime now, ExternalIdentity? counterpart = null)
    {
        identity.PersonId = person.Id;
        identity.BusinessEntity = person.BusinessEntity;
        db.IdentityLinks.Add(new IdentityLink
        {
            PersonId = person.Id,
            SourceIdentityId = counterpart?.Id,
            TargetIdentityId = identity.Id,
            LinkMethod = evidence.HasFlag(EvidenceType.MigrationMapping) ? LinkMethod.MigrationToolMapping
                : evidence.HasFlag(EvidenceType.SourceAnchor) ? LinkMethod.SourceAnchorMatch
                : evidence == EvidenceType.EmailAttribute ? LinkMethod.EmailMatch
                : LinkMethod.ManualAssertion,
            EvidenceType = evidence,
            EvidenceReference = "fictional-seed",
            Confidence = confidence,
            CreatedUtc = now,
            EffectiveFrom = now.AddDays(-30),
            ApprovedBy = confidence == LinkConfidence.HumanApproved ? "system:seed (fictional approval)" : null,
            ApprovedUtc = confidence == LinkConfidence.HumanApproved ? now : null,
        });
    }

    private void Migration(Person person, SystemKind authentication, MigrationStateKind state, string wave, ExternalIdentity? legacy = null, ExternalIdentity? target = null) =>
        db.MigrationStates.Add(new MigrationState
        {
            PersonId = person.Id,
            MigrationWave = wave,
            LegacyIdentityId = legacy?.Id,
            TargetIdentityId = target?.Id,
            AuthenticationAuthority = authentication,
            ProvisioningAuthority = authentication,
            AccountEnabledStateOwner = authentication,
            State = state,
        });
}
