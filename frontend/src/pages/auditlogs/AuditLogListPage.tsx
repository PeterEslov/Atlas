import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { auditLogsApi } from "../../api/auditLogs";
import { EmptyState, ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { Pagination } from "../../components/Pagination";
import { formatDateTime, ui } from "../../components/ui";

const PAGE_SIZE = 25;

function ValuesCell({ label, json }: { label: string; json: string | null }) {
  if (!json) return <span className="text-gray-300">–</span>;
  return (
    <details>
      <summary className="cursor-pointer text-atlas-700">{label}</summary>
      <pre className="mt-1 max-w-xs overflow-x-auto rounded bg-gray-50 p-2 text-xs">{json}</pre>
    </details>
  );
}

export function AuditLogListPage() {
  const [entityName, setEntityName] = useState("");
  const [action, setAction] = useState("");
  const [page, setPage] = useState(1);

  const { data, isPending, isError, error } = useQuery({
    queryKey: ["audit-logs", { entityName, action, page }],
    queryFn: () =>
      auditLogsApi.search({ entityName: entityName || undefined, action: action || undefined, page, pageSize: PAGE_SIZE }),
    placeholderData: (prev) => prev,
  });

  return (
    <div>
      <h1 className={ui.pageTitle}>Audit Log</h1>
      <p className="mb-4 text-sm text-gray-500">
        Systemomfattande spårlogg (Del 15) — Admin-only, se AuditLogsController. Ingen per-organisationsfiltrering
        finns ännu (se docs/ARCHITECTURE.md), så det här visar händelser för alla organisationer.
      </p>

      <div className={`${ui.card} mb-4 flex flex-wrap items-end gap-3 p-4`}>
        <div>
          <label className={ui.label}>Entitet</label>
          <input
            className={ui.input}
            placeholder="Ticket, User, Organization, Project, Team..."
            value={entityName}
            onChange={(e) => { setEntityName(e.target.value); setPage(1); }}
          />
        </div>
        <div>
          <label className={ui.label}>Action</label>
          <input
            className={ui.input}
            placeholder="Created, Updated, Deleted..."
            value={action}
            onChange={(e) => { setAction(e.target.value); setPage(1); }}
          />
        </div>
      </div>

      {isPending && <LoadingSpinner />}
      {isError && <ErrorMessage error={error} />}

      {data && (
        <div className={ui.card}>
          {data.items.length === 0 ? (
            <EmptyState label="Inga händelser matchar filtret." />
          ) : (
            <table className={ui.table}>
              <thead>
                <tr>
                  <th className={ui.th}>Tid</th>
                  <th className={ui.th}>Utförd av</th>
                  <th className={ui.th}>Action</th>
                  <th className={ui.th}>Entitet</th>
                  <th className={ui.th}>Gamla värden</th>
                  <th className={ui.th}>Nya värden</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {data.items.map((log) => (
                  <tr key={log.id} className="hover:bg-gray-50 align-top">
                    <td className={ui.td}>{formatDateTime(log.timestampUtc)}</td>
                    <td className={ui.td}>{log.actorFullName ?? log.userId}</td>
                    <td className={ui.td}>{log.action}</td>
                    <td className={ui.td}>
                      {log.entityName} <span className="text-gray-400 font-mono text-xs">{log.entityId.slice(0, 8)}</span>
                    </td>
                    <td className={ui.td}><ValuesCell label="Visa" json={log.oldValuesJson} /></td>
                    <td className={ui.td}><ValuesCell label="Visa" json={log.newValuesJson} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <div className="px-3"><Pagination result={data} onPageChange={setPage} /></div>
        </div>
      )}
    </div>
  );
}
