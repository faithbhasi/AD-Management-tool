using Ilm.Domain.Common;

namespace Ilm.Domain.Identity;

/// <summary>An account or identity record in a single system (AD forest, Okta org, Entra tenant).</summary>
public sealed class ExternalIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The person this identity is currently associated with, if any link exists.</summary>
    public Guid? PersonId { get; set; }

    public SystemKind System { get; set; }

    public string ForestOrTenantId { get; set; } = string.Empty;

    /// <summary>The configured connector that reads this identity (AD only).</summary>
    public string? ConnectorId { get; set; }

    /// <summary>The system's own immutable ID (objectGUID for AD, user ID for Okta, object ID for Entra).</summary>
    public string StableObjectId { get; set; } = string.Empty;

    public Guid? ObjectGuid { get; set; }

    public string? ObjectSid { get; set; }

    /// <summary>Last observed DN. Informational; operations always target <see cref="ObjectGuid"/>.</summary>
    public string? DistinguishedName { get; set; }

    public string? SamAccountName { get; set; }

    public string? UserPrincipalName { get; set; }

    public string? Mail { get; set; }

    public string? OktaIssuer { get; set; }

    public string? OktaSubject { get; set; }

    public string? EntraObjectId { get; set; }

    public string? SourceAnchor { get; set; }

    public IdentityState State { get; set; } = IdentityState.Unknown;

    public DateTime? LastReconciledUtc { get; set; }

    public AccountType AccountType { get; set; } = AccountType.Standard;

    /// <summary>Population key used by authority rules, for example "target-okta-standard".</summary>
    public string Population { get; set; } = string.Empty;

    public string? BusinessEntity { get; set; }

    public string? MigrationWave { get; set; }

    public string DisplayLabel =>
        SamAccountName ?? UserPrincipalName ?? OktaSubject ?? StableObjectId;
}

public enum IdentityState
{
    Unknown = 0,
    Active,
    Disabled,
    Staged,
    Suspended,
    Deprovisioned,
    Deleted,
    NotFound,
}
