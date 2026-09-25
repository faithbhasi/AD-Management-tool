using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Ilm.Domain.Configuration;

namespace Ilm.Infrastructure.Directory.Ldap;

/// <summary>
/// Creates LDAP connections bound as the process identity (the gMSA in production): no stored password.
/// Transport is LDAPS with certificate chain and host-name validation, or Kerberos sign-and-seal.
/// </summary>
public sealed class LdapConnectionFactory
{
    public LdapConnection Create(DirectoryConnectorDefinition definition, string domainController)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var port = definition.UseLdaps ? 636 : 389;
        var identifier = new LdapDirectoryIdentifier(domainController, port, fullyQualifiedDnsHostName: true, connectionless: false);

        // No NetworkCredential: Negotiate uses the process identity (the gMSA). No bind password exists anywhere.
        var connection = new LdapConnection(identifier, credential: (NetworkCredential?)null, AuthType.Negotiate)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(definition.TimeoutSeconds, 5, 120)),
            AutoBind = true,
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (definition.UseLdaps)
        {
            connection.SessionOptions.SecureSocketLayer = true;
            if (OperatingSystem.IsWindows())
            {
                connection.SessionOptions.VerifyServerCertificate = (_, certificate) => ValidateCertificate(certificate, domainController);
            }
        }
        else
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Kerberos sign-and-seal is only supported on Windows hosts; use LDAPS.");
            }

            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
        }

        return connection;
    }

    /// <summary>Chain (with revocation) and host-name validation for LDAPS.</summary>
    public static bool ValidateCertificate(X509Certificate certificate, string expectedHost)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using var cert = new X509Certificate2(certificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        return chain.Build(cert) && cert.MatchesHostname(expectedHost);
    }
}
