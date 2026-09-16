import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { teamsApi } from "../../api/teams";
import { useAuth } from "../../auth/AuthContext";
import { Permissions } from "../../auth/permissions";
import { EmptyState, ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { Pagination } from "../../components/Pagination";
import { formatDateTime, ui } from "../../components/ui";

const PAGE_SIZE = 25;

export function TeamListPage() {
  const { can } = useAuth();
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);

  const { data, isPending, isError, error } = useQuery({
    queryKey: ["teams", { search, page }],
    queryFn: () => teamsApi.search({ search: search || undefined, page, pageSize: PAGE_SIZE }),
    placeholderData: (prev) => prev,
  });

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className={ui.pageTitle + " mb-0"}>Team</h1>
        {can(Permissions.TeamManage) && (
          <Link to="/teams/new" className={ui.buttonPrimary}>+ Nytt team</Link>
        )}
      </div>

      <div className={`${ui.card} mb-4 flex flex-wrap items-end gap-3 p-4`}>
        <div>
          <label className={ui.label}>Sök</label>
          <input className={ui.input} value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
        </div>
      </div>

      {isPending && <LoadingSpinner />}
      {isError && <ErrorMessage error={error} />}

      {data && (
        <div className={ui.card}>
          {data.items.length === 0 ? (
            <EmptyState label="Inga team matchar filtret." />
          ) : (
            <table className={ui.table}>
              <thead>
                <tr>
                  <th className={ui.th}>Namn</th>
                  <th className={ui.th}>Medlemmar</th>
                  <th className={ui.th}>Skapad</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {data.items.map((t) => (
                  <tr key={t.id} className="hover:bg-gray-50">
                    <td className={ui.td}>
                      <Link to={`/teams/${t.id}`} className="font-medium text-atlas-700 hover:underline">{t.name}</Link>
                    </td>
                    <td className={ui.td}>{t.memberCount}</td>
                    <td className={ui.td}>{formatDateTime(t.createdAtUtc)}</td>
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
