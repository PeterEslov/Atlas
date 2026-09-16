import { api, toQueryString } from "./client";
import type { ChangeUserRoleRequest, PagedResult, UserDto, UserListQuery } from "./types";

export const usersApi = {
  search: (query: UserListQuery) => api.get<PagedResult<UserDto>>(`/api/users${toQueryString(query)}`),
  getById: (id: string) => api.get<UserDto>(`/api/users/${id}`),
  changeRole: (id: string, request: ChangeUserRoleRequest) => api.post<UserDto>(`/api/users/${id}/role`, request),
  deactivate: (id: string) => api.post<UserDto>(`/api/users/${id}/deactivate`),
  reactivate: (id: string) => api.post<UserDto>(`/api/users/${id}/reactivate`),
};
