import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { usersApi } from "../../api/users";
import { UserRole, UserRoleLabels, enumOptions } from "../../api/types";
import { EmptyState, ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { Pagination } from "../../components/Pagination";
import { ActiveBadge } from "../../components/Status";
import { ui } from "../../components/ui";

const PAGE_SIZE = 25;

export function UserListPage() {
  const [role, setRole] = useState<UserRole | "">("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);

  const { data, isPending, isError, error } = useQuery({
    queryKey: ["users", { role, search, page }],
    queryFn: () => usersApi.search({ role: role === "" ? undefined : role, search: search || undefined, page, pageSize: PAGE_SIZE }),
    placeholderData: (prev) => prev,
  });

  return (
    <div>
      <h1 className={ui.pageTitle}>Användare</h1>

      <div className={`${ui.card} mb-4 flex flex-wrap items-end gap-3 p-4`}>
        <div>
          <label className={ui.label}>Sök</label>
          <input className={ui.input} value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
        </div>
        <div>
          <label className={ui.label}>Roll</label>
          <select className={ui.input} value={role} onChange={(e) => { setRole(e.target.value === "" ? "" : (Number(e.target.value) as UserRole)); setPage(1); }}>
            <option value="">Alla</option>
            {enumOptions(UserRoleLabels).map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
        </div>
      </div>

      {isPending && <LoadingSpinner />}
      {isError && <ErrorMessage error={error} />}

      {data && (
        <div className={ui.card}>
          {data.items.length === 0 ? (
            <EmptyState label="Inga användare matchar filtret." />
          ) : (
            <table className={ui.table}>
              <thead>
                <tr>
                  <th className={ui.th}>Namn</th>
                  <th className={ui.th}>E-post</th>
                  <th className={ui.th}>Roll</th>
                  <th className={ui.th}>Organisation</th>
                  <th className={ui.th}>Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {data.items.map((u) => (
                  <tr key={u.id} className="hover:bg-gray-50">
                    <td className={ui.td}>
                      <Link to={`/users/${u.id}`} className="font-medium text-atlas-700 hover:underline">{u.fullName}</Link>
                    </td>
                    <td className={ui.td}>{u.email}</td>
                    <td className={ui.td}>{UserRoleLabels[u.role]}</td>
                    <td className={ui.td}>{u.organizationName}</td>
                    <td className={ui.td}><ActiveBadge isActive={u.isActive} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <div className="px-3"><Pagination result={data} onPageChange={setPage} /></div>
        </div>
      )}
      <div className="mt-2 text-xs text-gray-400">
        Nya användare skapas via registreringsformuläret på inloggningssidan, inte här (se AuthController.Register).
      </div>
    </div>
  );
}
