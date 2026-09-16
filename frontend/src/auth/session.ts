import type { AuthResponseDto } from "../api/types";
import { readOrganizationIdFromToken } from "./jwt";

// The whole "am I logged in, what can I do" session, kept in localStorage as
// one JSON blob under a single key — not spread across several keys, so a
// login/logout can never leave the browser in a half-updated state (e.g. a
// token without matching permissions). Session storage was deliberately not
// used instead: a portfolio reviewer opening the app in a second tab should
// stay logged in, the same way Atlas.Api's own JWT is valid until
// Jwt:ExpiryMinutes regardless of which browser tab issued it.
const STORAGE_KEY = "atlas.session";

export interface Session {
  token: string;
  expiresAtUtc: string;
  userId: string;
  fullName: string;
  email: string;
  role: number;
  permissions: string[];
  /** Read from the JWT's own "organization_id" claim — see jwt.ts's doc comment for why AuthResponseDto alone isn't enough. */
  organizationId: string | null;
}

export function saveSession(auth: AuthResponseDto): Session {
  const session: Session = {
    token: auth.token,
    expiresAtUtc: auth.expiresAtUtc,
    userId: auth.userId,
    fullName: auth.fullName,
    email: auth.email,
    role: auth.role,
    permissions: auth.permissions,
    organizationId: readOrganizationIdFromToken(auth.token),
  };
  localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
  return session;
}

export function loadSession(): Session | null {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (!raw) return null;

  try {
    const session = JSON.parse(raw) as Session;
    // Client-side expiry check is a UX nicety only — it saves a doomed round
    // trip to the API — never a security boundary. The real check is
    // Jwt:ExpiryMinutes/ValidateLifetime on the server (see Program.cs's
    // TokenValidationParameters); a tampered expiresAtUtc here can only make
    // the app log itself out early, never keep a truly expired token alive.
    if (new Date(session.expiresAtUtc).getTime() <= Date.now()) {
      clearSession();
      return null;
    }
    return session;
  } catch {
    clearSession();
    return null;
  }
}

export function clearSession(): void {
  localStorage.removeItem(STORAGE_KEY);
}

export function hasPermission(session: Session | null, permission: string): boolean {
  return session?.permissions.includes(permission) ?? false;
}
