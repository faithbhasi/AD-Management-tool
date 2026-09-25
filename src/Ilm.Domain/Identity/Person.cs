namespace Ilm.Domain.Identity;

/// <summary>
/// A human being, separate from any account they hold. ILM never invents an HR identifier:
/// <see cref="PersonIdentifier"/> is ILM-issued unless an authoritative source supplies one.
/// </summary>
public sealed class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PersonIdentifier { get; set; } = NewIlmIdentifier();

    public PersonIdentifierSource PersonIdentifierSource { get; set; } = PersonIdentifierSource.IlmIssued;

    /// <summary>Optional employee number. Informational only; never used as an automatic link key.</summary>
    public string? EmployeeIdentifier { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public LifecycleStatus LifecycleStatus { get; set; } = LifecycleStatus.Active;

    public string? BusinessEntity { get; set; }

    public string? Department { get; set; }

    public Guid? ManagerPersonId { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public static string NewIlmIdentifier() =>
        "ILM-P-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}

public enum PersonIdentifierSource
{
    IlmIssued = 0,
    HrSystem,
    ManualAssertion,
}

public enum LifecycleStatus
{
    Active = 0,
    Joining,
    LeaverRequested,
    Contained,
    Left,
    Unknown,
}
