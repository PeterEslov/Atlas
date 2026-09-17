import { useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { authApi } from "../api/auth";
import { UserRole, UserRoleLabels, enumOptions } from "../api/types";
import { ErrorMessage } from "../components/Feedback";
import { ui } from "../components/ui";

export function LoginPage() {
  const { isAuthenticated, login, register } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [mode, setMode] = useState<"login" | "register">("login");

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [fullName, setFullName] = useState("");
  const [organizationId, setOrganizationId] = useState("");
  const [role, setRole] = useState<UserRole>(UserRole.Customer);

  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  // featurelogin branch — only fires once the Register form is actually
  // showing (enabled: mode === "register"), so a plain login never makes an
  // extra request. See AuthController.GetRegistrableOrganizations's doc
  // comment for why this is reachable without a token.
  const organizationsQuery = useQuery({
    queryKey: ["auth", "registrable-organizations"],
    queryFn: () => authApi.registrableOrganizations(),
    enabled: mode === "register",
    staleTime: 60_000,
  });

  if (isAuthenticated) {
    const redirectTo = (location.state as { from?: string } | null)?.from ?? "/";
    return <Navigate to={redirectTo} replace />;
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      if (mode === "login") {
        await login({ email, password });
      } else {
        await register({ fullName, email, password, organizationId, role });
      }
      navigate("/", { replace: true });
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className={`${ui.card} w-full max-w-sm p-6`}>
        <div className="mb-6 text-center">
          <div className="text-xl font-bold text-atlas-700">Project Atlas</div>
          <div className="text-sm text-gray-500">{mode === "login" ? "Logga in" : "Skapa konto"}</div>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          {mode === "register" && (
            <div>
              <label className={ui.label}>Namn</label>
              <input
                className={`${ui.input} w-full`}
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                required
              />
            </div>
          )}

          <div>
            <label className={ui.label}>E-post</label>
            <input
              type="email"
              className={`${ui.input} w-full`}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
          </div>

          <div>
            <label className={ui.label}>Lösenord</label>
            <input
              type="password"
              className={`${ui.input} w-full`}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </div>

          {mode === "register" && (
            <>
              <div>
                <label className={ui.label}>Organisation</label>
                <select
                  className={`${ui.input} w-full`}
                  value={organizationId}
                  onChange={(e) => setOrganizationId(e.target.value)}
                  disabled={organizationsQuery.isPending}
                  required
                >
                  <option value="" disabled>
                    {organizationsQuery.isPending ? "Laddar organisationer..." : "Välj organisation"}
                  </option>
                  {organizationsQuery.data?.map((org) => (
                    <option key={org.id} value={org.id}>
                      {org.name}
                    </option>
                  ))}
                </select>
                {organizationsQuery.isError && (
                  <p className="mt-1 text-xs text-red-600">
                    Kunde inte hämta organisationer — kontrollera att Atlas.Api kör.
                  </p>
                )}
                {organizationsQuery.isSuccess && organizationsQuery.data.length === 0 && (
                  <p className="mt-1 text-xs text-gray-500">
                    Inga organisationer hittades — en Admin behöver skapa en först.
                  </p>
                )}
              </div>
              <div>
                <label className={ui.label}>Roll</label>
                <select
                  className={`${ui.input} w-full`}
                  value={role}
                  onChange={(e) => setRole(Number(e.target.value) as UserRole)}
                >
                  {enumOptions(UserRoleLabels).map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </div>
            </>
          )}

          {error !== null && <ErrorMessage error={error} />}

          <button type="submit" className={`${ui.buttonPrimary} w-full`} disabled={busy}>
            {busy ? "Ett ögonblick..." : mode === "login" ? "Logga in" : "Skapa konto"}
          </button>
        </form>

        <button
          className="mt-4 w-full text-center text-sm text-atlas-700 hover:underline"
          onClick={() => {
            setError(null);
            setMode(mode === "login" ? "register" : "login");
          }}
        >
          {mode === "login" ? "Inget konto? Registrera dig" : "Har du redan ett konto? Logga in"}
        </button>
      </div>
    </div>
  );
}
