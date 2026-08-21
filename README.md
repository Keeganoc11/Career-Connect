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

- **API** — ASP.NET Core Web API (.NET 8), Entity Framework Core 8, Postgres (Npgsql), JWT bearer auth, Swagger in development
- **Deployment** — single Docker image (see `Dockerfile`): the API serves the built React client as static files, so there's one deployed service and no CORS/cross-origin concerns in production
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

Prereqs: .NET 8 SDK (or newer — projects set `RollForward`), Node 20+, a local Postgres server.

Postgres via Homebrew:

```bash
brew install postgresql@14
brew services start postgresql@14
createdb careerconnect
```

By default the API connects as your macOS username with no password (Postgres's local trust/peer auth) — see `ConnectionStrings:Default` in `appsettings.Development.json` if your local setup differs (different username, a password, Docker Postgres on a different port, etc.).

The API and client are two separate processes — run each in its own terminal, at the same time.

**Terminal 1 — API** (http://localhost:5199, Swagger UI at `/swagger`):

```bash
cd api/CareerConnect.Api
dotnet run
```

**Terminal 2 — client** (http://localhost:5173, proxies `/api` to the API):

```bash
cd client
npm install
npm run dev
```

Then open http://localhost:5173. The client needs the API running to do anything — if you see "Can't reach the API server," terminal 1 either isn't running or is still starting up.

To stop either one, click into its terminal and press `Ctrl+C`.

Development sign-in is seeded from `appsettings.Development.json` (`dev@careerconnect.local` / `devpassword1`). The JWT signing key and seed credentials there are development-only values; anything real belongs in user secrets or environment variables.

**Port already in use?** That usually means a previous `dotnet run` is still running in the background from an earlier session. Find and stop it:

```bash
lsof -ti:5199 | xargs kill   # API; use 5173 for the client
```

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

The database schema is created and migrated automatically on first run (`DbSeeder` calls `Database.MigrateAsync()` at startup).

### Enabling Gmail-based status detection

Needs a Google OAuth client (OAuth consent screen + credentials at [console.cloud.google.com](https://console.cloud.google.com), scope `gmail.readonly`, redirect URI `http://localhost:5199/api/gmail/callback` for local dev):

```bash
cd api/CareerConnect.Api
dotnet user-secrets set "Gmail:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "Gmail:ClientSecret" "YOUR_CLIENT_SECRET"
```

Without these, everything else runs normally and Gmail endpoints return a 503 explaining what's missing.

Once connected, Gmail is scanned automatically in the background (not just when you click "Check for updates") — once a day by default. Override with `Gmail:ScanIntervalHours` (set to `0` to disable). Findings are stored and surfaced the next time you open the app, same review-before-accept flow as a manual scan — nothing is ever applied automatically.

## Deploying (Railway)

The app ships as a single Docker image (`Dockerfile` at the repo root) — the API serves the built React client as static files, so there's one deployed service, one URL, and no CORS configuration needed in production.

1. **Push this repo to GitHub** if it isn't already (Railway deploys from a repo).
2. **Create a Railway project** at [railway.com](https://railway.com) and add a service from your GitHub repo — Railway detects the root `Dockerfile` automatically.
3. **Add a Postgres database** to the same Railway project (`+ New` → `Database` → `PostgreSQL`). Railway does **not** auto-inject that service's variables into your API service — on the API service's Variables tab, add `DATABASE_URL` with value `${{Postgres.DATABASE_URL}}` (use whatever your Postgres service is actually named in the reference). `PostgresConnectionString.Resolve` (in `Data/PostgresConnectionString.cs`) reads that env var and converts it to Npgsql's format. If you'd rather supply a connection string directly, set `ConnectionStrings__Default` instead (Npgsql format, not the `postgres://` URI shape) — this takes priority over `DATABASE_URL`.
4. **Add a volume** for the Data Protection key ring (`+ New` → `Volume`, mount path e.g. `/data`) and set `DataProtection__KeysPath=/data/keys`. This persists the key that encrypts the stored Gmail refresh token across redeploys — without it, every redeploy generates a fresh key and silently breaks any existing Gmail connection.
5. **Set environment variables** on the service (Railway dashboard → Variables):

   | Variable | Value |
   |---|---|
   | `Jwt__Key` | A long random secret (e.g. `openssl rand -base64 48`) — never reuse the dev value from `appsettings.Development.json` |
   | `Seed__Email` / `Seed__Password` | Your real login for this deployed instance — **do not** reuse the dev seed credentials |
   | `ANTHROPIC_API_KEY` | Your Claude API key (optional — match scoring/email classification stay disabled without it) |
   | `Gmail__ClientId` / `Gmail__ClientSecret` | Your Google OAuth client (optional — Gmail features stay disabled without it) |
   | `Gmail__RedirectUri` | `https://<your-railway-domain>/api/gmail/callback` |
   | `DataProtection__KeysPath` | `/data/keys` (from step 4) |

   `App__ClientOrigin` and `Cors__AllowedOrigins` should stay **unset** in production — the client is same-origin with the API, so neither is needed.

6. **Register the production redirect URI with Google.** In Google Cloud Console, on the same OAuth client used for local dev, add `https://<your-railway-domain>/api/gmail/callback` to "Authorized redirect URIs" alongside the existing localhost one — don't replace it, or local dev's Gmail connect stops working.
7. **Deploy.** Railway builds the `Dockerfile` and starts the container; `DbSeeder` migrates the database and seeds your login user on first boot. Watch the deploy logs for the `Now listening on` line, then open the Railway-assigned domain and sign in with the `Seed__Email` / `Seed__Password` you set in step 5.

Redeploys are safe to run repeatedly — migrations only apply what's new, and the seeder skips creating the user if it already exists.

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
