# CodeFlowIQ Product Understanding

CodeFlowIQ is a local intelligence workspace for understanding unfamiliar software repositories. Its job is to help a developer answer, in minutes, questions that usually require hours of manual searching, debugging, and asking senior team members.

## Product Vision

CodeFlowIQ should become a developer-friendly map of a repository:

- What is this application?
- Which technologies does it use?
- Where does execution start?
- Which APIs are exposed?
- Which backend methods, repositories, SQL objects, and Azure services are involved?
- Where is the source evidence?
- Which parts are exact, inferred, partial, duplicated, or unresolved?

The long-term product should feel less like a static code search page and more like a local developer workbench for onboarding, impact analysis, runtime understanding, and evidence-backed navigation.

## Target Users

- New developers joining a large codebase.
- Senior engineers doing impact analysis before changing a feature.
- Tech leads reviewing architecture and runtime dependencies.
- Support engineers trying to understand where a request flows.
- Developers working on enterprise repositories with C#, SQL, JavaScript, TypeScript, Angular, React, and Azure.

## Current Product Pillars

### 1. Start Here

The overview screen gives a beginner-friendly first read of the repository. It summarizes the workspace, technologies, important folders, flows, API surface, data touchpoints, and cloud usage.

### 2. Runtime Map

Runtime Map is the curated onboarding view. It should avoid dumping everything at once. The default experience should show recommended stories, then let the user move to all start points, all flows, or evidence when they need more detail.

### 3. Repository Explorer

Repository Explorer is the full evidence workspace. It should be the destination for drill-down actions from Overview, Runtime Map, API Endpoints, Backend & Data, Cloud Services, Files Indexed, and End-to-End Flows.

### 4. End-to-End Flows

End-to-End Flows help answer: "If this API or feature runs, what does it eventually touch?" The view should support pagination or virtualization so large repositories remain readable.

### 5. C# Backend Trace

C# Backend Trace is focused on backend execution understanding. It should trace API routes through controller methods, DI handoffs, concrete implementations, base classes, repositories, SQL objects, cloud boundaries, and unresolved handoffs.

This feature exists because developers debug execution paths, not just relationships. It should become a clear, step-by-step execution trace rather than a noisy graph of related evidence.

### 6. Settings

Settings belong on a dedicated page, not buried at the bottom of a long sidebar. Theme, API URL, trace depth, framework call visibility, and boundary-call visibility should be easy to find.

## UX Principles

- Default views should be curated and readable.
- Full data should be available through browse, search, filters, pagination, and drill-down.
- Do not show every record on one page.
- Use plain product language before technical jargon.
- Explain why something matters, not only what the analyzer found.
- Keep evidence visible and source-backed.
- Make duplicates understandable by showing the file, method, route, table, relationship, and occurrence context.
- Preserve navigation context when moving from a curated view into Repository Explorer.
- Dark and light themes must both remain readable.

## Analysis Principles

- Treat each technology stack separately first: C#, SQL, frontend, Azure.
- Stitching across stacks should happen only where source evidence supports it.
- DI, factory patterns, keyed services, base-class calls, and repository handoffs need explicit relationship modeling.
- Unresolved handoffs are useful product signals and should be shown honestly.
- Large enterprise repositories need background indexing, progress, cancellation, and retry behavior.

## Near-Term Roadmap

1. Improve C# trace resolver quality for DI, keyed services, factory patterns, base classes, repositories, SQL, and Azure boundaries.
2. Make Repository Explorer the central evidence workspace for every feature entry point.
3. Add stronger grouping, filters, and pagination for large evidence sets.
4. Expand trace and evidence contracts with exact source location support.
5. Add stack-specific trace experiences for SQL and frontend after C# backend trace is stable.
