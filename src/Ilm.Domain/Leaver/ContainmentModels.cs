namespace Ilm.Domain.Leaver;

public enum ContainmentStep
{
    AuthenticationContainment = 1,
    SessionRevocation = 2,
    DirectoryDisable = 3,
    Verification = 4,
}

public enum ContainmentMethod
{
    OktaApi = 0,
    ContainmentOnlyLegacy,
    DirectActiveDirectory,
    VerifyOnly,
    SessionConnector,
    ManualControlled,
    EmergencyOverride,
}

public enum ContainmentActionStatus
{
    Pending = 0,
    InProgress,
    Succeeded,
    Verified,
    Failed,
    Unsupported,
    NotConfigured,
    ManualActionRequired,
    Unknown,
    Skipped,
}

public enum SessionSystem
{
    Okta = 0,
    Entra,
    Citrix,
    Vpn,
    Application,
}

/// <summary>Outcome categories every session connector must return.</summary>
public enum SessionRevocationStatus
{
    Succeeded = 0,
    Failed,
    Unsupported,
    NotConfigured,
    ManualActionRequired,
    Unknown,
}

public enum SessionRequirement
{
    Mandatory = 0,
    BestEffort,
    NotApplicable,
}

public enum OktaContainmentAction
{
    Deactivate = 0,
    Suspend,
}
