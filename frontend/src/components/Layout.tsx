import type { ReactNode } from "react";
import { NavLink } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { Permissions } from "../auth/permissions";

interface NavItem {
  to: string;
  label: string;
  /** Omit to show the link to every signed-in user regardless of permissions (Dashboard, Tickets). */
  permission?: string;
}

const NAV_ITEMS: NavItem[] = [
  { to: "/", label: "Dashboard" },
  { to: "/tickets", label: "Ärenden" },
  { to: "/projects", label: "Projekt", permission: Permissions.ProjectRead },
  { to: "/teams", label: "Team", permission: Permissions.TeamRead },
  { to: "/users", label: "Användare", permission: Permissions.UserRead },
  { to: "/organizations", label: "Organisationer", permission: Permissions.OrganizationRead },
  { to: "/audit-logs", label: "Audit Log", permission: Permissions.AuditLogRead },
];

export function Layout({ children }: { children: ReactNode }) {
  const { session, can, logout } = useAuth();

  return (
    <div className="min-h-screen flex">
      <aside className="w-56 shrink-0 bg-atlas-700 text-white flex flex-col">
        <div className="px-4 py-5 border-b border-atlas-600">
          <div className="font-bold text-lg leading-tight">Project Atlas</div>
          <div className="text-xs text-atlas-100">Northstar IT</div>
        </div>
        <nav className="flex-1 py-3">
          {NAV_ITEMS.filter((item) => !item.permission || can(item.permission)).map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === "/"}
              className={({ isActive }) =>
                `block px-4 py-2 text-sm ${
                  isActive ? "bg-atlas-600 font-semibold" : "text-atlas-50 hover:bg-atlas-600/60"
                }`
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="px-4 py-3 border-t border-atlas-600 text-xs text-atlas-100">
          Del 21 — React/TypeScript frontend
        </div>
      </aside>

      <div className="flex-1 flex flex-col min-w-0">
        <header className="flex items-center justify-between bg-white border-b px-6 py-3">
          <div />
          {session && (
            <div className="flex items-center gap-3 text-sm">
              <div className="text-right">
                <div className="font-medium">{session.fullName}</div>
                <div className="text-gray-500">{session.email}</div>
              </div>
              <button
                onClick={logout}
                className="rounded border border-gray-300 px-3 py-1.5 text-gray-700 hover:bg-gray-100"
              >
                Logga ut
              </button>
            </div>
          )}
        </header>
        <main className="flex-1 p-6 min-w-0">{children}</main>
      </div>
    </div>
  );
}
