using Ilm.Domain.Configuration;
using Ilm.Domain.Directory;
using Ilm.Domain.Protection;

namespace Ilm.UnitTests.Protection;

public sealed class ProtectionFloorTests
{
    private const string Domain = "S-1-5-21-1111-2222-3333";

    private static readonly PlatformIdentities Platform = new()
    {
        RuntimeIdentitySids = [Domain + "-1201"],
        HostComputerSids = [Domain + "-1202"],
        HostDnsNames = ["ilm-web01.corp.example.test"],
        DatabaseServiceIdentitySids = [Domain + "-1203"],
        BreakGlassSids = [Domain + "-1204"],
        ManagedPasswordRetrieverSids = [Domain + "-1102"],
        Complete = true,
    };

    private static DirectoryObjectFacts Facts(string? sid = null, int? adminCount = 0, int uac = 512, IReadOnlyCollection<string>? groups = null,
        bool complete = true, bool membershipComplete = true, string[]? classes = null, string? sam = "user1", string? dns = null, int primaryGroup = 513) => new()
        {
            ConnectorId = "corp",
            ForestId = "corp.example.test",
            ObjectGuid = Guid.NewGuid(),
            ObjectSid = sid ?? Domain + "-5001",
            SamAccountName = sam,
            DnsHostName = dns,
            ObjectClasses = classes ?? ["top", "person", "user"],
            UserAccountControl = uac,
            AdminCount = adminCount,
            PrimaryGroupId = primaryGroup,
            AttributesComplete = complete,
            TransitiveGroupSids = groups ?? [],
            MembershipComplete = membershipComplete,
        };

    public static TheoryData<string> FloorSids() => new()
    {
        "S-1-5-32-544", Domain + "-512", Domain + "-518", Domain + "-519", Domain + "-498", Domain + "-516",
        Domain + "-500", Domain + "-502", Domain + "-521", Domain + "-526", Domain + "-527", "S-1-5-32-548", "S-1-5-32-551",
    };

    [Theory]
    [MemberData(nameof(FloorSids))]
    public void Hardcoded_sid_is_protected_as_object_and_as_group(string sid)
    {
        Assert.True(ProtectionFloor.IsFloorSid(sid));
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(sid: sid), [], Platform).Status);
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(groups: [sid]), [], Platform).Status);
    }

    [Fact]
    public void Hardcoded_floor_cannot_be_removed_by_configuration()
    {
        // The configuration schema has no exclusion field; even an empty additions list and an otherwise
        // valid document leave the floor in force.
        var emptyConfig = new IlmConfigurationDocument();
        Assert.DoesNotContain(typeof(IlmConfigurationDocument).GetProperties(), p => p.Name.Contains("Exclu", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Remov", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(emptyConfig.ProtectionAdditions);
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(groups: [Domain + "-512"]), [], Platform).Status);
    }

    [Fact]
    public void Floor_source_cannot_be_declared_in_configuration()
    {
        var doc = new IlmConfigurationDocument { ProtectionAdditions = [new() { MatchOn = ProtectedObjectMatch.Sid, Value = Domain + "-512", Category = ProtectionCategory.Tier0Group, Source = ProtectionSource.HardcodedFloor }] };
        Assert.Contains(ConfigurationValidator.Validate(doc, Platform, DateTime.UtcNow), i => i.Code == "PROTECTION_SOURCE");
    }

    [Fact]
    public void AdminCount_object_is_denied() =>
        Assert.Contains(ProtectionEvaluator.Evaluate(Facts(adminCount: 1), [], Platform).Reasons, r => r.Category == ProtectionCategory.ProtectedByAdminCount);

    [Fact]
    public void Recursive_protected_membership_is_denied()
    {
        var decision = ProtectionEvaluator.Evaluate(Facts(groups: [Domain + "-1101", Domain + "-512"]), [], Platform);
        Assert.False(decision.AllowsAutomation);
        Assert.Contains(decision.Reasons, r => r.Category == ProtectionCategory.Tier0Group);
    }

    [Fact]
    public void Foreign_security_principal_membership_in_other_forest_is_denied()
    {
        // A legacy-forest object whose SID appears as an FSP inside a target-forest Domain Admins group.
        var decision = ProtectionEvaluator.Evaluate(Facts(sid: "S-1-5-21-9-9-9-1310", groups: ["S-1-5-21-1000000001-1000000002-1000000003-512"]), [], Platform);
        Assert.Equal(ProtectionStatus.Protected, decision.Status);
    }

    [Fact]
    public void Runtime_identity_is_denied() =>
        Assert.Contains(ProtectionEvaluator.Evaluate(Facts(sid: Domain + "-1201"), [], Platform).Reasons, r => r.Category == ProtectionCategory.PortalRuntimeIdentity);

    [Fact]
    public void Gmsa_password_retriever_is_protected_directly_and_through_group()
    {
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(sid: Domain + "-1102"), [], Platform).Status);
        Assert.Contains(ProtectionEvaluator.Evaluate(Facts(groups: [Domain + "-1102"]), [], Platform).Reasons, r => r.Category == ProtectionCategory.ManagedPasswordRetriever);
    }

    [Fact]
    public void Portal_host_database_identity_and_break_glass_are_protected()
    {
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(dns: "ILM-WEB01.corp.example.test", sam: "ILM-WEB01$"), [], Platform).Status);
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(sid: Domain + "-1203"), [], Platform).Status);
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(sid: Domain + "-1204"), [], Platform).Status);
    }

    [Fact]
    public void Protected_servers_are_denied()
    {
        Assert.Contains(ProtectionEvaluator.Evaluate(Facts(uac: 8192 | 524288, primaryGroup: 516, sam: "DC01$"), [], Platform).Reasons, r => r.Category == ProtectionCategory.DomainController);
        Assert.Contains(ProtectionEvaluator.Evaluate(Facts(uac: 4096 | 524288, sam: "APP01$"), [], Platform).Reasons, r => r.Category == ProtectionCategory.UnconstrainedDelegation);
    }

    [Fact]
    public void Attack_path_import_adds_protection()
    {
        var target = Facts(sid: Domain + "-4001", dns: "pki01.corp.example.test");
        Assert.Equal(ProtectionStatus.Clear, ProtectionEvaluator.Evaluate(target, [], Platform).Status);
        var additions = new[] { new ProtectedObjectEntry(ProtectedObjectMatch.Sid, Domain + "-4001", ProtectionCategory.DcSyncRights, ProtectionSource.ImportedAttackPath, "analysis-1") };
        var decision = ProtectionEvaluator.Evaluate(target, additions, Platform);
        Assert.Equal(ProtectionStatus.Protected, decision.Status);
        Assert.Contains(decision.Reasons, r => r.Source == ProtectionSource.ImportedAttackPath && r.Category == ProtectionCategory.DcSyncRights);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Unknown_protection_state_is_denied(bool attributes, bool membership, bool platform)
    {
        var decision = ProtectionEvaluator.Evaluate(Facts(complete: attributes, membershipComplete: membership), [], Platform with { Complete = platform });
        Assert.Equal(ProtectionStatus.Unknown, decision.Status);
        Assert.False(decision.AllowsAutomation);
    }

    [Fact]
    public void Protected_dominates_unknown()
    {
        var decision = ProtectionEvaluator.Evaluate(Facts(adminCount: 1, membershipComplete: false), [], Platform);
        Assert.Equal(ProtectionStatus.Protected, decision.Status);
    }

    [Fact]
    public void Primary_group_in_floor_is_protected_even_without_member_attribute() =>
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(primaryGroup: 512), [], Platform).Status);

    [Fact]
    public void Managed_service_accounts_and_sync_identities_are_protected()
    {
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(classes: ["top", "msDS-GroupManagedServiceAccount"]), [], Platform).Status);
        Assert.Equal(ProtectionStatus.Protected, ProtectionEvaluator.Evaluate(Facts(sam: "MSOL_0123456789ab"), [], Platform).Status);
    }

    [Fact]
    public void Ordinary_user_is_clear() =>
        Assert.Equal(ProtectionStatus.Clear, ProtectionEvaluator.Evaluate(Facts(), [], Platform).Status);

    [Fact]
    public void Tier0_operations_cannot_be_enabled_through_configuration()
    {
        var doc = new IlmConfigurationDocument { FeatureFlags = new() { ["Tier0Management"] = true, ["ManageDomainAdmins"] = true } };
        var issues = ConfigurationValidator.Validate(doc, Platform, DateTime.UtcNow);
        Assert.Equal(2, issues.Count(i => i.Code == "FEATURE_UNKNOWN"));
        Assert.DoesNotContain(Enum.GetNames<Feature>(), n => n.Contains("Tier0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Uac_helpers_only_set_the_disable_bit()
    {
        Assert.Equal(514, UserAccountControlExtensions.WithDisableBitSet(512));
        Assert.Equal(514, UserAccountControlExtensions.WithDisableBitSet(514));
        Assert.True(UserAccountControlExtensions.IsDisabled(66050));
    }
}
