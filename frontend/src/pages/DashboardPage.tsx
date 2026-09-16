import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { ticketsApi } from "../api/tickets";
import { ErrorMessage, LoadingSpinner } from "../components/Feedback";
import { ui } from "../components/ui";
import { useAuth } from "../auth/AuthContext";

function StatCard({ label, value, accent }: { label: string; value: number; accent?: string }) {
  return (
    <div className={`${ui.card} p-4`}>
      <div className="text-2xl font-bold text-gray-900">{value}</div>
      <div className={`text-sm ${accent ?? "text-gray-500"}`}>{label}</div>
    </div>
  );
}

export function DashboardPage() {
  const { session } = useAuth();
  const { data, isPending, isError, error } = useQuery({
    queryKey: ["tickets", "stats"],
    queryFn: () => ticketsApi.stats(),
  });

  return (
    <div>
      <h1 className={ui.pageTitle}>Hej, {session?.fullName.split(" ")[0]}!</h1>

      {isPending && <LoadingSpinner />}
      {isError && <ErrorMessage error={error} />}

      {data && (
        <>
          <div className="mb-2 text-xs text-gray-400">
            Genererad {new Date(data.generatedAtUtc).toLocaleString("sv-SE")} — cachad via Redis i upp till 30s
            (Del 13), så en ny ärendeändring kan dröja lite innan den syns här.
          </div>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5 mb-6">
            <StatCard label="Totalt" value={data.totalCount} />
            <StatCard label="Nya" value={data.newCount} />
            <StatCard label="Öppna" value={data.openCount} />
            <StatCard label="Pågår" value={data.inProgressCount} />
            <StatCard label="Väntar" value={data.onHoldCount} />
            <StatCard label="Lösta" value={data.resolvedCount} />
            <StatCard label="Stängda" value={data.closedCount} />
            <StatCard label="Avbrutna" value={data.cancelledCount} />
            <StatCard label="Försenade" value={data.overdueCount} accent="text-red-600 font-medium" />
          </div>

          <h2 className="text-sm font-semibold text-gray-700 mb-2">Per prioritet</h2>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4 mb-6">
            <StatCard label="Låg" value={data.lowPriorityCount} />
            <StatCard label="Medel" value={data.mediumPriorityCount} />
            <StatCard label="Hög" value={data.highPriorityCount} />
            <StatCard label="Kritisk" value={data.criticalPriorityCount} accent="text-red-600 font-medium" />
          </div>
        </>
      )}

      <Link to="/tickets" className="text-sm text-atlas-700 hover:underline">
        Visa alla ärenden →
      </Link>
    </div>
  );
}
