import { loadSession, clearSession } from "../auth/session";
import type { ProblemDetails } from "./types";

// Falls back to Atlas.Api's http launchSettings.json profile (port 5080) so
// the app works out of the box for `dotnet run --project src/Atlas.Api` +
// `npm run dev`, with no .env file required. Override via .env.local (see
// .env.example) for the https profile or a Docker Compose/Azure target.
const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? "http://localhost:5080";

/** Thrown for every non-2xx response, carrying whatever ExceptionHandlingMiddleware put in the ProblemDetails body. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.detail ?? problem?.title ?? `Request failed with status ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }
}

/**
 * Fired on the window whenever a request comes back 401 — the token is
 * missing, expired, or Atlas.Api rejected it outright. AuthContext listens
 * for this to force a logout + redirect, so a session going stale mid-use
 * (not just at page load, where loadSession()'s own expiry check already
 * catches it) is handled in exactly one place instead of in every page that
 * happens to call the API.
 */
const UNAUTHORIZED_EVENT = "atlas:unauthorized";

function dispatchUnauthorized(): void {
  clearSession();
  window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
}

export function onUnauthorized(handler: () => void): () => void {
  window.addEventListener(UNAUTHORIZED_EVENT, handler);
  return () => window.removeEventListener(UNAUTHORIZED_EVENT, handler);
}

/** Turns a query-params object into a `?a=1&b=2` string, dropping undefined/null/empty-string values entirely (not the same as sending them as ""). */
export function toQueryString(params: Record<string, unknown>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === "") continue;
    search.set(key, String(value));
  }
  const qs = search.toString();
  return qs ? `?${qs}` : "";
}

async function parseProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    const contentType = response.headers.get("content-type") ?? "";
    if (contentType.includes("json")) {
      return (await response.json()) as ProblemDetails;
    }
  } catch {
    // Body wasn't valid JSON (or was empty) — fall through to a null problem;
    // the caller still gets a correct status code either way.
  }
  return null;
}

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  /** Set for multipart/form-data uploads (see uploadFile below) — skips the JSON Content-Type/serialization. */
  formData?: FormData;
  signal?: AbortSignal;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const session = loadSession();
  const headers: Record<string, string> = {};
  if (session) {
    headers["Authorization"] = `Bearer ${session.token}`;
  }

  let body: BodyInit | undefined;
  if (options.formData) {
    body = options.formData;
    // Deliberately no Content-Type header here — the browser sets
    // multipart/form-data with the correct boundary itself; setting it by
    // hand is a classic way to silently break the upload.
  } else if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(options.body);
  }

  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: options.method ?? "GET",
    headers,
    body,
    signal: options.signal,
  });

  if (response.status === 401) {
    dispatchUnauthorized();
    throw new ApiError(401, await parseProblem(response));
  }

  if (!response.ok) {
    throw new ApiError(response.status, await parseProblem(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: "GET", signal }),
  post: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>(path, { method: "POST", body: body ?? {}, signal }),
  put: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>(path, { method: "PUT", body: body ?? {}, signal }),
  delete: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: "DELETE", signal }),
  upload: <T>(path: string, formData: FormData, signal?: AbortSignal) =>
    request<T>(path, { method: "POST", formData, signal }),
};

/** Streams a file download (e.g. a ticket attachment) and returns it as a Blob plus the filename to save it as. */
export async function downloadFile(path: string): Promise<{ blob: Blob; fileName: string }> {
  const session = loadSession();
  const headers: Record<string, string> = {};
  if (session) headers["Authorization"] = `Bearer ${session.token}`;

  const response = await fetch(`${API_BASE_URL}${path}`, { headers });
  if (response.status === 401) {
    dispatchUnauthorized();
    throw new ApiError(401, await parseProblem(response));
  }
  if (!response.ok) {
    throw new ApiError(response.status, await parseProblem(response));
  }

  const disposition = response.headers.get("content-disposition") ?? "";
  const match = /filename="?([^";]+)"?/.exec(disposition);
  const fileName = match?.[1] ?? "download";

  return { blob: await response.blob(), fileName };
}
