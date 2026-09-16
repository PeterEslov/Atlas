import { Route, Routes } from "react-router-dom";
import { useAuth } from "./auth/AuthContext";
import { ProtectedRoute, RequirePermission } from "./auth/ProtectedRoute";
import { Permissions } from "./auth/permissions";
import { Layout } from "./components/Layout";
import { LoginPage } from "./pages/LoginPage";
import { DashboardPage } from "./pages/DashboardPage";
import { TicketListPage } from "./pages/tickets/TicketListPage";
import { TicketDetailPage } from "./pages/tickets/TicketDetailPage";
import { TicketCreatePage } from "./pages/tickets/TicketCreatePage";
import { OrganizationListPage } from "./pages/organizations/OrganizationListPage";
import { OrganizationDetailPage } from "./pages/organizations/OrganizationDetailPage";
import { OrganizationCreatePage } from "./pages/organizations/OrganizationCreatePage";
import { UserListPage } from "./pages/users/UserListPage";
import { UserDetailPage } from "./pages/users/UserDetailPage";
import { ProjectListPage } from "./pages/projects/ProjectListPage";
import { ProjectDetailPage } from "./pages/projects/ProjectDetailPage";
import { ProjectCreatePage } from "./pages/projects/ProjectCreatePage";
import { TeamListPage } from "./pages/teams/TeamListPage";
import { TeamDetailPage } from "./pages/teams/TeamDetailPage";
import { TeamCreatePage } from "./pages/teams/TeamCreatePage";
import { AuditLogListPage } from "./pages/auditlogs/AuditLogListPage";

export function App() {
  const { isAuthenticated } = useAuth();

  if (!isAuthenticated) {
    return (
      <Routes>
        <Route path="*" element={<LoginPage />} />
      </Routes>
    );
  }

  return (
    <Layout>
      <Routes>
        <Route path="/login" element={<LoginPage />} />

        <Route path="/" element={<ProtectedRoute><DashboardPage /></ProtectedRoute>} />

        <Route path="/tickets" element={<ProtectedRoute><TicketListPage /></ProtectedRoute>} />
        <Route
          path="/tickets/new"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.TicketCreate}><TicketCreatePage /></RequirePermission>
            </ProtectedRoute>
          }
        />
        <Route path="/tickets/:id" element={<ProtectedRoute><TicketDetailPage /></ProtectedRoute>} />

        <Route
          path="/organizations"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.OrganizationRead}><OrganizationListPage /></RequirePermission>
            </ProtectedRoute>
          }
        />
        <Route
          path="/organizations/new"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.OrganizationManage}><OrganizationCreatePage /></RequirePermission>
            </ProtectedRoute>
          }
        />
        <Route
          path="/organizations/:id"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.OrganizationRead}><OrganizationDetailPage /></RequirePermission>
            </ProtectedRoute>
          }
        />

        <Route
          path="/users"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.UserRead}><UserListPage /></RequirePermission>
            </ProtectedRoute>
          }
        />
        <Route
          path="/users/:id"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.UserRead}><UserDetailPage /></RequirePermission>
            </ProtectedRoute>
          }
        />

        <Route
          path="/projects"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.ProjectRead}><ProjectListPage /></RequirePermission>
            </ProtectedRoute>
          }
        />
        <Route
          path="/projects/new"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.ProjectManage}><ProjectCreatePage /></RequirePermission>
            </ProtectedRoute>
          }
        />
        <Route
          path="/projects/:id"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.ProjectRead}><ProjectDetailPage /></RequirePermission>
            </ProtectedRoute>
          }
        />

        <Route
          path="/teams"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.TeamRead}><TeamListPage /></RequirePermission>
            </ProtectedRoute>
          }
        />
        <Route
          path="/teams/new"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.TeamManage}><TeamCreatePage /></RequirePermission>
            </ProtectedRoute>
          }
        />
        <Route
          path="/teams/:id"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.TeamRead}><TeamDetailPage /></RequirePermission>
            </ProtectedRoute>
          }
        />

        <Route
          path="/audit-logs"
          element={
            <ProtectedRoute>
              <RequirePermission permission={Permissions.AuditLogRead}><AuditLogListPage /></RequirePermission>
            </ProtectedRoute>
          }
        />
      </Routes>
    </Layout>
  );
}
