using System.Text.Json;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Tickets.Dtos;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Caching;

/// <summary>
/// Redis-backed cache-aside store for GET /api/tickets/stats (Del 13), one
/// JSON entry per organization. Built on <see cref="IDistributedCache"/> —
/// the ASP.NET Core abstraction over "an external key/value store with
/// TTLs" — rather than StackExchange.Redis's own IConnectionMultiplexer/
/// IDatabase types directly, because everything this class needs (get one
/// blob by key, set one blob with an expiry, remove one key) is exactly what
/// IDistributedCache already models. Reaching for Redis's own richer client
/// API (pattern-based key scans, pub/sub, Lua scripts) would only make sense
/// once a later feature actually needs one of those — none of which applies
/// here, with a single key per organization. See
/// DependencyInjection.AddCaching for what plugs a real Redis connection in
/// behind that abstraction (AddStackExchangeRedisCache).
///
/// Every method below is "fail open": if Redis is unreachable, a GET is
/// treated as a cache miss (TicketService.GetStatsAsync falls back to the
/// database), and a failed SET or invalidation is only logged, never thrown.
/// The same principle already governs Del 12's Service Bus publish in
/// TicketService.AssignAsync — an optional, secondary dependency being down
/// must never turn a request that would otherwise succeed into a 500. A
/// missed invalidation specifically is also bounded by the TTL below: even
/// if Redis is unreachable at the exact moment a ticket changes, the stale
/// entry expires on its own at most Redis:StatsCacheTtlSeconds later.
/// </summary>
public sealed class RedisTicketStatsCache : ITicketStatsCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisTicketStatsCache> _logger;
    private readonly TimeSpan _ttl;

    public RedisTicketStatsCache(IDistributedCache cache, IConfiguration configuration, ILogger<RedisTicketStatsCache> logger)
    {
        _cache = cache;
        _logger = logger;

        // A short TTL (30s default) on top of explicit invalidation isn't
        // belt-and-braces redundancy — it's a backstop for two things
        // invalidation can't fix: a Redis call that itself fails at
        // invalidation time (see InvalidateAsync), and OverdueCount, which
        // goes stale purely from the passage of time, with no ticket write
        // at all to hook an invalidation onto. Configurable (rather than a
        // hardcoded constant) so Peter can turn it down to something like 5
        // seconds while testing cache behaviour by hand, without a rebuild.
        var ttlSeconds = configuration.GetValue<int?>("Redis:StatsCacheTtlSeconds") ?? 30;
        _ttl = TimeSpan.FromSeconds(ttlSeconds);
    }

    private static string KeyFor(Guid organizationId) => $"atlas:ticket-stats:{organizationId:N}";

    public async Task<TicketStatsDto?> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        string? json;
        try
        {
            json = await _cache.GetStringAsync(KeyFor(organizationId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for organization {OrganizationId}; treating as a cache miss and falling back to the database.", organizationId);
            return null;
        }

        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TicketStatsDto>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            // Defensive: a cached shape that no longer matches TicketStatsDto
            // (e.g. this DTO gained/lost a field between two deploys, and an
            // old entry is still sitting in Redis with the old shape) should
            // be treated as a miss, not a crash — same fail-open principle as
            // a Redis connection failure above.
            _logger.LogWarning(ex, "Cached ticket stats for organization {OrganizationId} could not be deserialized; treating as a cache miss.", organizationId);
            return null;
        }
    }

    public async Task SetAsync(Guid organizationId, TicketStatsDto stats, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(stats, SerializerOptions);
            await _cache.SetStringAsync(
                KeyFor(organizationId),
                json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            // The caller (TicketService.GetStatsAsync) already has its freshly
            // computed result and returns it regardless — a failed SET just
            // means the next request pays the database's aggregate-query cost
            // again too, instead of hitting a warm cache. Degraded, not broken.
            _logger.LogWarning(ex, "Redis SET failed for organization {OrganizationId}; stats were computed but not cached.", organizationId);
        }
    }

    public async Task InvalidateAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(KeyFor(organizationId), cancellationToken);
        }
        catch (Exception ex)
        {
            // If Redis can't even be reached to remove the now-stale entry,
            // the TTL above is the backstop that eventually clears it instead
            // — see this class's own doc comment for why that TTL exists even
            // though explicit invalidation covers the common case.
            _logger.LogWarning(ex, "Redis invalidation failed for organization {OrganizationId}; the stale entry will expire via TTL instead.", organizationId);
        }
    }
}
