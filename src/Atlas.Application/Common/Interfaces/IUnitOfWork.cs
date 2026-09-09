namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Wraps DbContext.SaveChangesAsync so application services depend on an
/// abstraction rather than EF Core directly. One unit of work per HTTP request.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
