import type { PagedResult } from "../api/types";

export function Pagination<T>({
  result,
  onPageChange,
}: {
  result: PagedResult<T>;
  onPageChange: (page: number) => void;
}) {
  if (result.totalCount === 0) return null;

  return (
    <div className="flex items-center justify-between border-t px-1 py-3 text-sm text-gray-600">
      <span>
        Sida {result.page} av {result.totalPages} ({result.totalCount} totalt)
      </span>
      <div className="flex gap-2">
        <button
          className="rounded border px-3 py-1 disabled:opacity-40"
          disabled={!result.hasPreviousPage}
          onClick={() => onPageChange(result.page - 1)}
        >
          Föregående
        </button>
        <button
          className="rounded border px-3 py-1 disabled:opacity-40"
          disabled={!result.hasNextPage}
          onClick={() => onPageChange(result.page + 1)}
        >
          Nästa
        </button>
      </div>
    </div>
  );
}
