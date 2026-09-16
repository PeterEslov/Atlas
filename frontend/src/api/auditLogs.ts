import { api, toQueryString } from "./client";
import type { AuditLogDto, AuditLogListQuery, PagedResult } from "./types";

export const auditLogsApi = {
  search: (query: AuditLogListQuery) => api.get<PagedResult<AuditLogDto>>(`/api/audit-logs${toQueryString(query)}`),
};
