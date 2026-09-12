using Atlas.Application.Audit.Dtos;
using Atlas.Application.Common.Interfaces;
using Atlas.Application.Common.Models;

namespace Atlas.Application.Audit.Services;

/// <summary>Thin read wrapper around IAuditLogRepository — the same "load, map to DTO" shape as every other *Service.SearchAsync in this project.</summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _auditLogRepository;

    public AuditLogService(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogListQuery query, CancellationToken cancellationToken)
    {
        var normalizedQuery = Normalize(query);
        var (items, totalCount) = await _auditLogRepository.SearchAsync(normalizedQuery, cancellationToken);

        var dtos = items.Select(i => new AuditLogDto(
            i.AuditLog.Id,
            i.AuditLog.UserId,
            i.ActorFullName,
            i.AuditLog.Action,
            i.AuditLog.EntityName,
            i.AuditLog.EntityId,
            i.AuditLog.OldValuesJson,
            i.AuditLog.NewValuesJson,
            i.AuditLog.TimestampUtc)).ToList();

        return new PagedResult<AuditLogDto>(dtos, normalizedQuery.Page, normalizedQuery.PageSize, totalCount);
    }

    private static AuditLogListQuery Normalize(AuditLogListQuery query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => 25,
            > AuditLogListQuery.MaxPageSize => AuditLogListQuery.MaxPageSize,
            _ => query.PageSize
        };

        return new AuditLogListQuery
        {
            EntityName = query.EntityName,
            EntityId = query.EntityId,
            UserId = query.UserId,
            Action = query.Action,
            FromUtc = query.FromUtc,
            ToUtc = query.ToUtc,
            Page = page,
            PageSize = pageSize
        };
    }
}
