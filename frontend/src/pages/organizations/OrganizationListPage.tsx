import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { organizationsApi } from "../../api/organizations";
import { OrganizationType, OrganizationTypeLabels, enumOptions } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { Permissions } from "../../auth/permissions";
import { EmptyState, ErrorMessage, LoadingSpinner } from "../../components/Feedback";
import { Pagination } from "../../components/Pagination";
import { ActiveBadge } from "../../components/Status";
import { formatDateTime, ui } from "../../components/ui";

const PAGE_SIZE = 25;

export function OrganizationListPage() {
  const { can } = useAuth();
  const [type, setType] = useState<OrganizationType | "">("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);

  const { data, isPending, isError, error } = useQuery({
    queryKey: ["organizations", { type, search, page }],
    queryFn: () =>
      organizationsApi.search({ type: type === "" ? undefined : type, search: search || undefined, page, pageSize: PAGE_SIZE }),
    placeholderData: (prev) => prev,
  });

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className={ui.pageTitle + " mb-0"}>Organisationer</h1>
        {can(Permissions.OrganizationManage) && (
          <Link to="/organizations/new" className={ui.buttonPrimary}>
            + Ny organisation
          </Link>
        )}
      </div>

      <div className={`${ui.card} mb-4 flex flex-wrap items-end gap-3 p-4`}>
        <div>
          <label className={ui.label}>Sök</label>
          <input className={ui.input} value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
        </div>
        <div>
          <label className={ui.label}>Typ</label>
          <select
            className={ui.input}
            value={type}
            onChange={(e) => { setType(e.target.value === "" ? "" : (Number(e.target.value) as OrganizationType)); setPage(1); }}
          >
            <option value="">Alla</option>
            {enumOptions(OrganizationTypeLabels).map((opt) => (
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
            <EmptyState label="Inga organisationer matchar filtret." />
          ) : (
            <table className={ui.table}>
              <thead>
                <tr>
                  <th className={ui.th}>Namn</th>
                  <th className={ui.th}>Typ</th>
                  <th className={ui.th}>Status</th>
                  <th className={ui.th}>Skapad</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {data.items.map((org) => (
                  <tr key={org.id} className="hover:bg-gray-50">
                    <td className={ui.td}>
                      <Link to={`/organizations/${org.id}`} className="font-medium text-atlas-700 hover:underline">
                        {org.name}
                      </Link>
                    </td>
                    <td className={ui.td}>{OrganizationTypeLabels[org.type]}</td>
                    <td className={ui.td}><ActiveBadge isActive={org.isActive} /></td>
                    <td className={ui.td}>{formatDateTime(org.createdAtUtc)}</td>
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
