# CodeFlowIQ

Local-first, provider-agnostic, multi-language workspace intelligence.

CodeFlowIQ indexes local code workspaces so developers can search, inspect, and trace application flow before any AI model is involved.

## Phase 1 Scope

- Git repository and plain directory workspace support
- Incremental local file inventory
- Language detection
- C#, SQL/T-SQL, JavaScript, and TypeScript analyzer foundations
- SQLite persistence
- CLI commands: `init`, `sync`, `status`, `files`, `symbols`

AI, embeddings, cloud providers, work items, and UI are intentionally optional later phases.
