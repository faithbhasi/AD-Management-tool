namespace Ilm.Domain.Protection;

public static class WellKnownSids
{
    public const string BuiltinAdministrators = "S-1-5-32-544";
    public const string AccountOperators = "S-1-5-32-548";
    public const string ServerOperators = "S-1-5-32-549";
    public const string PrintOperators = "S-1-5-32-550";
    public const string BackupOperators = "S-1-5-32-551";
    public const string Replicator = "S-1-5-32-552";

    public const int RidAdministrator = 500;
    public const int RidKrbtgt = 502;
    public const int RidEnterpriseReadOnlyDomainControllers = 498;
    public const int RidDomainAdmins = 512;
    public const int RidDomainControllers = 516;
    public const int RidCertPublishers = 517;
    public const int RidSchemaAdmins = 518;
    public const int RidEnterpriseAdmins = 519;
    public const int RidGroupPolicyCreatorOwners = 520;
    public const int RidReadOnlyDomainControllers = 521;
    public const int RidKeyAdmins = 526;
    public const int RidEnterpriseKeyAdmins = 527;

    /// <summary>Returns the RID of a domain SID (S-1-5-21-a-b-c-RID), or null for other SIDs.</summary>
    public static int? DomainRid(string? sid)
    {
        if (string.IsNullOrWhiteSpace(sid) || !sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = sid.Split('-');
        if (parts.Length != 8)
        {
            return null;
        }

        return int.TryParse(parts[^1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rid)
            ? rid
            : null;
    }

    /// <summary>Returns the domain part of a domain SID (without the RID).</summary>
    public static string? DomainPart(string? sid)
    {
        if (DomainRid(sid) is null)
        {
            return null;
        }

        return sid![..sid!.LastIndexOf('-')];
    }
}
