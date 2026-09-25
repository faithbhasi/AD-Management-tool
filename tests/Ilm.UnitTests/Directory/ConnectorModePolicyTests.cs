using Ilm.Domain.Directory;

namespace Ilm.UnitTests.Directory;

public sealed class ConnectorModePolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);

    private static ConnectorModeContext Ctx(ConnectorMode mode, ForestRole role, bool marker = false, DateTime? expiry = null, params DirectoryOperation[] ops) =>
        new(mode, role, marker, expiry, ops.ToHashSet(), Now);

    [Theory]
    [InlineData(DirectoryOperation.DisableAccount, true)]
    [InlineData(DirectoryOperation.VerifyAccountState, true)]
    [InlineData(DirectoryOperation.Search, true)]
    [InlineData(DirectoryOperation.CreateAccount, false)]
    [InlineData(DirectoryOperation.EnableAccount, false)]
    [InlineData(DirectoryOperation.ResetPassword, false)]
    [InlineData(DirectoryOperation.ModifyAttributes, false)]
    [InlineData(DirectoryOperation.AddGroupMember, false)]
    [InlineData(DirectoryOperation.RemoveGroupMember, false)]
    [InlineData(DirectoryOperation.MoveObject, false)]
    [InlineData(DirectoryOperation.DeleteObject, false)]
    [InlineData(DirectoryOperation.SetContainmentMarker, false)]
    public void Containment_only_legacy_allows_disable_and_verify_only(DirectoryOperation op, bool allowed) =>
        Assert.Equal(allowed, ConnectorModePolicy.Deny(Ctx(ConnectorMode.ContainmentOnlyLegacy, ForestRole.Legacy), op) is null);

    [Fact]
    public void Containment_marker_needs_separate_approval() =>
        Assert.Null(ConnectorModePolicy.Deny(Ctx(ConnectorMode.ContainmentOnlyLegacy, ForestRole.Legacy, marker: true), DirectoryOperation.SetContainmentMarker));

    [Fact]
    public void Read_only_modes_never_write()
    {
        foreach (var op in Enum.GetValues<DirectoryOperation>().Where(o => !ConnectorModePolicy.IsReadOperation(o)))
        {
            Assert.NotNull(ConnectorModePolicy.Deny(Ctx(ConnectorMode.ReadOnlyLegacy, ForestRole.Legacy), op));
            Assert.NotNull(ConnectorModePolicy.Deny(Ctx(ConnectorMode.ReadOnlyTarget, ForestRole.Target), op));
        }
    }

    [Fact]
    public void Legacy_forest_can_never_be_write_target()
    {
        Assert.False(ConnectorModePolicy.IsModeValidForRole(ConnectorMode.WriteTarget, ForestRole.Legacy));
        Assert.NotNull(ConnectorModePolicy.Deny(Ctx(ConnectorMode.WriteTarget, ForestRole.Legacy), DirectoryOperation.DisableAccount));
    }

    [Fact]
    public void Deletion_is_never_automated()
    {
        foreach (var mode in Enum.GetValues<ConnectorMode>())
        {
            Assert.NotNull(ConnectorModePolicy.Deny(Ctx(mode, ForestRole.Target, true, Now.AddDays(1), DirectoryOperation.DeleteObject), DirectoryOperation.DeleteObject));
        }
    }

    [Fact]
    public void Migration_exception_requires_unexpired_listed_operations()
    {
        Assert.NotNull(ConnectorModePolicy.Deny(Ctx(ConnectorMode.MigrationException, ForestRole.Legacy, expiry: Now.AddDays(-1), ops: DirectoryOperation.ModifyAttributes), DirectoryOperation.ModifyAttributes));
        Assert.Null(ConnectorModePolicy.Deny(Ctx(ConnectorMode.MigrationException, ForestRole.Legacy, expiry: Now.AddDays(5), ops: DirectoryOperation.ModifyAttributes), DirectoryOperation.ModifyAttributes));
        Assert.NotNull(ConnectorModePolicy.Deny(Ctx(ConnectorMode.MigrationException, ForestRole.Legacy, expiry: Now.AddDays(5), ops: DirectoryOperation.ModifyAttributes), DirectoryOperation.CreateAccount));
    }
}
