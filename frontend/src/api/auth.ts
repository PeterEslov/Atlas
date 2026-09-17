import { api } from "./client";
import type { AuthResponseDto, LoginRequest, OrganizationOptionDto, RegisterRequest } from "./types";

export const authApi = {
  login: (request: LoginRequest) => api.post<AuthResponseDto>("/api/auth/login", request),
  register: (request: RegisterRequest) => api.post<AuthResponseDto>("/api/auth/register", request),
  // featurelogin branch — anonymous, powers the Register form's organization
  // dropdown (see AuthController.GetRegistrableOrganizations's doc comment).
  registrableOrganizations: () => api.get<OrganizationOptionDto[]>("/api/auth/organizations"),
};
