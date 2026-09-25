using Ilm.Domain.Common;
using Ilm.Domain.Identity;

namespace Ilm.UnitTests.Identity;

public sealed class IdentityLinkPolicyTests
{
    [Fact]
    public void Email_only_match_is_provisional() =>
        Assert.Equal(LinkConfidence.Provisional, IdentityLinkPolicy.AutomaticConfidence(EvidenceType.EmailAttribute, 1));

    [Fact]
    public void Name_only_match_is_ambiguous_and_can_never_be_approved()
    {
        Assert.Equal(LinkConfidence.Ambiguous, IdentityLinkPolicy.AutomaticConfidence(EvidenceType.NameSimilarity, 1));
        var link = new IdentityLink { EvidenceType = EvidenceType.NameSimilarity | EvidenceType.TicketReference, Confidence = LinkConfidence.Ambiguous, CreatedByUserId = Guid.NewGuid() };
        Assert.NotNull(IdentityLinkPolicy.ApprovalBlocker(link, Guid.NewGuid()));
        Assert.Throws<DomainException>(() => IdentityLinkPolicy.Approve(link, Guid.NewGuid(), "approver", DateTime.UtcNow));
    }

    [Fact]
    public void Multiple_candidates_are_ambiguous() =>
        Assert.Equal(LinkConfidence.Ambiguous, IdentityLinkPolicy.AutomaticConfidence(EvidenceType.SourceAnchor, 2));

    [Fact]
    public void Strong_system_evidence_is_authoritative() =>
        Assert.Equal(LinkConfidence.Authoritative, IdentityLinkPolicy.AutomaticConfidence(EvidenceType.MigrationMapping, 1));

    [Fact]
    public void Employee_number_without_hr_source_stays_provisional() =>
        Assert.Equal(LinkConfidence.Provisional, IdentityLinkPolicy.AutomaticConfidence(EvidenceType.EmployeeRecord, 1));

    [Fact]
    public void Proposer_cannot_approve_own_link()
    {
        var proposer = Guid.NewGuid();
        var link = new IdentityLink { EvidenceType = EvidenceType.EmailAttribute, Confidence = LinkConfidence.Provisional, CreatedByUserId = proposer };
        Assert.NotNull(IdentityLinkPolicy.ApprovalBlocker(link, proposer));
    }

    [Fact]
    public void Email_link_becomes_human_approved_only_with_a_second_person()
    {
        var link = new IdentityLink { EvidenceType = EvidenceType.EmailAttribute, Confidence = LinkConfidence.Provisional, CreatedByUserId = Guid.NewGuid() };
        IdentityLinkPolicy.Approve(link, Guid.NewGuid(), "approver", DateTime.UtcNow);
        Assert.Equal(LinkConfidence.HumanApproved, link.Confidence);
        Assert.True(link.EvidenceType.HasFlag(EvidenceType.HumanAttestation));
    }

    [Theory]
    [InlineData(LinkConfidence.Authoritative, true)]
    [InlineData(LinkConfidence.HumanApproved, true)]
    [InlineData(LinkConfidence.Provisional, false)]
    [InlineData(LinkConfidence.Ambiguous, false)]
    [InlineData(LinkConfidence.Rejected, false)]
    public void Only_confirmed_links_are_contained_automatically(LinkConfidence confidence, bool eligible) =>
        Assert.Equal(eligible, IdentityLinkPolicy.IsContainmentEligible(confidence));

    [Fact]
    public void Person_identifier_is_ilm_issued_not_invented_hr_id()
    {
        var person = new Person();
        Assert.StartsWith("ILM-P-", person.PersonIdentifier, StringComparison.Ordinal);
        Assert.Equal(PersonIdentifierSource.IlmIssued, person.PersonIdentifierSource);
        Assert.Null(person.EmployeeIdentifier);
    }
}
