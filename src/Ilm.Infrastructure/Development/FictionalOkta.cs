using Ilm.Application.Okta;
using Ilm.Infrastructure.Directory.Mock;
using Ilm.Infrastructure.Okta.Mock;
using static Ilm.Infrastructure.Development.FictionalIds;

namespace Ilm.Infrastructure.Development;

public sealed record FictionalOperator(string Subject, string DisplayName, string Email, IReadOnlyList<string> GroupIds, IReadOnlyList<string> TokenGroupNames, string Description);

/// <summary>Fictional Okta users: operators for the mock OIDC provider, and people with Okta identities.</summary>
public static class FictionalOkta
{
    public static readonly IReadOnlyList<FictionalOperator> Operators =
    [
        new(OperatorOlivia, "Olivia Operator", "olivia.operator@example.test", [GroupLifecycleOperators, GroupReaders], ["ILM-LifecycleOperators"], "Lifecycle Operator: requests and executes leaver workflows."),
        new(ApproverAdrian, "Adrian Approver", "adrian.approver@example.test", [GroupLifecycleApprovers, GroupReaders], ["ILM-LifecycleApprovers"], "Lifecycle Approver: approves leaver requests."),
        new(SecuritySasha, "Sasha Security", "sasha.security@example.test", [GroupSecurityApprovers], ["ILM-SecurityApprovers"], "Security Approver: approves sensitive configuration, legacy containment and Tier 0 tasks."),
        new(ConfigCasey, "Casey Config", "casey.config@example.test", [GroupConfigurationAdministrators], ["ILM-ConfigAdmins"], "Configuration Administrator: proposes configuration."),
        new(AuditorAudrey, "Audrey Auditor", "audrey.auditor@example.test", [GroupAuditors], ["ILM-Auditors"], "Auditor: reads audit records."),
        new(DbaDale, "Dale DBA", "dale.dba@example.test", [], [], "Database Administrator: no lifecycle role."),
        new(DecoyMallory, "Mallory Renamed", "mallory.renamed@example.test", [GroupDecoy], ["ILM-SecurityApprovers"], "Holds an unmapped group renamed to look like 'ILM-SecurityApprovers'; gets no role."),
    ];

    public static void Populate(MockOktaOrg org)
    {
        ArgumentNullException.ThrowIfNull(org);
        if (org.Users.ContainsKey(OktaAlex))
        {
            return;
        }

        foreach (var op in Operators)
        {
            var parts = op.DisplayName.Split(' ');
            var user = org.AddUser(op.Subject, op.Email, parts[0], parts[^1], OktaUserStatus.Active);
            foreach (var g in op.GroupIds)
            {
                user.GroupIds.Add(g);
            }
        }

        AddPerson(org, OktaAlex, "alex.example@example.test", "Alex", "Example", "Finance", CorpUser("alex.example"));
        AddPerson(org, OktaBailey, "bailey.sample@example.test", "Bailey", "Sample", "Engineering", CorpUser("bailey.sample"));
        AddPerson(org, OktaFinley, "finley.demo@example.test", "Finley", "Demo", "Finance", CorpUser("finley.demo"));
        AddPerson(org, OktaHarper, "harper.synthetic@example.test", "Harper", "Synthetic", "Engineering", CorpUser("harper.synthetic"));

        org.AdIntegration.TargetConnectorId = TargetConnector;
        org.AdIntegration.ProvisioningGroupIds.Add(GroupAdProvisioning);
        org.AdIntegration.OuByDepartment["Finance"] = CorpFinanceOu;
        org.AdIntegration.OuByDepartment["Engineering"] = CorpEngineeringOu;
        org.AdIntegration.DefaultOuGuid = CorpStaffOu;
    }

    public static Guid CorpUser(string sam) => For($"{TargetConnector}:user:{sam}");

    private static void AddPerson(MockOktaOrg org, string id, string login, string first, string last, string department, Guid adGuid)
    {
        var user = org.AddUser(id, login, first, last, OktaUserStatus.Active, department);
        user.GroupIds.Add(GroupAdProvisioning);
        user.AdObjectGuid = adGuid;
    }

    /// <summary>Creates the in-memory directory and Okta org together (the Okta agent pushes into the directory).</summary>
    public static (InMemoryDirectoryStore Directory, MockOktaOrg Okta) CreateEnvironment()
    {
        var directory = new InMemoryDirectoryStore();
        FictionalDirectory.Populate(directory);
        var okta = new MockOktaOrg(directory);
        Populate(okta);
        return (directory, okta);
    }
}
