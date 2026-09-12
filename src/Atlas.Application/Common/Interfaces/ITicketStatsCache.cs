using Atlas.Application.Tickets.Dtos;

namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Cache-aside port for GET /api/tickets/stats (Del 13) — one cached entry
/// per organization, not a generic ICache&lt;TKey,TValue&gt;. Same reasoning
/// as ITicketRepository's own doc comment about avoiding a generic
/// IRepository&lt;T&gt;: a generic cache abstraction would just push
/// key-naming, serialization and TTL decisions out to every call site
/// instead of owning them in exactly one place — Atlas.Infrastructure's
/// Redis implementation, see RedisTicketStatsCache.
///
/// Implementations must be "fail open": a Redis outage should degrade this
/// feature back to "every request hits the database and computes the
/// aggregate itself", never turn a perfectly healthy database read into a
/// 500 just because the cache in front of it is unavailable. See
/// RedisTicketStatsCache's own doc comment for exactly how that's enforced —
/// the same "an optional dependency failing must never fail the request"
/// principle Del 12's Service Bus publish already follows in
/// TicketService.AssignAsync.
/// </summary>
public interface ITicketStatsCache
{
    /// <summary>
    /// Returns the cached stats for this organization, or null on a cache
    /// miss — which includes both "nothing cached yet / TTL expired" and
    /// "Redis itself is unreachable right now"; callers (TicketService)
    /// treat both identically by falling back to the database.
    /// </summary>
    Task<TicketStatsDto?> GetAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Caches freshly computed stats for this organization, with the TTL RedisTicketStatsCache owns (Redis:StatsCacheTtlSeconds).</summary>
    Task SetAsync(Guid organizationId, TicketStatsDto stats, CancellationToken cancellationToken);

    /// <summary>
    /// Evicts the cached stats for this organization. Called after any write
    /// that changes a counted dimension — ticket created or deleted, status
    /// changed, priority changed. See TicketService for exactly which methods
    /// call this, and why AssignAsync deliberately does not (assigning a
    /// ticket to someone changes no field this DTO counts).
    /// </summary>
    Task InvalidateAsync(Guid organizationId, CancellationToken cancellationToken);
}
