namespace Ilm.Domain.Directory;

/// <summary>userAccountControl bits used by ILM.</summary>
[Flags]
public enum UserAccountControl
{
    None = 0,
    AccountDisable = 0x2,
    PasswordNotRequired = 0x20,
    NormalAccount = 0x200,
    WorkstationTrustAccount = 0x1000,
    ServerTrustAccount = 0x2000,
    DontExpirePassword = 0x10000,
    TrustedForDelegation = 0x80000,
    NotDelegated = 0x100000,
    TrustedToAuthForDelegation = 0x1000000,
    PartialSecretsAccount = 0x4000000,
}

public static class UserAccountControlExtensions
{
    public static bool IsDisabled(int value) => (value & (int)UserAccountControl.AccountDisable) != 0;

    /// <summary>Containment may only ever set the disable bit. Returns the new value.</summary>
    public static int WithDisableBitSet(int value) => value | (int)UserAccountControl.AccountDisable;
}
