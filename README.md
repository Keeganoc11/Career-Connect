# Career Connect

A personal job application tracker — built as a real, end-to-end product to demonstrate backend engineering with ASP.NET Core, Entity Framework Core, and React.

Tracking a job search in a spreadsheet falls apart fast: statuses go stale, there's no history of what happened when, and the job descriptions you'll want later (for tailoring resumes) are scattered across tabs. Career Connect keeps the whole pipeline in one place, with status changes as first-class, auditable events.

## Features (Phase 1)

- **Pipeline dashboard** — live counts per status (Applied, Phone Screen, Interview, Offer, Rejected, Ghosted, Withdrawn), click a tile to filter the list
- **Sortable application list** — sort by date, status, company, or last activity; search by company/role
- **Inline status updates** — change status straight from the list; every transition is recorded in an append-only status history with a timestamp and provenance
- **Full CRUD** — add/edit applications with notes and the full pasted job description
- **JWT auth** — single seeded user today, but every query is user-scoped so multi-user is a data change, not a redesign

## Roadmap

| Phase | What | Why the data model is already ready |
|---|---|---|
| 1 | Manual tracker (this) | — |
| 2 | LLM-based resume ↔ job description match scoring | `JobDescriptionText` is captured on every application from day one |
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
