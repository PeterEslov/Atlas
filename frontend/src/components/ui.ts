// Shared Tailwind class strings — not a component library (Peter chose
// "Tailwind, egen design" over MUI for Del 21), just enough repetition-
// avoidance that every page's buttons/inputs/cards look consistent without
// copy-pasting the same long className string into forty places.
export const ui = {
  card: "bg-white rounded-lg border border-gray-200 shadow-sm",
  input: "rounded border border-gray-300 px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-atlas-500 focus:border-atlas-500",
  label: "block text-sm font-medium text-gray-700 mb-1",
  buttonPrimary:
    "rounded bg-atlas-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-atlas-700 disabled:opacity-50 disabled:cursor-not-allowed",
  buttonSecondary:
    "rounded border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50",
  buttonDanger:
    "rounded bg-red-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50",
  table: "min-w-full divide-y divide-gray-200 text-sm",
  th: "px-3 py-2 text-left font-medium text-gray-500 uppercase text-xs tracking-wide",
  td: "px-3 py-2 whitespace-nowrap",
  pageTitle: "text-xl font-semibold text-gray-900 mb-4",
} as const;

export function formatDateTime(value: string | null): string {
  if (!value) return "–";
  return new Date(value).toLocaleString("sv-SE", { dateStyle: "short", timeStyle: "short" });
}
