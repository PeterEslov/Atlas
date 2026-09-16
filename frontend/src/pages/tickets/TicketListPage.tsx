import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { ticketsApi } from "../../api/tickets";
import { TicketPriority, TicketPriorityLabels, TicketStatus, TicketStatusLabels, enumOptions } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Permissions } from "../../auth/permissions";
import { ErrorMessage, LoadingSpinner, EmptyState } from "../../components/Feedback";
import { Pagination } from "../../components/Pagination";
import { PriorityBadge, StatusBadge } from "../../components/Status";
import { ui, formatDateTime } from "../../components/ui";

const PAGE_SIZE = 25;

export function TicketListPage() {
  const { can } = useAuth();
  const [status, setStatus] = useState<TicketStatus | "">("");
  const [priority, setPriority] = useState<TicketPriority | "">("");
  const [overdueOnly, setOverdueOnly] = useState(false);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);

  const { data, isPending, isError, error } = useQuery({
    queryKey: ["tickets", { status, priority, overdueOnly, search, page }],
    queryFn: () =>
      ticketsApi.search({
        status: status === "" ? undefined : status,
        priority: priority === "" ? undefined : priority,
        overdueOnly: overdueOnly || undefined,
        search: search || undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
    placeholderData: (prev) => prev,
  });

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className={ui.pageTitle + " mb-0"}>Ärenden</h1>
        {can(Permissions.TicketCreate) && (
          <Link to="/tickets/new" className={ui.buttonPrimary}>
            + Nytt ärende
          </Link>
        )}
      </div>

      <div className={`${ui.card} mb-4 flex flex-wrap items-end gap-3 p-4`}>
        <div>
          <label className={ui.label}>Sök</label>
          <input
            className={ui.input}
            placeholder="Titel..."
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
          />
        </div>
        <div>
          <label className={ui.label}>Status</label>
          <select
            className={ui.input}
            value={status}
            onChange={(e) => {
              setStatus(e.target.value === "" ? "" : (Number(e.target.value) as TicketStatus));
              setPage(1);
            }}
          >
            <option value="">Alla</option>
            {enumOptions(TicketStatusLabels).map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className={ui.label}>Prioritet</label>
          <select
            className={ui.input}
            value={priority}
            onChange={(e) => {
              setPriority(e.target.value === "" ? "" : (Number(e.target.value) as TicketPriority));
              setPage(1);
            }}
          >
            <option value="">Alla</option>
            {enumOptions(TicketPriorityLabels).map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700 pb-1.5">
          <input
            type="checkbox"
            checked={overdueOnly}
            onChange={(e) => {
              setOverdueOnly(e.target.checked);
              setPage(1);
            }}
          />
          Endast försenade
        </label>
      </div>

      {isPending && <LoadingSpinner />}
      {isError && <ErrorMessage error={error} />}

      {data && (
        <div className={ui.card}>
          {data.items.length === 0 ? (
            <EmptyState label="Inga ärenden matchar filtret." />
          ) : (
            <table className={ui.table}>
              <thead>
                <tr>
                  <th className={ui.th}>Titel</th>
                  <th className={ui.th}>Status</th>
                  <th className={ui.th}>Prioritet</th>
                  <th className={ui.th}>Organisation</th>
                  <th className={ui.th}>Tilldelad</th>
                  <th className={ui.th}>Förfaller</th>
                  <th className={ui.th}>Skapad</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {data.items.map((ticket) => (
                  <tr key={ticket.id} className="hover:bg-gray-50">
                    <td className={ui.td}>
                      <Link to={`/tickets/${ticket.id}`} className="font-medium text-atlas-700 hover:underline">
                        {ticket.title}
                      </Link>
                      {ticket.isOverdue && <span className="ml-2 text-xs font-semibold text-red-600">FÖRSENAD</span>}
                    </td>
                    <td className={ui.td}>
                      <StatusBadge status={ticket.status} />
                    </td>
                    <td className={ui.td}>
                      <PriorityBadge priority={ticket.priority} />
                    </td>
                    <td className={ui.td}>{ticket.organizationName}</td>
                    <td className={ui.td}>{ticket.assignedToUserName ?? "–"}</td>
                    <td className={ui.td}>{formatDateTime(ticket.dueAtUtc)}</td>
                    <td className={ui.td}>{formatDateTime(ticket.createdAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <div className="px-3">
            <Pagination result={data} onPageChange={setPage} />
          </div>
        </div>
      )}
    </div>
  );
}
