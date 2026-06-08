# CodeFlowIQ

CodeFlowIQ is a local-first code intelligence app for onboarding developers into unfamiliar repositories. It indexes a local Git repository or plain directory and explains what the codebase contains, where the application starts, and how runtime flows move through frontend, API, backend, database, SQL/stored procedures, and Azure/cloud dependencies.

The app is designed to work before any LLM or cloud provider is involved. Optional capabilities such as Git history, local embeddings, LLM assistant, work item providers, IDE integration, and enterprise governance should remain modular and independently turn-off-able.

## What CodeFlowIQ Does

- Indexes local Git repositories and plain local folders.
- Detects files, languages, symbols, APIs, relationships, Azure services, SQL objects, and runtime entry points.
- Builds a Runtime Map from entry points to execution flows.
- Connects frontend API calls to backend handlers where route templates match.
- Connects backend handlers/managers/repositories to SQL procedures, tables, and database operations when evidence exists.
- Shows source-backed evidence for each runtime step.
- Provides a React UI and local .NET API for repository exploration.

## Supported Stacks

- C#
- ASP.NET / ASP.NET Core
- Azure Functions
- SQL / T-SQL
- JavaScript / TypeScript
- React
- Angular
- Azure service usage signals

## Repository Layout

```text
CodeFlowIQ/
  src/
    CodeFlowIQ.Core/        Shared contracts and query DTOs
    CodeFlowIQ.Data/        SQLite persistence and EF migrations
    CodeFlowIQ.Analyzers/   C#, SQL/T-SQL, JS/TS, Angular analyzers
    CodeFlowIQ.Indexing/    Workspace indexing and query services
    CodeFlowIQ.Git/         Git workspace detection/support
    CodeFlowIQ.Api/         Local ASP.NET Core API
    CodeFlowIQ.Cli/         CLI surface
    CodeFlowIQ.UI/          React/Vite frontend
  tests/
    CodeFlowIQ.Tests/       Integration and indexing tests
```

## Prerequisites

Install these before running the app on a new machine:

- .NET SDK compatible with the target framework used by the solution.
  - Current project output targets `.NETCoreApp,Version=v10.0`.
  - Check with:

```powershell
dotnet --info
```

- Node.js and npm for the React UI.

```powershell
node --version
npm --version
```

- Git is recommended, but target workspaces do not have to be Git clones.

## Fresh Clone Setup

Clone the repository:

```powershell
git clone <your-codeflowiq-repo-url>
cd CodeFlowIQ
```

Restore and build the .NET solution:

```powershell
dotnet restore
dotnet build
```

Install UI dependencies:

```powershell
cd src\CodeFlowIQ.UI
npm.cmd ci
cd ..\..
```

Use `npm.cmd` on Windows PowerShell. Calling `npm` directly may hit PowerShell execution policy restrictions because it can resolve to `npm.ps1`.

## Run The App Locally

Run the API in one terminal:

```powershell
cd CodeFlowIQ
$env:CODEFLOWIQ_API_URLS='http://127.0.0.1:5188'
dotnet run --project src\CodeFlowIQ.Api\CodeFlowIQ.Api.csproj
```

Run the UI in a second terminal:

```powershell
cd CodeFlowIQ\src\CodeFlowIQ.UI
npm.cmd run dev
```

Open the UI:

```text
http://127.0.0.1:5173/
```

The UI defaults to API URL:

```text
http://127.0.0.1:5188
```

You can also set the UI API URL using:

```powershell
$env:VITE_CODEFLOWIQ_API_BASE_URL='http://127.0.0.1:5188'
npm.cmd run dev
```

## Check API Health

```powershell
Invoke-WebRequest -Uri 'http://127.0.0.1:5188/health' -UseBasicParsing
```

Expected response includes:

```json
{
  "status": "healthy",
  "name": "CodeFlowIQ.Api"
}
```

## Runtime Metadata

The API writes local runtime metadata to:

Windows:

```text
%LOCALAPPDATA%\CodeFlowIQ\runtime\api.json
```

macOS:

```text
~/Library/Application Support/CodeFlowIQ/runtime/api.json
```

Linux:

```text
~/.local/share/CodeFlowIQ/runtime/api.json
```

The API uses `CODEFLOWIQ_API_URLS` when supplied. If not supplied, the API can choose a dynamic localhost port. For development, prefer setting:

```powershell
$env:CODEFLOWIQ_API_URLS='http://127.0.0.1:5188'
```

## Index A Repository Or Folder

1. Start the API and UI.
2. Open `http://127.0.0.1:5173/`.
3. Enter a target workspace path, for example:

```text
C:\Users\you\source\SomeEnterpriseRepo
```

4. Click `Init` for a new workspace.
5. Click `Sync` after code changes or analyzer changes.
6. Use `Start here`, `Runtime map`, `Flow chains`, `Backend graph`, `API surface`, `Azure`, and `Files` to inspect the indexed workspace.

Important: existing indexed repositories should be synced again after analyzer changes so new relationship signals are captured.

## Main UI Areas

- `Start here`: guided repository overview, technologies, learning path, APIs, data touchpoints, Azure dependencies, and important folders.
- `Runtime map`: the primary onboarding surface. Shows recommended runtime stories, all start points, all flows, evidence, quality, and coverage.
- `Flow chains`: traces code relationships through frontend/API/backend/database chains.
- `Backend graph`: backend relationships such as method calls, procedure execution, reads, and writes.
- `API surface`: detected backend API routes and handlers.
- `Azure`: detected Azure/cloud service usage.
- `Files`: repository file browser with search and drill-down.

## Important API Endpoints

Base URL in local development:

```text
http://127.0.0.1:5188
```

Endpoints:

```text
GET  /health
POST /api/workspace/init
POST /api/workspace/sync
GET  /api/workspace/status
GET  /api/summary
GET  /api/overview
GET  /api/runtime-flows
GET  /api/files
GET  /api/symbols
GET  /api/relationships
GET  /api/apis
GET  /api/azure
GET  /api/flows
GET  /api/chains
GET  /api/backend
```

Example Runtime Map query:

```powershell
$path = [System.Uri]::EscapeDataString('C:\Users\you\source\SomeEnterpriseRepo')
Invoke-RestMethod -Uri "http://127.0.0.1:5188/api/runtime-flows?path=$path&take=10"
```

## Verification

Run backend tests:

```powershell
cd CodeFlowIQ
dotnet test
```

Build the UI:

```powershell
cd CodeFlowIQ\src\CodeFlowIQ.UI
npm.cmd run build
```

Expected current baseline:

```text
dotnet test: 29 passed
npm.cmd run build: passes
```

## Troubleshooting

### API DLL Locked During `dotnet test`

If tests fail because `CodeFlowIQ.Api` is locking DLLs, stop the running API process and rerun tests.

Find the process:

```powershell
Get-Process | Where-Object { $_.ProcessName -eq 'CodeFlowIQ.Api' } | Select-Object Id,ProcessName,Path
```

Stop it:

```powershell
Stop-Process -Id <process-id>
```

Then rerun:

```powershell
dotnet test
```

### UI Cannot Reach API

Check API health:

```powershell
Invoke-WebRequest -Uri 'http://127.0.0.1:5188/health' -UseBasicParsing
```

If the API is not running, start it with:

```powershell
$env:CODEFLOWIQ_API_URLS='http://127.0.0.1:5188'
dotnet run --project src\CodeFlowIQ.Api\CodeFlowIQ.Api.csproj
```

If using a non-default API port, set the UI field `API (saved)` in the app or run Vite with:

```powershell
$env:VITE_CODEFLOWIQ_API_BASE_URL='http://127.0.0.1:<port>'
npm.cmd run dev
```

### `npm` Fails In PowerShell

Use:

```powershell
npm.cmd <command>
```

Examples:

```powershell
npm.cmd ci
npm.cmd run dev
npm.cmd run build
```

### Runtime Map Looks Incomplete

- Click `Sync` after analyzer changes.
- Confirm the target workspace path is correct.
- Check `Runtime map -> Quality + Coverage`.
- `Detected only` means CodeFlowIQ found a start file but has not connected downstream evidence yet.
- `Inferred link` means CodeFlowIQ connected a flow using a project/domain fallback. Inspect evidence before relying on it as an exact call chain.

## Development Notes

- Keep optional capabilities modular and independently turn-off-able.
- Do not require Git for core code understanding.
- Prefer local deterministic analysis before LLM/embedding features.
- Keep indexing incremental and mindful of lower-memory machines.
- Runtime Map is the main onboarding surface and should stay readable, searchable, evidence-backed, and honest about gaps.

## Current UI Structure

The UI is being moved toward feature-based modules.

Current important files:

```text
src/CodeFlowIQ.UI/src/App.tsx
src/CodeFlowIQ.UI/src/types.ts
src/CodeFlowIQ.UI/src/runtime.ts
src/CodeFlowIQ.UI/src/styles.css
src/CodeFlowIQ.UI/src/features/runtime-map/RuntimeMapPanel.tsx
src/CodeFlowIQ.UI/src/features/runtime-map/runtime-map.css
```

Future extraction targets:

- Overview / Start Here
- Shared Metric, EmptyState, ResultExplorer
- Flow Chains
- Backend Graph
- API Surface
- Azure
- Files

## Product Direction

CodeFlowIQ should help a new developer answer:

- What is this repository?
- Where does the application start?
- Which runtime entry points exist?
- What happens step by step from entry point to frontend/API/backend/database/cloud?
- What source evidence supports each step?
- Where is the analysis exact, inferred, partial, or missing?

The product should feel like a local developer workbench, not a marketing dashboard.
