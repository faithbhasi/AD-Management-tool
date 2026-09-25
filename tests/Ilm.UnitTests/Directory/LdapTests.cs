using System.DirectoryServices.Protocols;
using Ilm.Domain.Common;
using Ilm.Infrastructure.Directory.Ldap;

namespace Ilm.UnitTests.Directory;

public sealed class LdapTests
{
    [Theory]
    [InlineData("alice", "alice")]
    [InlineData("a*", @"a\2a")]
    [InlineData("(admin)", @"\28admin\29")]
    [InlineData(@"back\slash", @"back\5cslash")]
    [InlineData("nul\0", @"nul\00")]
    [InlineData("*)(|(objectClass=*", @"\2a\29\28|\28objectClass=\2a")]
    public void Filter_values_are_escaped(string input, string expected) => Assert.Equal(expected, LdapFilter.Escape(input));

    [Fact]
    public void Injection_attempt_cannot_change_filter_structure()
    {
        var filter = LdapFilter.Prefix("sAMAccountName", "x*)(objectClass=*");
        Assert.Equal(@"(sAMAccountName=x\2a\29\28objectClass=\2a*)", filter);
        // Only the outer pair of parentheses is unescaped; the payload cannot open or close a clause.
        Assert.Equal(1, filter.Count(c => c == '('));
        Assert.Equal(1, filter.Count(c => c == ')'));
    }

    [Fact]
    public void Attribute_names_are_validated() => Assert.Throws<ArgumentException>(() => LdapFilter.Equal("cn)(x", "y"));

    [Fact]
    public void In_chain_filter_uses_matching_rule() =>
        Assert.Equal(@"(member:1.2.840.113556.1.4.1941:=CN=A\28B\29,DC=x)", LdapFilter.GroupsContainingInChain("CN=A(B),DC=x"));

    [Fact]
    public void Guid_is_escaped_as_binary() =>
        Assert.Equal(@"(objectGUID=\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00)", LdapFilter.ObjectGuid(Guid.Empty));

    [Fact]
    public void Rdn_values_are_escaped() => Assert.Equal(@"Smith\, John \+ Co\=op", LdapDn.EscapeRdnValue("Smith, John + Co=op"));

    [Fact]
    public void Sid_reference_rejects_injection() => Assert.Throws<ArgumentException>(() => LdapDn.SidReference("S-1-5-21-1>,(cn=*)"));

    [Fact]
    public void Sid_binary_converts_to_string()
    {
        byte[] sid = [1, 5, 0, 0, 0, 0, 0, 5, 21, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0, 2, 0, 0];
        Assert.Equal("S-1-5-21-1-2-3-512", SidConverter.ToSddlString(sid));
    }

    [Theory]
    [InlineData(ResultCode.EntryAlreadyExists, null, SafeErrorCategory.DuplicateObject)]
    [InlineData(ResultCode.ConstraintViolation, "00002071: name exists", SafeErrorCategory.DuplicateObject)]
    [InlineData(ResultCode.UnwillingToPerform, "0000052D: SvcErr: DSID-031A12D2, problem 5003", SafeErrorCategory.ConstraintViolation)]
    [InlineData(ResultCode.InsufficientAccessRights, null, SafeErrorCategory.InsufficientDirectoryRights)]
    [InlineData(ResultCode.NoSuchAttribute, null, SafeErrorCategory.ConcurrencyConflict)]
    [InlineData(ResultCode.NoSuchObject, null, SafeErrorCategory.NotFound)]
    public void Native_errors_map_to_safe_categories(ResultCode code, string? message, SafeErrorCategory expected) =>
        Assert.Equal(expected, LdapErrorClassifier.Classify(code, message));

    [Fact]
    public void Ad_time_formats_parse()
    {
        Assert.Equal(new DateTime(2026, 9, 25, 11, 2, 54, DateTimeKind.Utc), LdapDirectoryConnector.GeneralizedTime("20260925110254.0Z"));
        Assert.Null(LdapDirectoryConnector.FileTime("0"));
        Assert.NotNull(LdapDirectoryConnector.FileTime("134036000000000000"));
    }

    [Fact]
    public void Safe_attribute_allowlist_excludes_secrets()
    {
        foreach (var forbidden in SafeAttributeAllowlist.Forbidden)
        {
            Assert.DoesNotContain(forbidden, SafeAttributeAllowlist.Object, StringComparer.OrdinalIgnoreCase);
        }
    }
}
