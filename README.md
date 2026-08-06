# Career Connect

A personal job application tracker — built as a real, end-to-end product to demonstrate backend engineering with ASP.NET Core, Entity Framework Core, and React.

Tracking a job search in a spreadsheet falls apart fast: statuses go stale, there's no history of what happened when, and the job descriptions you'll want later (for tailoring resumes) are scattered across tabs. Career Connect keeps the whole pipeline in one place, with status changes as first-class, auditable events.

## Features

**Tracking (Phase 1)**

- **Pipeline dashboard** — live counts per status (Applied, Phone Screen, Interview, Offer, Rejected, Ghosted, Withdrawn), click a tile to filter the list
- **Sortable application list** — sort by date, status, company, match score, or last activity; search by company/role
- **Inline status updates** — change status straight from the list; every transition is recorded in an append-only status history with a timestamp and provenance
- **Full CRUD** — add/edit applications with notes and the full pasted job description
- **JWT auth** — single seeded user today, but every query is user-scoped so multi-user is a data change, not a redesign

**Match scoring (Phase 2)**

- **Resume library** — store multiple resume versions; the one marked *active* is what new scores run against
- **Resume ↔ job description scoring** — Claude compares your active resume to a posting and returns a 0-100 score, a short explanation, and covered requirements vs. gaps
- **Copy-paste-ready suggested edits** — each suggestion names the resume section it applies to, why it helps, and ready-to-paste text (bracketed placeholders where only you can supply the real specifics — the model is instructed never to invent achievements, metrics, or technologies)
- **Structured outputs** — the model's response is schema-constrained, so there's no fragile JSON parsing to fail at runtime
- **Score history** — re-scoring appends rather than overwrites, so progress after resume edits stays visible
- **Degrades cleanly** — with no API key configured, the tracker works exactly as before and scoring returns a clear "needs an API key" message rather than an error

## Roadmap

| Phase | What | Why the data model is already ready |
|---|---|---|
| 1 | Manual tracker | — |
| 2 | LLM-based resume ↔ job description match scoring | `JobDescriptionText` was captured on every application from day one |
| 3 | Job posting ingestion from a URL | Ingestion writes the same `Application` shape; source column is a one-line migration |
| 4 | Email-based status detection | `StatusChange` rows carry a `Source` enum (`Manual` today) — automated detection just writes rows with a new source |

## Tech stack

- **API** — ASP.NET Core Web API (.NET 8), Entity Framework Core 8, SQLite for local dev (provider chosen in one place; swapping to SQL Server/Postgres means regenerating `Data/Migrations` against the new provider), JWT bearer auth, Swagger in development
- **Client** — React 19 + TypeScript, Vite, Tailwind CSS v4
- **Tests** — xUnit against the service layer, run on SQLite in-memory (a real relational engine, so FK constraints and cascade deletes behave like production)
- **CI** — GitHub Actions: backend build + tests, frontend lint + typecheck + build on every push/PR

## Repo layout

```
api/
  CareerConnect.Api/        ASP.NET Core Web API
    Domain/                 Entities + enums
    Data/                   DbContext, migrations, seeder
    Services/               Business logic (thin controllers)
    Contracts/              Request/response DTOs
    Controllers/
  CareerConnect.Api.Tests/  Service-layer unit tests
client/                     React + TypeScript + Tailwind
.github/workflows/ci.yml    CI pipeline
```

## API surface

| Method | Route | Notes |
|---|---|---|
| POST | `/api/auth/login` | Returns a JWT |
| GET | `/api/applications` | List (user-scoped) |
| GET | `/api/applications/summary` | Counts per status for the dashboard |
| GET | `/api/applications/{id}` | Includes full status history |
| POST | `/api/applications` | Records the initial status-history entry |
| PUT | `/api/applications/{id}` | Field edits — deliberately cannot change status |
| PATCH | `/api/applications/{id}/status` | The one way status changes; appends to history |
| DELETE | `/api/applications/{id}` | Cascades status history |
| GET | `/api/applications/matches` | Latest match result per application, for the list view |
| GET | `/api/applications/{id}/match` | Latest match result for one application |
| POST | `/api/applications/{id}/match` | Runs a fresh scoring pass and stores it |
| GET/POST | `/api/resumes` | List / create resume versions |
| PUT/DELETE | `/api/resumes/{id}` | Edit or remove a resume |
| PATCH | `/api/resumes/{id}/active` | Choose which resume new scores use |

Status is excluded from the general update on purpose: it's the core interaction, and funneling every transition through one endpoint is what keeps the audit history complete.

## Running locally

Prereqs: .NET 8 SDK (or newer — projects set `RollForward`), Node 20+.

```bash
# API — http://localhost:5199 (Swagger UI at /swagger)
cd api/CareerConnect.Api
dotnet run
```

```bash
# Client — http://localhost:5173, proxies /api to the API
cd client
npm install
npm run dev
```

Development sign-in is seeded from `appsettings.Development.json` (`dev@careerconnect.local` / `devpassword1`). The JWT signing key and seed credentials there are development-only values; anything real belongs in user secrets or environment variables.

### Enabling match scoring

Match scoring calls the Claude API and needs an API key from [console.anthropic.com](https://console.anthropic.com). Store it in .NET user secrets so it never lands in the repo:

```bash
cd api/CareerConnect.Api && dotnet user-secrets init && dotnet user-secrets set "Anthropic:ApiKey" "YOUR_KEY_HERE"
```

An `ANTHROPIC_API_KEY` environment variable works too. Without either, everything else runs normally and the scoring endpoint returns a 503 explaining what's missing.

Optional overrides in `appsettings.json`: `Anthropic:Model` (default `claude-opus-5`) and `Anthropic:Effort` (`low`/`medium`/`high`/`max`, default `medium` — scoring is a bounded analysis task, so medium is the cost/quality sweet spot).

```bash
# Tests
dotnet test
```

The database (`careerconnect.db`) is created and migrated automatically on first run.

## Design decisions

- **Append-only `StatusChange` history** instead of just a status column — an audit trail now, and the landing zone for Phase 4's automated detection (`Source` enum) without backfilling.
- **Enums stored as strings** — readable in the database, and reordering the C# enum can never corrupt stored rows.
- **`DateOnly` for the application date** — you applied on a date, not at an instant; avoids timezone off-by-one bugs. All real timestamps are UTC.
- **User-scoped everything** — every service method takes the caller's user id from the JWT; "single user" is a row count, not an architecture.
- **Service layer owns the rules, controllers stay thin** — which is also what makes the unit tests cheap to write.
- **The LLM call sits behind an interface** (`IResumeMatchAnalyzer`) — so scoring logic is unit tested against a fake, with no network calls and no API key in CI.
- **Structured outputs instead of prompt-and-parse** — the response schema is enforced by the API, so a malformed model response isn't a failure mode the app has to handle.
- **Match results are append-only and record their model id** — scores from different models aren't comparable, and keeping history shows whether a resume edit actually helped.
- **Deleting a resume with scores attached is blocked** (409) rather than cascading — the resume text is the context that makes an old score meaningful.
- **Scoring failures are typed** (`MatchFailureReason`) and map to distinct status codes: 409 for "you need to add a job description first", 503 for "no API key", 502 for an upstream failure. The UI can tell the user what to fix instead of just "try again".
