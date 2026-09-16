import { api, toQueryString, downloadFile } from "./client";
import type {
  AddTicketCommentRequest,
  AddTicketTagRequest,
  AssignTicketRequest,
  AttachmentDto,
  ChangeTicketPriorityRequest,
  ChangeTicketStatusRequest,
  CreateTicketRequest,
  PagedResult,
  RemoveTicketTagRequest,
  TicketCommentDto,
  TicketDetailDto,
  TicketDto,
  TicketListQuery,
  TicketStatsDto,
} from "./types";

export const ticketsApi = {
  search: (query: TicketListQuery) => api.get<PagedResult<TicketDto>>(`/api/tickets${toQueryString(query)}`),
  stats: () => api.get<TicketStatsDto>("/api/tickets/stats"),
  getById: (id: string) => api.get<TicketDetailDto>(`/api/tickets/${id}`),
  create: (request: CreateTicketRequest) => api.post<TicketDetailDto>("/api/tickets", request),
  assign: (id: string, request: AssignTicketRequest) =>
    api.post<TicketDetailDto>(`/api/tickets/${id}/assign`, request),
  changeStatus: (id: string, request: ChangeTicketStatusRequest) =>
    api.post<TicketDetailDto>(`/api/tickets/${id}/status`, request),
  changePriority: (id: string, request: ChangeTicketPriorityRequest) =>
    api.post<TicketDetailDto>(`/api/tickets/${id}/priority`, request),
  addComment: (id: string, request: AddTicketCommentRequest) =>
    api.post<TicketCommentDto>(`/api/tickets/${id}/comments`, request),
  reopen: (id: string) => api.post<TicketDetailDto>(`/api/tickets/${id}/reopen`),
  addTag: (id: string, request: AddTicketTagRequest) => api.post<TicketDetailDto>(`/api/tickets/${id}/tags`, request),
  removeTag: (id: string, request: RemoveTicketTagRequest) =>
    api.post<TicketDetailDto>(`/api/tickets/${id}/tags/remove`, request),
  uploadAttachment: (id: string, file: File) => {
    const formData = new FormData();
    formData.set("file", file);
    return api.upload<AttachmentDto>(`/api/tickets/${id}/attachments`, formData);
  },
  downloadAttachment: (ticketId: string, attachmentId: string) =>
    downloadFile(`/api/tickets/${ticketId}/attachments/${attachmentId}/download`),
  delete: (id: string) => api.delete<void>(`/api/tickets/${id}`),
};
