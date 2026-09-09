namespace Atlas.Domain.Common;

/// <summary>
/// Base class for every entity in the domain model. Provides identity-based
/// equality (two entities are equal when their <see cref="Id"/> matches, regardless
/// of what other state has changed) which is the correct equality semantics for
/// entities as opposed to value objects.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>UTC timestamp set when the row is first persisted.</summary>
    public DateTime CreatedAtUtc { get; protected set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp updated every time the entity is modified. Null until the first update.</summary>
    public DateTime? ModifiedAtUtc { get; protected set; }

    /// <summary>Call from within domain methods that mutate state, so ModifiedAtUtc is never forgotten.</summary>
    protected void MarkModified() => ModifiedAtUtc = DateTime.UtcNow;

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id == other.Id;
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
