import { api } from "./client";
import type { AuthResponseDto, LoginRequest, RegisterRequest } from "./types";

export const authApi = {
  login: (request: LoginRequest) => api.post<AuthResponseDto>("/api/auth/login", request),
  register: (request: RegisterRequest) => api.post<AuthResponseDto>("/api/auth/register", request),
};
