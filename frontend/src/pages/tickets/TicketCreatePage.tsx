import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { ticketsApi } from "../../api/tickets";
import { TicketPriority, TicketPriorityLabels, enumOptions } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { ErrorMessage } from "../../components/Feedback";
import { ui } from "../../components/ui";

export function TicketCreatePage() {
  const { session } = useAuth();
  const navigate = useNavigate();

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [organizationId, setOrganizationId] = useState(session?.organizationId ?? "");
  const [priority, setPriority] = useState<TicketPriority>(TicketPriority.Medium);
  const [dueAtUtc, setDueAtUtc] = useState("");

  const mutation = useMutation({
    mutationFn: () =>
      ticketsApi.create({
        title,
        description,
        organizationId,
        priority,
        projectId: null,
        dueAtUtc: dueAtUtc ? new Date(dueAtUtc).toISOString() : null,
      }),
    onSuccess: (ticket) => navigate(`/tickets/${ticket.id}`),
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    mutation.mutate();
  }

  return (
    <div className="max-w-xl">
      <h1 className={ui.pageTitle}>Nytt ärende</h1>
      <form onSubmit={handleSubmit} className={`${ui.card} space-y-4 p-6`}>
        <div>
          <label className={ui.label}>Titel</label>
          <input
            className={`${ui.input} w-full`}
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            required
          />
        </div>
        <div>
          <label className={ui.label}>Beskrivning</label>
          <textarea
            className={`${ui.input} w-full`}
            rows={4}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            required
          />
        </div>
        <div>
          <label className={ui.label}>Organization ID</label>
          <input
            className={`${ui.input} w-full font-mono text-xs`}
            value={organizationId}
            onChange={(e) => setOrganizationId(e.target.value)}
            required
          />
        </div>
        <div>
          <label className={ui.label}>Prioritet</label>
          <select
            className={ui.input}
            value={priority}
            onChange={(e) => setPriority(Number(e.target.value) as TicketPriority)}
          >
            {enumOptions(TicketPriorityLabels).map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className={ui.label}>Förfaller (valfritt)</label>
          <input
            type="datetime-local"
            className={ui.input}
            value={dueAtUtc}
            onChange={(e) => setDueAtUtc(e.target.value)}
          />
        </div>

        {mutation.isError && <ErrorMessage error={mutation.error} />}

        <div className="flex gap-2">
          <button type="submit" className={ui.buttonPrimary} disabled={mutation.isPending}>
            {mutation.isPending ? "Skapar..." : "Skapa ärende"}
          </button>
          <button type="button" className={ui.buttonSecondary} onClick={() => navigate(-1)}>
            Avbryt
          </button>
        </div>
      </form>
    </div>
  );
}
