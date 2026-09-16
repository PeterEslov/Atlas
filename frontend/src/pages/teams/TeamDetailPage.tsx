import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import { teamsApi } from "../../api/teams";
import { useAuth } from "../../auth/AuthContext";
import { Permissions } from "../../auth/permissions";
import { ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { formatDateTime, ui } from "../../components/ui";

export function TeamDetailPage() {
  const { id } = useParams<{ id: string }>();
  const teamId = id!;
  const { can } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const query = useQuery({ queryKey: ["teams", teamId], queryFn: () => teamsApi.getById(teamId) });

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ["teams"], exact: false });
  }

  const [newMemberId, setNewMemberId] = useState("");
  const addMemberMutation = useMutation({
    mutationFn: () => teamsApi.addMember(teamId, { userId: newMemberId }),
    onSuccess: () => { setNewMemberId(""); invalidate(); },
  });
  const removeMemberMutation = useMutation({
    mutationFn: (userId: string) => teamsApi.removeMember(teamId, userId),
    onSuccess: invalidate,
  });

  if (query.isPending) return <LoadingSpinner />;
  if (query.isError) return <ErrorMessage error={query.error} />;
  const team = query.data!;
  const canManage = can(Permissions.TeamManage);

  return (
    <div className="max-w-xl space-y-6">
      <button className="text-sm text-gray-500 hover:underline" onClick={() => navigate("/teams")}>
        ← Tillbaka till team
      </button>

      <h1 className="text-xl font-semibold text-gray-900">{team.name}</h1>
      <div className="text-xs text-gray-400">Skapad {formatDateTime(team.createdAtUtc)}</div>

      <div className={`${ui.card} p-4`}>
        <h2 className="mb-2 text-sm font-semibold text-gray-700">Medlemmar ({team.members.length})</h2>
        <ul className="mb-3 space-y-1">
          {team.members.length === 0 && <li className="text-sm text-gray-400">Inga medlemmar.</li>}
          {team.members.map((m) => (
            <li key={m.userId} className="flex items-center justify-between text-sm">
              <span>{m.fullName} <span className="text-gray-400">· sedan {formatDateTime(m.joinedAtUtc)}</span></span>
              {canManage && (
                <button className="text-gray-400 hover:text-red-600" onClick={() => removeMemberMutation.mutate(m.userId)}>
                  Ta bort
                </button>
              )}
            </li>
          ))}
        </ul>
        {canManage && (
          <div className="flex items-end gap-2 border-t pt-3">
            <div className="flex-1">
              <label className={ui.label}>Lägg till medlem (User ID)</label>
              <input className={`${ui.input} w-full font-mono text-xs`} value={newMemberId} onChange={(e) => setNewMemberId(e.target.value)} />
            </div>
            <button className={ui.buttonSecondary} disabled={!newMemberId || addMemberMutation.isPending} onClick={() => addMemberMutation.mutate()}>
              Lägg till
            </button>
          </div>
        )}
        {[addMemberMutation, removeMemberMutation].filter((m) => m.isError).map((m, i) => <ErrorMessage key={i} error={m.error} />)}
      </div>
    </div>
  );
}
