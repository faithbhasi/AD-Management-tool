namespace Ilm.Domain.Feasibility;

public enum FeasibilityRecommendation
{
    NotAssessed = 0,
    Go,
    ConditionalGo,
    NoGo,
}

public enum FeasibilityRunStatus
{
    Draft = 0,
    AwaitingApproval,
    Approved,
    Rejected,
}

public enum FeasibilityMode
{
    Mock = 0,
    Pcatest,
}

/// <summary>One execution of the Okta-to-AD feasibility harness and the report it produced.</summary>
public sealed class FeasibilityRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public FeasibilityMode Mode { get; set; }

    public string Environment { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public string ResultsJson { get; set; } = "[]";

    public string ReportMarkdown { get; set; } = string.Empty;

    public string ReportSha256 { get; set; } = string.Empty;

    public FeasibilityRecommendation Recommendation { get; set; }

    public FeasibilityRunStatus Status { get; set; }

    public Guid StartedByUserId { get; set; }

    public Guid? ApprovalId { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ApprovedUtc { get; set; }
}
