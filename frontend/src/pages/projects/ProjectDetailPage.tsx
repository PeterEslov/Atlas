import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import { projectsApi } from "../../api/projects";
import { useAuth } from "../../auth/AuthContext";
import { Permissions } from "../../auth/permissions";
import { ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { formatDateTime, ui } from "../../components/ui";

export function ProjectDetailPage() {
  const { id } = useParams<{ id: string }>();
  const projectId = id!;
  const { can } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const query = useQuery({ queryKey: ["projects", projectId], queryFn: () => projectsApi.getById(projectId) });

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ["projects"], exact: false });
  }

  const [newMemberId, setNewMemberId] = useState("");
  const addMemberMutation = useMutation({
    mutationFn: () => projectsApi.addMember(projectId, { userId: newMemberId }),
    onSuccess: () => { setNewMemberId(""); invalidate(); },
  });
  const removeMemberMutation = useMutation({
    mutationFn: (userId: string) => projectsApi.removeMember(projectId, userId),
    onSuccess: invalidate,
  });
  const archiveMutation = useMutation({ mutationFn: () => projectsApi.archive(projectId), onSuccess: invalidate });
  const unarchiveMutation = useMutation({ mutationFn: () => projectsApi.unarchive(projectId), onSuccess: invalidate });

  if (query.isPending) return <LoadingSpinner />;
  if (query.isError) return <ErrorMessage error={query.error} />;
  const project = query.data!;
  const canManage = can(Permissions.ProjectManage);

  return (
    <div className="max-w-xl space-y-6">
      <button className="text-sm text-gray-500 hover:underline" onClick={() => navigate("/projects")}>
        ← Tillbaka till projekt
      </button>

      <div className="flex items-center gap-3">
        <h1 className="text-xl font-semibold text-gray-900">{project.name}</h1>
        {project.isArchived && (
          <span className="inline-block rounded-full bg-gray-200 px-2.5 py-0.5 text-xs text-gray-600">Arkiverat</span>
        )}
      </div>
      {project.description && <p className="text-sm text-gray-700">{project.description}</p>}
      <div className="text-xs text-gray-400">Skapad {formatDateTime(project.createdAtUtc)}</div>

      <div className={`${ui.card} p-4`}>
        <h2 className="mb-2 text-sm font-semibold text-gray-700">Medlemmar ({project.members.length})</h2>
        <ul className="mb-3 space-y-1">
          {project.members.length === 0 && <li className="text-sm text-gray-400">Inga medlemmar.</li>}
          {project.members.map((m) => (
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

      {canManage && (
        <div className={`${ui.card} p-4`}>
          <h2 className="mb-2 text-sm font-semibold text-gray-700">Åtgärder</h2>
          {project.isArchived ? (
            <button className={ui.buttonSecondary} disabled={unarchiveMutation.isPending} onClick={() => unarchiveMutation.mutate()}>
              Återställ från arkiv
            </button>
          ) : (
            <button className={ui.buttonDanger} disabled={archiveMutation.isPending} onClick={() => archiveMutation.mutate()}>
              Arkivera
            </button>
          )}
          {[archiveMutation, unarchiveMutation].filter((m) => m.isError).map((m, i) => <ErrorMessage key={i} error={m.error} />)}
        </div>
      )}
    </div>
  );
}
