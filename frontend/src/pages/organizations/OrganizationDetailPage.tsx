import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import { organizationsApi } from "../../api/organizations";
import { OrganizationTypeLabels } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Permissions } from "../../auth/permissions";
import { ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { ActiveBadge } from "../../components/Status";
import { formatDateTime, ui } from "../../components/ui";

export function OrganizationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const orgId = id!;
  const { can } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const query = useQuery({ queryKey: ["organizations", orgId], queryFn: () => organizationsApi.getById(orgId) });

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ["organizations"], exact: false });
  }

  const [name, setName] = useState("");
  const renameMutation = useMutation({
    mutationFn: () => organizationsApi.rename(orgId, { name }),
    onSuccess: () => { setName(""); invalidate(); },
  });
  const deactivateMutation = useMutation({ mutationFn: () => organizationsApi.deactivate(orgId), onSuccess: invalidate });
  const reactivateMutation = useMutation({ mutationFn: () => organizationsApi.reactivate(orgId), onSuccess: invalidate });

  if (query.isPending) return <LoadingSpinner />;
  if (query.isError) return <ErrorMessage error={query.error} />;
  const org = query.data!;
  const canManage = can(Permissions.OrganizationManage);

  return (
    <div className="max-w-2xl space-y-6">
      <button className="text-sm text-gray-500 hover:underline" onClick={() => navigate("/organizations")}>
        ← Tillbaka till organisationer
      </button>

      <div className="flex items-center gap-3">
        <h1 className="text-xl font-semibold text-gray-900">{org.name}</h1>
        <ActiveBadge isActive={org.isActive} />
      </div>
      <div className="text-sm text-gray-500">
        {OrganizationTypeLabels[org.type]} · Skapad {formatDateTime(org.createdAtUtc)}
        {org.modifiedAtUtc && <> · Ändrad {formatDateTime(org.modifiedAtUtc)}</>}
      </div>

      <div className={`${ui.card} grid grid-cols-3 gap-4 p-4 text-center`}>
        <div>
          <div className="text-2xl font-bold">{org.userCount}</div>
          <div className="text-xs text-gray-500">Användare</div>
        </div>
        <div>
          <div className="text-2xl font-bold">{org.teamCount}</div>
          <div className="text-xs text-gray-500">Team</div>
        </div>
        <div>
          <div className="text-2xl font-bold">{org.projectCount}</div>
          <div className="text-xs text-gray-500">Projekt</div>
        </div>
      </div>

      {canManage && (
        <div className={`${ui.card} space-y-4 p-4`}>
          <h2 className="text-sm font-semibold text-gray-700">Åtgärder</h2>
          <div className="flex items-end gap-2">
            <div className="flex-1">
              <label className={ui.label}>Byt namn</label>
              <input className={`${ui.input} w-full`} value={name} onChange={(e) => setName(e.target.value)} placeholder={org.name} />
            </div>
            <button className={ui.buttonSecondary} disabled={!name || renameMutation.isPending} onClick={() => renameMutation.mutate()}>
              Byt namn
            </button>
          </div>
          <div className="flex gap-2 border-t pt-3">
            {org.isActive ? (
              <button className={ui.buttonDanger} disabled={deactivateMutation.isPending} onClick={() => deactivateMutation.mutate()}>
                Inaktivera
              </button>
            ) : (
              <button className={ui.buttonSecondary} disabled={reactivateMutation.isPending} onClick={() => reactivateMutation.mutate()}>
                Aktivera igen
              </button>
            )}
          </div>
          {[renameMutation, deactivateMutation, reactivateMutation].filter((m) => m.isError).map((m, i) => (
            <ErrorMessage key={i} error={m.error} />
          ))}
        </div>
      )}
    </div>
  );
}
