import { api, toQueryString } from "./client";
import type {
  CreateOrganizationRequest,
  OrganizationDetailDto,
  OrganizationDto,
  OrganizationListQuery,
  PagedResult,
  RenameOrganizationRequest,
} from "./types";

export const organizationsApi = {
  search: (query: OrganizationListQuery) =>
    api.get<PagedResult<OrganizationDto>>(`/api/organizations${toQueryString(query)}`),
  getById: (id: string) => api.get<OrganizationDetailDto>(`/api/organizations/${id}`),
  create: (request: CreateOrganizationRequest) => api.post<OrganizationDetailDto>("/api/organizations", request),
  rename: (id: string, request: RenameOrganizationRequest) =>
    api.post<OrganizationDetailDto>(`/api/organizations/${id}/rename`, request),
  deactivate: (id: string) => api.post<OrganizationDetailDto>(`/api/organizations/${id}/deactivate`),
  reactivate: (id: string) => api.post<OrganizationDetailDto>(`/api/organizations/${id}/reactivate`),
};
