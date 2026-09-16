import { api, toQueryString } from "./client";
import type { AddTeamMemberRequest, CreateTeamRequest, PagedResult, TeamDetailDto, TeamDto, TeamListQuery } from "./types";

export const teamsApi = {
  search: (query: TeamListQuery) => api.get<PagedResult<TeamDto>>(`/api/teams${toQueryString(query)}`),
  getById: (id: string) => api.get<TeamDetailDto>(`/api/teams/${id}`),
  create: (request: CreateTeamRequest) => api.post<TeamDetailDto>("/api/teams", request),
  addMember: (id: string, request: AddTeamMemberRequest) => api.post<TeamDetailDto>(`/api/teams/${id}/members`, request),
  removeMember: (id: string, userId: string) => api.delete<TeamDetailDto>(`/api/teams/${id}/members/${userId}`),
};
