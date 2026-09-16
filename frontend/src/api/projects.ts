import { api, toQueryString } from "./client";
import type {
  AddProjectMemberRequest,
  CreateProjectRequest,
  PagedResult,
  ProjectDetailDto,
  ProjectDto,
  ProjectListQuery,
} from "./types";

export const projectsApi = {
  search: (query: ProjectListQuery) => api.get<PagedResult<ProjectDto>>(`/api/projects${toQueryString(query)}`),
  getById: (id: string) => api.get<ProjectDetailDto>(`/api/projects/${id}`),
  create: (request: CreateProjectRequest) => api.post<ProjectDetailDto>("/api/projects", request),
  archive: (id: string) => api.post<ProjectDetailDto>(`/api/projects/${id}/archive`),
  unarchive: (id: string) => api.post<ProjectDetailDto>(`/api/projects/${id}/unarchive`),
  addMember: (id: string, request: AddProjectMemberRequest) =>
    api.post<ProjectDetailDto>(`/api/projects/${id}/members`, request),
  removeMember: (id: string, userId: string) =>
    api.delete<ProjectDetailDto>(`/api/projects/${id}/members/${userId}`),
};
