namespace Atlas.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant is violated (e.g. an illegal state transition
/// on an aggregate). Distinct from validation exceptions, which belong to the
/// application layer — this is for rules the domain itself must never allow,
/// no matter which caller reaches it.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
