namespace Ilm.Domain.Common;

/// <summary>Systems that can own identity state or hold sessions.</summary>
public enum SystemKind
{
    None = 0,
    ActiveDirectory,
    Okta,
    Entra,
    Exchange,
    Citrix,
    Mimecast,
    Vpn,
    Application,
    Ilm,
    Manual,
}
