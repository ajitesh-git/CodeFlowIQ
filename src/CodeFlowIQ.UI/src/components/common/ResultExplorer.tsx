import { useMemo, useState } from "react";
import { EmptyState } from "./EmptyState";

type ResultExplorerProps = {
  emptyLabel: string;
  loadedLabel: string;
  rows: string[];
  searchPlaceholder: string;
};

export function ResultExplorer({
  emptyLabel,
  loadedLabel,
  rows,
  searchPlaceholder
}: ResultExplorerProps) {
  const [query, setQuery] = useState("");
  const filteredRows = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    if (normalized.length === 0) {
      return rows;
    }

    return rows.filter((row) => row.toLowerCase().includes(normalized));
  }, [query, rows]);

  return (
    <section className="result-explorer">
      <div className="result-explorer-bar">
        <label>
          Search loaded results
          <input
            placeholder={searchPlaceholder}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
        </label>
        <div>
          <strong>{filteredRows.length}</strong>
          <span>shown of {rows.length} loaded {loadedLabel}</span>
        </div>
      </div>
      <div className="row-list">
        {filteredRows.length === 0 ? (
          <EmptyState label={rows.length === 0 ? emptyLabel : "No loaded results match this search"} />
        ) : (
          filteredRows.map((row, index) => <code key={`${row}-${index}`}>{row}</code>)
        )}
      </div>
    </section>
  );
}
