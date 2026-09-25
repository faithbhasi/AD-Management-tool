using Ilm.Domain.Directory;
using Ilm.Infrastructure.Directory.Mock;
using static Ilm.Infrastructure.Development.FictionalIds;

namespace Ilm.Infrastructure.Development;

/// <summary>Builds three fictional forests under example.test that cover every containment scenario.</summary>
public static class FictionalDirectory
{
    public static void Populate(InMemoryDirectoryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        lock (store.Gate)
        {
            if (store.HasForest(TargetConnector))
            {
                return;
            }

            BuildTarget(store.AddForest(TargetConnector, "corp.example.test", "corp.example.test", TargetDomainSid, "dc01.corp.example.test", "dc02.corp.example.test"));
            BuildLegacyA(store.AddForest(LegacyAConnector, "legacy-a.example.test", "legacy-a.example.test", LegacyADomainSid, "lgadc01.legacy-a.example.test"));
            BuildLegacyB(store.AddForest(LegacyBConnector, "legacy-b.example.test", "legacy-b.example.test", LegacyBDomainSid, "lgbdc01.legacy-b.example.test"));
        }
    }

    private static void BuildTarget(MockForest f)
    {
        var root = f.DomainDn;
        var managed = Ou(f, CorpManagedOu, $"OU=ILM Managed,{root}");
        var staff = Ou(f, CorpStaffOu, $"OU=Staff,{managed}");
        var finance = Ou(f, CorpFinanceOu, $"OU=Finance,{staff}");
        var engineering = Ou(f, CorpEngineeringOu, $"OU=Engineering,{staff}");
        var admins = Ou(f, CorpAdminOu, $"OU=Admin Accounts,{managed}");
        var workstations = Ou(f, CorpWorkstationsOu, $"OU=Workstations,{managed}");
        var memberServers = Ou(f, CorpMemberServersOu, $"OU=Member Servers,{managed}");
        var groups = Ou(f, CorpGroupsOu, $"OU=Groups,{managed}");
        var tier0 = Ou(f, CorpTier0Ou, $"OU=Tier0,{root}");
        var platform = Ou(f, CorpPlatformOu, $"OU=Platform,{root}");
        var dcs = Ou(f, CorpDomainControllersOu, $"OU=Domain Controllers,{root}");
        var users = $"CN=Users,{root}";
        var builtin = $"CN=Builtin,{root}";
        var msa = $"CN=Managed Service Accounts,{root}";

        var domainAdmins = Group(f, "Domain Admins", users, f.DomainSid + "-512");
        var domainUsers = Group(f, "Domain Users", users, f.DomainSid + "-513");
        Group(f, "Domain Controllers", users, f.DomainSid + "-516");
        Group(f, "Schema Admins", users, f.DomainSid + "-518");
        var enterpriseAdmins = Group(f, "Enterprise Admins", users, f.DomainSid + "-519");
        Group(f, "Enterprise Read-only Domain Controllers", users, f.DomainSid + "-498");
        var administrators = Group(f, "Administrators", builtin, "S-1-5-32-544");
        administrators.Members.Add(domainAdmins.Guid);
        administrators.Members.Add(enterpriseAdmins.Guid);
        var serverAdminsT0 = Group(f, "Server Admins T0", tier0, f.DomainSid + "-1101");
        domainAdmins.Members.Add(serverAdminsT0.Guid);
        var retrievers = Group(f, "ILM gMSA Retrievers", platform, GmsaRetrieversGroupSid);
        var legalHold = Group(f, "Legal Hold", groups, f.DomainSid + "-1103", LegalHoldGroup);
        var wave1 = Group(f, "Migration Wave 1", groups, f.DomainSid + "-1104", MigrationWaveGroup);
        var financeStaff = Group(f, "Finance Staff", groups, f.DomainSid + "-1105", FinanceGroup);
        var engineeringStaff = Group(f, "Engineering Staff", groups, f.DomainSid + "-1106");

        // A legacy-forest principal nested into a Tier 0 group through a foreign security principal.
        var fsp = new MockObject
        {
            DistinguishedName = $"CN={LegacyADomainSid}-1310,CN=ForeignSecurityPrincipals,{root}",
            Kind = DirectoryObjectKind.ForeignSecurityPrincipal,
            ObjectClasses = ["top", "foreignSecurityPrincipal"],
            ForeignSid = LegacyADomainSid + "-1310",
        };
        f.Objects[fsp.Guid] = fsp;
        serverAdminsT0.Members.Add(fsp.Guid);

        User(f, "alex.example", "Alex Example", finance, "Finance", 512, groups: [financeStaff, domainUsers]);
        User(f, "bailey.sample", "Bailey Sample", engineering, "Engineering", 512, groups: [engineeringStaff, wave1, legalHold]);
        User(f, "dana.fictional", "Dana Fictional", staff, "Operations", 512);
        User(f, "adm-dana.fictional", "Dana Fictional (Admin)", admins, "Operations", 512);
        User(f, "finley.demo", "Finley Demo", finance, "Finance", 514);
        User(f, "harper.synthetic", "Harper Synthetic", engineering, "Engineering", 512);
        User(f, "jordan.unmapped", "Jordan Unmapped", staff, "Operations", 512);
        var kai = User(f, "t0-kai.privileged", "Kai Privileged (Tier 0)", tier0, "Infrastructure", 512, groups: [domainAdmins]);
        kai.AdminCount = 1;
        User(f, "lee.nested", "Lee Nested", admins, "Infrastructure", 512, groups: [serverAdminsT0]);
        var morgan = User(f, "morgan.unknown", "Morgan Unknown", staff, "Operations", 512);
        morgan.FailMembershipReads = true;
        User(f, "breakglass-01", "Break Glass 01", tier0, null, 512, sid: BreakGlassSid);
        User(f, "MSOL_a1b2c3d4e5f6", "Directory Sync Account", users, null, 66048);

        Gmsa(f, "svc-ilm$", msa, RuntimeGmsaSid);
        Gmsa(f, "svc-pgsql$", msa, DatabaseServiceSid);

        var ws1 = Computer(f, "WS-0001", workstations, "Windows 11 Enterprise", 4096, DateTime.UtcNow.AddDays(-2));
        ws1.Attributes["managedBy"] = f.FindByDn($"CN=Alex Example,{finance}")!.DistinguishedName;
        Computer(f, "WS-0002", workstations, "Windows 11 Enterprise", 4096, DateTime.UtcNow.AddDays(-120));
        Computer(f, "SRV-APP01", memberServers, "Windows Server 2025 Datacenter", 4096, DateTime.UtcNow.AddDays(-1));
        Computer(f, "CA01", memberServers, "Windows Server 2025 Datacenter", 4096, DateTime.UtcNow.AddDays(-1), sid: CertificateAuthoritySid);
        Computer(f, "OKTA-AGT01", memberServers, "Windows Server 2022 Standard", 4096, DateTime.UtcNow.AddDays(-1), dns: OktaAgentDns);
        var host = Computer(f, "ILM-WEB01", platform, "Windows Server 2025 Standard", 4096, DateTime.UtcNow.AddDays(-1), sid: PortalHostSid, dns: PortalHostDns);
        retrievers.Members.Add(host.Guid);
        var dc = Computer(f, "DC01", dcs, "Windows Server 2025 Datacenter", 8192 | 524288, DateTime.UtcNow.AddDays(-1));
        dc.PrimaryGroupId = 516;
    }

    private static void BuildLegacyA(MockForest f)
    {
        var root = f.DomainDn;
        var staff = Ou(f, LegacyAStaffOu, $"OU=Legacy Staff,{root}");
        var admins = Ou(f, LegacyAAdminOu, $"OU=Legacy Admins,{root}");
        Group(f, "Domain Admins", $"CN=Users,{root}", f.DomainSid + "-512");
        Group(f, "Domain Users", $"CN=Users,{root}", f.DomainSid + "-513");
        User(f, "casey.placeholder", "Casey Placeholder", staff, "Sales", 512);
        User(f, "bailey.sample", "Bailey Sample (legacy)", staff, "Engineering", 512);
        User(f, "emerson.test", "Emerson Test", staff, "Infrastructure", 512, sid: LegacyADomainSid + "-1310");
        User(f, "adm-riley.legacy", "Riley Legacy (Admin)", admins, "Infrastructure", 512);
    }

    private static void BuildLegacyB(MockForest f)
    {
        var root = f.DomainDn;
        var usersOu = Ou(f, LegacyBUsersOu, $"OU=Users Legacy B,{root}");
        Group(f, "Domain Admins", $"CN=Users,{root}", f.DomainSid + "-512");
        User(f, "gray.mock", "Gray Mock", usersOu, "Marketing", 512);
        User(f, "hsynthetic", "H. Synthetic", usersOu, "Engineering", 512, mailOverride: "harper.synthetic@example.test");
    }

    private static string Ou(MockForest f, Guid guid, string dn)
    {
        var o = new MockObject { Guid = guid, DistinguishedName = dn, Kind = DirectoryObjectKind.OrganizationalUnit, ObjectClasses = ["top", "organizationalUnit"] };
        o.Attributes["ou"] = dn.Split(',')[0][3..];
        f.Objects[guid] = o;
        return dn;
    }

    private static MockObject Group(MockForest f, string name, string container, string sid, Guid? guid = null)
    {
        var g = new MockObject
        {
            Guid = guid ?? For($"{f.ConnectorId}:group:{name}"),
            DistinguishedName = $"CN={name},{container}",
            Kind = DirectoryObjectKind.Group,
            ObjectClasses = ["top", "group"],
            Sid = sid,
        };
        g.Attributes["cn"] = name;
        g.Attributes["sAMAccountName"] = name;
        f.Objects[g.Guid] = g;
        return g;
    }

    private static MockObject User(MockForest f, string sam, string displayName, string container, string? department, int uac, MockObject[]? groups = null, string? sid = null, string? mailOverride = null)
    {
        var mailDomain = f.ConnectorId == TargetConnector ? "example.test" : f.DomainDns;
        var mail = mailOverride ?? $"{sam}@{mailDomain}";
        var u = new MockObject
        {
            Guid = For($"{f.ConnectorId}:user:{sam}"),
            DistinguishedName = $"CN={displayName},{container}",
            Kind = DirectoryObjectKind.User,
            ObjectClasses = ["top", "person", "organizationalPerson", "user"],
            Sid = sid ?? f.NewSid(),
            UserAccountControl = uac,
            AdminCount = 0,
            PrimaryGroupId = 513,
            LastLogonTimestampUtc = DateTime.UtcNow.AddDays(-3),
        };
        u.Attributes["cn"] = displayName;
        u.Attributes["sAMAccountName"] = sam;
        u.Attributes["userPrincipalName"] = f.ConnectorId == TargetConnector ? $"{sam}@example.test" : $"{sam}@{f.DomainDns}";
        u.Attributes["mail"] = mail;
        u.Attributes["displayName"] = displayName;
        u.Attributes["department"] = department;
        u.ProxyAddresses.Add("SMTP:" + mail);
        f.Objects[u.Guid] = u;
        foreach (var g in groups ?? [])
        {
            g.Members.Add(u.Guid);
        }

        return u;
    }

    private static void Gmsa(MockForest f, string sam, string container, string sid)
    {
        var g = new MockObject
        {
            Guid = For($"{f.ConnectorId}:gmsa:{sam}"),
            DistinguishedName = $"CN={sam.TrimEnd('$')},{container}",
            Kind = DirectoryObjectKind.ManagedServiceAccount,
            ObjectClasses = ["top", "person", "organizationalPerson", "user", "computer", "msDS-GroupManagedServiceAccount"],
            Sid = sid,
            UserAccountControl = 4096,
            PrimaryGroupId = 515,
        };
        g.Attributes["cn"] = sam.TrimEnd('$');
        g.Attributes["sAMAccountName"] = sam;
        f.Objects[g.Guid] = g;
    }

    private static MockObject Computer(MockForest f, string name, string container, string os, int uac, DateTime lastLogon, string? sid = null, string? dns = null)
    {
        var c = new MockObject
        {
            Guid = For($"{f.ConnectorId}:computer:{name}"),
            DistinguishedName = $"CN={name},{container}",
            Kind = DirectoryObjectKind.Computer,
            ObjectClasses = ["top", "person", "organizationalPerson", "user", "computer"],
            Sid = sid ?? f.NewSid(),
            UserAccountControl = uac,
            PrimaryGroupId = 515,
            LastLogonTimestampUtc = lastLogon,
        };
        c.Attributes["cn"] = name;
        c.Attributes["sAMAccountName"] = name + "$";
        c.Attributes["dNSHostName"] = dns ?? $"{name.ToLowerInvariant()}.{f.DomainDns}";
        c.Attributes["operatingSystem"] = os;
        c.Attributes["operatingSystemVersion"] = "10.0 (26100)";
        f.Objects[c.Guid] = c;
        return c;
    }
}
