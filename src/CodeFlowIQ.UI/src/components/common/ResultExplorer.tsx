import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import { EmptyState } from "./EmptyState";
import "./common.css";

type ResultExplorerProps = {
  emptyLabel: string;
  loadedLabel: string;
  rows: string[];
  searchPlaceholder: string;
  onOpenRow?: (row: string) => void;
};

export function ResultExplorer({
  emptyLabel,
  loadedLabel,
  onOpenRow,
  rows,
  searchPlaceholder
}: ResultExplorerProps) {
  const [query, setQuery] = useState("");
  const filteredRows = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    if (normalized.length === 0) {
      return rows;
    }

    return rows.filter((row) => getDisplayRow(row).toLowerCase().includes(normalized));
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
          filteredRows.map((row, index) => (
            <div className="result-row" key={`${row}-${index}`}>
              <div className="result-row-content">
                <strong>{getReadableRowTitle(row)}</strong>
                <span>{getReadableRowDetail(row)}</span>
                <code>{getDisplayRow(row)}</code>
              </div>
              {onOpenRow && (
                <button onClick={() => onOpenRow(row)} title="Open full evidence in Repository Explorer" type="button">
                  <Search size={15} /> View evidence
                </button>
              )}
            </div>
          ))
        )}
      </div>
    </section>
  );
}

function getReadableRowTitle(row: string) {
  const parts = splitDisplayRow(row);
  if (parts.length >= 3) {
    return `${formatReadableToken(parts[0])} -> ${formatReadableToken(parts[2])}`;
  }

  if (parts.length === 2) {
    return formatReadableToken(parts[0]);
  }

  return formatReadableToken(row);
}

function getReadableRowDetail(row: string) {
  const parts = splitDisplayRow(row);
  if (parts.length >= 3) {
    return `Relationship: ${formatReadableToken(parts[1])}`;
  }

  if (parts.length === 2) {
    return `Found in ${formatReadableToken(parts[1])}`;
  }

  return "Open the evidence view to inspect the exact source record.";
}

function getDisplayRow(row: string) {
  return splitDisplayRow(row).join("\t");
}

function splitDisplayRow(row: string) {
  return row
    .split("\t")
    .map((part) => part.trim())
    .filter((part) => part.length > 0 && !/^(file|relationship):\d+$/i.test(part));
}

function formatReadableToken(value: string) {
  const withoutKind = value.includes(":") ? value.slice(value.indexOf(":") + 1) : value;
  const afterPath = withoutKind.split(/[\\/]/).filter(Boolean).pop() ?? withoutKind;
  const afterMember = afterPath.includes("::") ? afterPath.slice(afterPath.lastIndexOf("::") + 2) : afterPath;
  return afterMember.replace(/_/g, " ").trim() || value;
}
