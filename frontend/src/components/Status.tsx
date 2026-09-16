import { TicketPriority, TicketPriorityLabels, TicketStatus, TicketStatusLabels } from "../api/types";

const STATUS_STYLES: Record<TicketStatus, string> = {
  [TicketStatus.New]: "bg-gray-100 text-gray-700",
  [TicketStatus.Open]: "bg-blue-100 text-blue-700",
  [TicketStatus.InProgress]: "bg-amber-100 text-amber-800",
  [TicketStatus.OnHold]: "bg-purple-100 text-purple-700",
  [TicketStatus.Resolved]: "bg-atlas-100 text-atlas-700",
  [TicketStatus.Closed]: "bg-gray-200 text-gray-600",
  [TicketStatus.Cancelled]: "bg-red-100 text-red-700",
};

const PRIORITY_STYLES: Record<TicketPriority, string> = {
  [TicketPriority.Low]: "bg-gray-100 text-gray-600",
  [TicketPriority.Medium]: "bg-blue-100 text-blue-700",
  [TicketPriority.High]: "bg-orange-100 text-orange-700",
  [TicketPriority.Critical]: "bg-red-100 text-red-800",
};

export function StatusBadge({ status }: { status: TicketStatus }) {
  return (
    <span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-medium ${STATUS_STYLES[status]}`}>
      {TicketStatusLabels[status]}
    </span>
  );
}

export function PriorityBadge({ priority }: { priority: TicketPriority }) {
  return (
    <span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-medium ${PRIORITY_STYLES[priority]}`}>
      {TicketPriorityLabels[priority]}
    </span>
  );
}

export function ActiveBadge({ isActive }: { isActive: boolean }) {
  return isActive ? (
    <span className="inline-block rounded-full bg-atlas-100 px-2.5 py-0.5 text-xs font-medium text-atlas-700">
      Aktiv
    </span>
  ) : (
    <span className="inline-block rounded-full bg-gray-200 px-2.5 py-0.5 text-xs font-medium text-gray-600">
      Inaktiv
    </span>
  );
}
