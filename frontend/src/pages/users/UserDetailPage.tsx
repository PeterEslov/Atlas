import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import { usersApi } from "../../api/users";
import { UserRole, UserRoleLabels, enumOptions } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Permissions } from "../../auth/permissions";
import { ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { ActiveBadge } from "../../components/Status";
import { formatDateTime, ui } from "../../components/ui";

export function UserDetailPage() {
  const { id } = useParams<{ id: string }>();
  const userId = id!;
  const { can, session } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const query = useQuery({ queryKey: ["users", userId], queryFn: () => usersApi.getById(userId) });

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ["users"], exact: false });
  }

  const [role, setRole] = useState<UserRole | "">("");
  const roleMutation = useMutation({
    mutationFn: () => usersApi.changeRole(userId, { role: role as UserRole }),
    onSuccess: () => { setRole(""); invalidate(); },
  });
  const deactivateMutation = useMutation({ mutationFn: () => usersApi.deactivate(userId), onSuccess: invalidate });
  const reactivateMutation = useMutation({ mutationFn: () => usersApi.reactivate(userId), onSuccess: invalidate });

  if (query.isPending) return <LoadingSpinner />;
  if (query.isError) return <ErrorMessage error={query.error} />;
  const user = query.data!;
  const isSelf = user.id === session?.userId;
  const canManage = can(Permissions.UserManage) && !isSelf;

  return (
    <div className="max-w-xl space-y-6">
      <button className="text-sm text-gray-500 hover:underline" onClick={() => navigate("/users")}>
        ← Tillbaka till användare
      </button>

      <div className="flex items-center gap-3">
        <h1 className="text-xl font-semibold text-gray-900">{user.fullName}</h1>
        <ActiveBadge isActive={user.isActive} />
        {isSelf && <span className="text-xs text-gray-400">(du)</span>}
      </div>
      <div className="text-sm text-gray-500">
        {user.email} · {UserRoleLabels[user.role]} · {user.organizationName}
      </div>
      <div className="text-xs text-gray-400">Skapad {formatDateTime(user.createdAtUtc)}</div>

      {isSelf && (
        <div className="text-sm text-gray-500 italic">
          UsersController vägrar en användare ändra sin egen roll eller inaktivera sig själv (se
          UsersController.ChangeRole/Deactivate) — därför är åtgärderna nedan dolda för ditt eget konto.
        </div>
      )}

      {canManage && (
        <div className={`${ui.card} space-y-4 p-4`}>
          <h2 className="text-sm font-semibold text-gray-700">Åtgärder</h2>
          <div className="flex items-end gap-2">
            <div>
              <label className={ui.label}>Byt roll</label>
              <select className={ui.input} value={role} onChange={(e) => setRole(e.target.value === "" ? "" : (Number(e.target.value) as UserRole))}>
                <option value="">Välj...</option>
                {enumOptions(UserRoleLabels).map((opt) => (
                  <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
              </select>
            </div>
            <button className={ui.buttonSecondary} disabled={role === "" || roleMutation.isPending} onClick={() => roleMutation.mutate()}>
              Uppdatera roll
            </button>
          </div>
          <div className="flex gap-2 border-t pt-3">
            {user.isActive ? (
              <button className={ui.buttonDanger} disabled={deactivateMutation.isPending} onClick={() => deactivateMutation.mutate()}>
                Inaktivera
              </button>
            ) : (
              <button className={ui.buttonSecondary} disabled={reactivateMutation.isPending} onClick={() => reactivateMutation.mutate()}>
                Aktivera igen
              </button>
            )}
          </div>
          {[roleMutation, deactivateMutation, reactivateMutation].filter((m) => m.isError).map((m, i) => (
            <ErrorMessage key={i} error={m.error} />
          ))}
        </div>
      )}
    </div>
  );
}
