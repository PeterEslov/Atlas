import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "react-router-dom";
import { ticketsApi } from "../../api/tickets";
import {
  TicketPriority,
  TicketPriorityLabels,
  TicketStatus,
  TicketStatusLabels,
  enumOptions,
} from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Permissions } from "../../auth/permissions";
import { ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { PriorityBadge, StatusBadge } from "../../components/Status";
import { ui, formatDateTime } from "../../components/ui";

export function TicketDetailPage() {
  const { id } = useParams<{ id: string }>();
  const ticketId = id!;
  const { can } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const ticketQuery = useQuery({
    queryKey: ["tickets", ticketId],
    queryFn: () => ticketsApi.getById(ticketId),
  });

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ["tickets", ticketId] });
    queryClient.invalidateQueries({ queryKey: ["tickets"], exact: false });
  }

  // ---- Assign --------------------------------------------------------------
  const [assignUserId, setAssignUserId] = useState("");
  const assignMutation = useMutation({
    mutationFn: () => ticketsApi.assign(ticketId, { userId: assignUserId }),
    onSuccess: () => {
      setAssignUserId("");
      invalidate();
    },
  });

  // ---- Status ---------------------------------------------------------------
  const [status, setStatus] = useState<TicketStatus | "">("");
  const statusMutation = useMutation({
    mutationFn: (newStatus: TicketStatus) => ticketsApi.changeStatus(ticketId, { status: newStatus }),
    onSuccess: () => {
      setStatus("");
      invalidate();
    },
  });

  // ---- Priority ---------------------------------------------------------------
  const [priority, setPriority] = useState<TicketPriority | "">("");
  const [escalationReason, setEscalationReason] = useState("");
  const priorityMutation = useMutation({
    mutationFn: () =>
      ticketsApi.changePriority(ticketId, {
        priority: priority as TicketPriority,
        escalationReason: escalationReason || null,
      }),
    onSuccess: () => {
      setPriority("");
      setEscalationReason("");
      invalidate();
    },
  });

  // ---- Reopen / delete --------------------------------------------------------
  const reopenMutation = useMutation({ mutationFn: () => ticketsApi.reopen(ticketId), onSuccess: invalidate });
  const deleteMutation = useMutation({
    mutationFn: () => ticketsApi.delete(ticketId),
    onSuccess: () => navigate("/tickets"),
  });

  // ---- Tags -----------------------------------------------------------------
  const [newTag, setNewTag] = useState("");
  const addTagMutation = useMutation({
    mutationFn: () => ticketsApi.addTag(ticketId, { name: newTag }),
    onSuccess: () => {
      setNewTag("");
      invalidate();
    },
  });
  const removeTagMutation = useMutation({
    mutationFn: (name: string) => ticketsApi.removeTag(ticketId, { name }),
    onSuccess: invalidate,
  });

  // ---- Comments -----------------------------------------------------------
  const [commentBody, setCommentBody] = useState("");
  const [isInternal, setIsInternal] = useState(false);
  const addCommentMutation = useMutation({
    mutationFn: () => ticketsApi.addComment(ticketId, { body: commentBody, isInternal }),
    onSuccess: () => {
      setCommentBody("");
      setIsInternal(false);
      invalidate();
    },
  });

  // ---- Attachments ------------------------------------------------------------
  const uploadMutation = useMutation({
    mutationFn: (file: File) => ticketsApi.uploadAttachment(ticketId, file),
    onSuccess: invalidate,
  });
  const [downloadError, setDownloadError] = useState<unknown>(null);

  async function handleDownload(attachmentId: string, fileName: string) {
    setDownloadError(null);
    try {
      const { blob } = await ticketsApi.downloadAttachment(ticketId, attachmentId);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = fileName;
      a.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setDownloadError(err);
    }
  }

  if (ticketQuery.isPending) return <LoadingSpinner />;
  if (ticketQuery.isError) return <ErrorMessage error={ticketQuery.error} />;
  const ticket = ticketQuery.data!;

  const canUpdate = can(Permissions.TicketUpdate);
  const canAssign = can(Permissions.TicketAssign);
  const canDelete = can(Permissions.TicketDelete);

  return (
    <div className="max-w-3xl space-y-6">
      <div>
        <button className="text-sm text-gray-500 hover:underline" onClick={() => navigate("/tickets")}>
          ← Tillbaka till ärenden
        </button>
        <div className="mt-1 flex items-center gap-3">
          <h1 className="text-xl font-semibold text-gray-900">{ticket.title}</h1>
          <StatusBadge status={ticket.status} />
          <PriorityBadge priority={ticket.priority} />
          {ticket.isOverdue && <span className="text-xs font-semibold text-red-600">FÖRSENAD</span>}
        </div>
        <div className="mt-1 text-sm text-gray-500">
          {ticket.organizationName} · Skapad {formatDateTime(ticket.createdAtUtc)}
          {ticket.modifiedAtUtc && <> · Ändrad {formatDateTime(ticket.modifiedAtUtc)}</>}
        </div>
      </div>

      <div className={`${ui.card} p-4`}>
        <h2 className="mb-2 text-sm font-semibold text-gray-700">Beskrivning</h2>
        <p className="whitespace-pre-wrap text-sm text-gray-800">{ticket.description}</p>
      </div>

      {(canUpdate || canAssign || canDelete) && (
        <div className={`${ui.card} space-y-4 p-4`}>
          <h2 className="text-sm font-semibold text-gray-700">Åtgärder</h2>

          {canAssign && (
            <div className="flex items-end gap-2">
              <div className="flex-1">
                <label className={ui.label}>Tilldela till (User ID)</label>
                <input
                  className={`${ui.input} w-full font-mono text-xs`}
                  value={assignUserId}
                  onChange={(e) => setAssignUserId(e.target.value)}
                  placeholder={ticket.assignedToUserId ?? "GUID"}
                />
              </div>
              <button
                className={ui.buttonSecondary}
                disabled={!assignUserId || assignMutation.isPending}
                onClick={() => assignMutation.mutate()}
              >
                Tilldela
              </button>
            </div>
          )}

          {canUpdate && (
            <div className="flex items-end gap-2">
              <div>
                <label className={ui.label}>Byt status</label>
                <select
                  className={ui.input}
                  value={status}
                  onChange={(e) => setStatus(e.target.value === "" ? "" : (Number(e.target.value) as TicketStatus))}
                >
                  <option value="">Välj...</option>
                  {enumOptions(TicketStatusLabels).map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </div>
              <button
                className={ui.buttonSecondary}
                disabled={status === "" || statusMutation.isPending}
                onClick={() => status !== "" && statusMutation.mutate(status)}
              >
                Uppdatera status
              </button>
            </div>
          )}

          {canUpdate && (
            <div className="flex flex-wrap items-end gap-2">
              <div>
                <label className={ui.label}>Byt prioritet</label>
                <select
                  className={ui.input}
                  value={priority}
                  onChange={(e) =>
                    setPriority(e.target.value === "" ? "" : (Number(e.target.value) as TicketPriority))
                  }
                >
                  <option value="">Välj...</option>
                  {enumOptions(TicketPriorityLabels).map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </div>
              <div className="flex-1 min-w-[12rem]">
                <label className={ui.label}>Eskaleringsskäl (valfritt)</label>
                <input
                  className={`${ui.input} w-full`}
                  value={escalationReason}
                  onChange={(e) => setEscalationReason(e.target.value)}
                />
              </div>
              <button
                className={ui.buttonSecondary}
                disabled={priority === "" || priorityMutation.isPending}
                onClick={() => priorityMutation.mutate()}
              >
                Uppdatera prioritet
              </button>
            </div>
          )}

          <div className="flex gap-2 pt-2 border-t">
            {canUpdate && (ticket.status === TicketStatus.Closed || ticket.status === TicketStatus.Cancelled) && (
              <button
                className={ui.buttonSecondary}
                disabled={reopenMutation.isPending}
                onClick={() => reopenMutation.mutate()}
              >
                Återöppna
              </button>
            )}
            {canDelete && (
              <button
                className={ui.buttonDanger}
                disabled={deleteMutation.isPending}
                onClick={() => {
                  if (confirm(`Radera ärendet "${ticket.title}" permanent? Det här går inte att ångra.`)) {
                    deleteMutation.mutate();
                  }
                }}
              >
                Radera ärende
              </button>
            )}
          </div>

          {[assignMutation, statusMutation, priorityMutation, reopenMutation, deleteMutation]
            .filter((m) => m.isError)
            .map((m, i) => (
              <ErrorMessage key={i} error={m.error} />
            ))}
        </div>
      )}

      <div className={`${ui.card} p-4`}>
        <h2 className="mb-2 text-sm font-semibold text-gray-700">Taggar</h2>
        <div className="mb-2 flex flex-wrap gap-2">
          {ticket.tags.length === 0 && <span className="text-sm text-gray-400">Inga taggar.</span>}
          {ticket.tags.map((tag) => (
            <span
              key={tag}
              className="inline-flex items-center gap-1 rounded-full bg-gray-100 px-2.5 py-0.5 text-xs text-gray-700"
            >
              {tag}
              {canUpdate && (
                <button
                  className="text-gray-400 hover:text-red-600"
                  onClick={() => removeTagMutation.mutate(tag)}
                  aria-label={`Ta bort tagg ${tag}`}
                >
                  ×
                </button>
              )}
            </span>
          ))}
        </div>
        {canUpdate && (
          <div className="flex gap-2">
            <input
              className={ui.input}
              placeholder="Ny tagg..."
              value={newTag}
              onChange={(e) => setNewTag(e.target.value)}
            />
            <button
              className={ui.buttonSecondary}
              disabled={!newTag || addTagMutation.isPending}
              onClick={() => addTagMutation.mutate()}
            >
              Lägg till
            </button>
          </div>
        )}
      </div>

      <div className={`${ui.card} p-4`}>
        <h2 className="mb-2 text-sm font-semibold text-gray-700">Bilagor</h2>
        {downloadError !== null && <ErrorMessage error={downloadError} />}
        <ul className="mb-3 space-y-1">
          {ticket.attachments.length === 0 && <li className="text-sm text-gray-400">Inga bilagor.</li>}
          {ticket.attachments.map((a) => (
            <li key={a.id} className="text-sm">
              <button
                className="text-atlas-700 hover:underline"
                onClick={() => handleDownload(a.id, a.fileName)}
              >
                {a.fileName}
              </button>
              <span className="text-gray-400"> · {(a.sizeInBytes / 1024).toFixed(0)} KB</span>
            </li>
          ))}
        </ul>
        {canUpdate && (
          <div>
            <input
              type="file"
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) uploadMutation.mutate(file);
                e.target.value = "";
              }}
            />
            {uploadMutation.isPending && <div className="text-xs text-gray-400">Laddar upp...</div>}
            {uploadMutation.isError && <ErrorMessage error={uploadMutation.error} />}
          </div>
        )}
      </div>

      <div className={`${ui.card} p-4`}>
        <h2 className="mb-2 text-sm font-semibold text-gray-700">Kommentarer</h2>
        <ul className="mb-3 space-y-3">
          {ticket.comments.length === 0 && <li className="text-sm text-gray-400">Inga kommentarer än.</li>}
          {ticket.comments.map((c) => (
            <li key={c.id} className="border-l-2 border-gray-200 pl-3">
              <div className="text-xs text-gray-500">
                {formatDateTime(c.createdAtUtc)}
                {c.isInternal && (
                  <span className="ml-2 rounded bg-amber-100 px-1.5 py-0.5 text-amber-800">Intern</span>
                )}
              </div>
              <div className="text-sm text-gray-800 whitespace-pre-wrap">{c.body}</div>
            </li>
          ))}
        </ul>
        {canUpdate && (
          <form
            className="space-y-2"
            onSubmit={(e: FormEvent) => {
              e.preventDefault();
              addCommentMutation.mutate();
            }}
          >
            <textarea
              className={`${ui.input} w-full`}
              rows={3}
              placeholder="Skriv en kommentar..."
              value={commentBody}
              onChange={(e) => setCommentBody(e.target.value)}
              required
            />
            <div className="flex items-center justify-between">
              <label className="flex items-center gap-2 text-sm text-gray-600">
                <input type="checkbox" checked={isInternal} onChange={(e) => setIsInternal(e.target.checked)} />
                Intern kommentar (syns inte för Customer)
              </label>
              <button type="submit" className={ui.buttonPrimary} disabled={addCommentMutation.isPending}>
                Kommentera
              </button>
            </div>
            {addCommentMutation.isError && <ErrorMessage error={addCommentMutation.error} />}
          </form>
        )}
      </div>

      <div className={`${ui.card} p-4`}>
        <h2 className="mb-2 text-sm font-semibold text-gray-700">Historik</h2>
        {ticket.history.length === 0 ? (
          <div className="text-sm text-gray-400">Inga ändringar registrerade.</div>
        ) : (
          <ul className="space-y-1 text-sm text-gray-700">
            {ticket.history.map((h) => (
              <li key={h.id}>
                <span className="text-gray-400">{formatDateTime(h.changedAtUtc)}</span> — {h.fieldName}:{" "}
                <span className="text-gray-400">{h.oldValue ?? "–"}</span> → {h.newValue ?? "–"}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
