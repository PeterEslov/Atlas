import type { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useAuth } from "./AuthContext";

/**
 * Route guard: redirects to /login if nobody's signed in. This is a UX
 * convenience only, not a security boundary — every permission that
 * actually matters is enforced server-side by Atlas.Api's
 * [Authorize(Policy = Permissions.X)] policies (see Program.cs), which is
 * the only place that can't be bypassed by editing localStorage or React
 * state in devtools.
 */
export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }
  return <>{children}</>;
}

/**
 * Hides/redirects a route the signed-in user has no permission for — again a
 * UX nicety (don't show a "New organization" button/page to a Manager who'll
 * just get a 403) layered on top of, never instead of, the server-side check.
 */
export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const { can } = useAuth();
  if (!can(permission)) {
    return <Navigate to="/" replace />;
  }
  return <>{children}</>;
}
