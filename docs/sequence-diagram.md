# CodeFlowIQ Sequence Diagram

This diagram shows the main developer workflow: connect the UI to the local API, index a repository, load insights, and drill into evidence.

```mermaid
sequenceDiagram
    actor Dev as Developer
    participant UI as CodeFlowIQ UI
    participant API as Local API
    participant Jobs as Indexing Job Service
    participant Index as Indexing Service
    participant Analyzer as Language Analyzers
    participant Store as Local SQLite Store
    participant Query as Query Handlers

    Dev->>UI: Enter API URL and repository path
    UI->>API: GET /health
    API-->>UI: healthy + runtime metadata

    Dev->>UI: Index repo
    UI->>API: POST /api/workspace/init
    API->>Jobs: Start background indexing job
    Jobs->>Index: Index selected workspace
    Index->>Analyzer: Analyze files, symbols, routes, SQL, Azure, relationships
    Analyzer->>Store: Save source-backed evidence
    Jobs-->>API: Progress, status, errors
    UI->>API: GET /api/workspace/indexing-status
    API-->>UI: Progress updates

    Dev->>UI: Load insights
    UI->>API: GET /api/overview, /api/runtime-flows, /api/explorer/*
    API->>Query: Execute focused query handlers
    Query->>Store: Read files, symbols, relationships, evidence
    Store-->>Query: Indexed evidence
    Query-->>API: Product-shaped DTOs
    API-->>UI: Overview, Runtime Map, Explorer rows, trace data

    Dev->>UI: Click source evidence or trace step
    UI->>API: GET /api/explorer/related or trace endpoint
    API->>Query: Resolve selected evidence context
    Query->>Store: Load nearby caller/callee/data/cloud relationships
    API-->>UI: Related evidence and source previews
    UI-->>Dev: Focused drill-down view
```

## Important User Journeys

1. New developer opens `Start here` to understand the repository at a high level.
2. Developer opens `Runtime stories` to see curated entry points before browsing all flows.
3. Developer opens `Browse evidence` to inspect the full indexed repository without preview limits.
4. Developer opens `C# backend trace` to follow one API route through controller, DI handoff, manager, repository, SQL, and cloud boundaries.
5. Developer uses Settings to tune API URL, theme, and trace behavior without scrolling the sidebar.

## Sequence Quality Goals

- Indexing must be backgrounded and report progress.
- Long-running analysis should not freeze the UI.
- Each drill-down should land on the most exact source-backed evidence row available.
- Unresolved DI handoffs, framework calls, and inferred links should be visible to the user as analysis quality signals.
