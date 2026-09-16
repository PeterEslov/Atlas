import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { projectsApi } from "../../api/projects";
import { useAuth } from "../../auth/AuthContext";
import { Permissions } from "../../auth/permissions";
import { EmptyState, ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { Pagination } from "../../components/Pagination";
import { formatDateTime, ui } from "../../components/ui";

const PAGE_SIZE = 25;

export function ProjectListPage() {
  const { can } = useAuth();
  const [search, setSearch] = useState("");
  const [isArchived, setIsArchived] = useState<boolean | "">("");
  const [page, setPage] = useState(1);

  const { data, isPending, isError, error } = useQuery({
    queryKey: ["projects", { search, isArchived, page }],
    queryFn: () =>
      projectsApi.search({ search: search || undefined, isArchived: isArchived === "" ? undefined : isArchived, page, pageSize: PAGE_SIZE }),
    placeholderData: (prev) => prev,
  });

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className={ui.pageTitle + " mb-0"}>Projekt</h1>
        {can(Permissions.ProjectManage) && (
          <Link to="/projects/new" className={ui.buttonPrimary}>+ Nytt projekt</Link>
        )}
      </div>

      <div className={`${ui.card} mb-4 flex flex-wrap items-end gap-3 p-4`}>
        <div>
          <label className={ui.label}>Sök</label>
          <input className={ui.input} value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700 pb-1.5">
          <input
            type="checkbox"
            checked={isArchived === true}
            onChange={(e) => { setIsArchived(e.target.checked ? true : ""); setPage(1); }}
          />
          Endast arkiverade
        </label>
      </div>

      {isPending && <LoadingSpinner />}
      {isError && <ErrorMessage error={error} />}

      {data && (
        <div className={ui.card}>
          {data.items.length === 0 ? (
            <EmptyState label="Inga projekt matchar filtret." />
          ) : (
            <table className={ui.table}>
              <thead>
                <tr>
                  <th className={ui.th}>Namn</th>
                  <th className={ui.th}>Medlemmar</th>
                  <th className={ui.th}>Status</th>
                  <th className={ui.th}>Skapad</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {data.items.map((p) => (
                  <tr key={p.id} className="hover:bg-gray-50">
                    <td className={ui.td}>
                      <Link to={`/projects/${p.id}`} className="font-medium text-atlas-700 hover:underline">{p.name}</Link>
                    </td>
                    <td className={ui.td}>{p.memberCount}</td>
                    <td className={ui.td}>
                      {p.isArchived ? (
                        <span className="inline-block rounded-full bg-gray-200 px-2.5 py-0.5 text-xs text-gray-600">Arkiverat</span>
                      ) : (
                        <span className="inline-block rounded-full bg-atlas-100 px-2.5 py-0.5 text-xs text-atlas-700">Aktivt</span>
                      )}
                    </td>
                    <td className={ui.td}>{formatDateTime(p.createdAtUtc)}</td>
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
