import { BookOpen, Compass, RefreshCw, Search } from "lucide-react";
import { EmptyState } from "../../components/common/EmptyState";
import type { OverviewSection, RepositoryOverview, RepositoryOverviewItem } from "../../types";
import "./overview.css";

type OverviewPanelProps = {
  overview: RepositoryOverview | null;
  disabled: boolean;
  onLoad: () => void;
  onOpenItem: (section: OverviewSection, item: RepositoryOverviewItem) => void;
  onBrowseSection: (section: OverviewSection) => void;
};

export function OverviewPanel({
  overview,
  disabled,
  onLoad,
  onOpenItem,
  onBrowseSection
}: OverviewPanelProps) {
  if (!overview) {
    return (
      <div className="overview-empty">
        <div>
          <BookOpen size={24} />
          <h2>Start with a guided repository tour</h2>
          <p>Load an indexed workspace to see the main technologies, suggested learning path, detected flows, APIs, data touchpoints, Azure services, and important folders.</p>
        </div>
        <button onClick={onLoad} disabled={disabled}>
          <Compass size={17} /> Build overview
        </button>
      </div>
    );
  }

  return (
    <div className="overview-layout">
      <section className="overview-hero">
        <div>
          <span>{overview.kind}</span>
          <h2>{overview.name}</h2>
          <p>{overview.summary}</p>
        </div>
        <button onClick={onLoad} disabled={disabled}>
          <RefreshCw size={17} /> Refresh
        </button>
      </section>

      <section className="learning-path">
        <div className="section-heading">
          <h3>Suggested Learning Path</h3>
          <span>{overview.suggestedStartingPoints.length} steps</span>
        </div>
        <ol>
          {overview.suggestedStartingPoints.length === 0 ? (
            <li><EmptyState label="No starting points detected yet" /></li>
          ) : (
            overview.suggestedStartingPoints.map((item) => (
              <li key={`${item.title}-${item.kind}`}>
                <button className="learning-path-button" onClick={() => onOpenItem("guide", item)}>
                  <strong>{item.title}</strong>
                  <span>{item.detail}</span>
                </button>
              </li>
            ))
          )}
        </ol>
      </section>

      <div className="overview-grid">
        <OverviewList title="Technologies" section="technology" rows={overview.technologySignals} onOpenItem={onOpenItem} onBrowseSection={onBrowseSection} />
        <OverviewList title="Detected Flows" section="flow" rows={overview.detectedFlows} onOpenItem={onOpenItem} onBrowseSection={onBrowseSection} />
        <OverviewList title="Important APIs" section="api" rows={overview.importantApis} onOpenItem={onOpenItem} onBrowseSection={onBrowseSection} />
        <OverviewList title="Data Touchpoints" section="data" rows={overview.dataTouchpoints} onOpenItem={onOpenItem} onBrowseSection={onBrowseSection} />
        <OverviewList title="Azure Dependencies" section="azure" rows={overview.azureDependencies} onOpenItem={onOpenItem} onBrowseSection={onBrowseSection} />
        <OverviewList title="Important Folders" section="folder" rows={overview.importantFolders} onOpenItem={onOpenItem} onBrowseSection={onBrowseSection} />
      </div>
    </div>
  );
}

function OverviewList({
  title,
  section,
  rows,
  onOpenItem,
  onBrowseSection
}: {
  title: string;
  section: OverviewSection;
  rows: RepositoryOverviewItem[];
  onOpenItem: (section: OverviewSection, item: RepositoryOverviewItem) => void;
  onBrowseSection: (section: OverviewSection) => void;
}) {
  return (
    <section className="overview-list">
      <div className="section-heading">
        <h3>{title}</h3>
        <span>{rows.length}</span>
      </div>
      <div>
        {rows.length === 0 ? (
          <EmptyState label="No items detected" />
        ) : (
          rows.map((row) => (
            <button className="overview-drill-card" key={`${title}-${row.title}-${row.detail}`} onClick={() => onOpenItem(section, row)}>
              <strong>{row.title}</strong>
              <span>{row.detail}</span>
            </button>
          ))
        )}
      </div>
      <button className="browse-section-button" onClick={() => onBrowseSection(section)}>
        <Search size={16} /> Browse all
      </button>
    </section>
  );
}
