namespace Ilm.Domain.Common;

/// <summary>A rule violation with a category that is safe to surface.</summary>
public sealed class DomainException : Exception
{
    public DomainException(SafeErrorCategory category, string message)
        : base(message)
    {
        Category = category;
    }

    public DomainException()
        : this(SafeErrorCategory.Unexpected, "Domain rule violated.")
    {
    }

    public DomainException(string message)
        : this(SafeErrorCategory.ValidationFailed, message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        Category = SafeErrorCategory.Unexpected;
    }

    public DomainException(SafeErrorCategory category, string message, Exception innerException)
        : base(message, innerException)
    {
        Category = category;
    }

    public SafeErrorCategory Category { get; }
}
