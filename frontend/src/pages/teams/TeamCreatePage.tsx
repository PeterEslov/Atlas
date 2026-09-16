import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { teamsApi } from "../../api/teams";
import { useAuth } from "../../auth/AuthContext";
import { ErrorMessage } from "../../components/Feedback";
import { ui } from "../../components/ui";

export function TeamCreatePage() {
  const { session } = useAuth();
  const navigate = useNavigate();
  const [organizationId, setOrganizationId] = useState(session?.organizationId ?? "");
  const [name, setName] = useState("");

  const mutation = useMutation({
    mutationFn: () => teamsApi.create({ organizationId, name }),
    onSuccess: (team) => navigate(`/teams/${team.id}`),
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    mutation.mutate();
  }

  return (
    <div className="max-w-xl">
      <h1 className={ui.pageTitle}>Nytt team</h1>
      <form onSubmit={handleSubmit} className={`${ui.card} space-y-4 p-6`}>
        <div>
          <label className={ui.label}>Organization ID</label>
          <input className={`${ui.input} w-full font-mono text-xs`} value={organizationId} onChange={(e) => setOrganizationId(e.target.value)} required />
        </div>
        <div>
          <label className={ui.label}>Namn</label>
          <input className={`${ui.input} w-full`} value={name} onChange={(e) => setName(e.target.value)} required />
        </div>
        {mutation.isError && <ErrorMessage error={mutation.error} />}
        <div className="flex gap-2">
          <button type="submit" className={ui.buttonPrimary} disabled={mutation.isPending}>
            {mutation.isPending ? "Skapar..." : "Skapa team"}
          </button>
          <button type="button" className={ui.buttonSecondary} onClick={() => navigate(-1)}>Avbryt</button>
        </div>
      </form>
    </div>
  );
}
