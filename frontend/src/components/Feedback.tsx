import { ApiError } from "../api/client";

export function LoadingSpinner({ label = "Laddar..." }: { label?: string }) {
  return <div className="py-8 text-center text-sm text-gray-500">{label}</div>;
}

/** Renders an ApiError's ProblemDetails.detail — the exact message ExceptionHandlingMiddleware sent — rather than a generic "something went wrong". */
export function ErrorMessage({ error }: { error: unknown }) {
  const message =
    error instanceof ApiError
      ? error.problem?.detail ?? error.message
      : error instanceof Error
        ? error.message
        : "Ett okänt fel inträffade.";

  return (
    <div className="rounded border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{message}</div>
  );
}

export function EmptyState({ label }: { label: string }) {
  return <div className="py-8 text-center text-sm text-gray-400">{label}</div>;
}
