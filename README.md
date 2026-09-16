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

**Automated prep (Phase 5)**

The four AI features above are individually useful but were each a manual button. Prep chains them into the actual workflow — find a posting, paste it, and the app gets you to the point of applying:

- **One pass, end to end** — scores your active resume against the posting, rewrites it, re-scores the rewrite, and repeats until it clears a target score (default 80, `Prep:TargetScore`); then writes a cover letter against whichever version won
- **Knows when to stop** — a resume that already clears the target is never rewritten, and a pass that doesn't improve the score ends the loop rather than drifting further from the original. Capped at 3 passes regardless
- **Keeps the best version, not the last** — a rewrite that scores worse than the original is discarded; a tie is kept, since it's still phrased in the posting's language
- **Runs in the background** — a pass is several chained model calls, so the request records a `PrepRun` and returns immediately. Progress is written step by step, so the UI follows along live and closing the tab loses nothing
- **"You're good to apply"** — the verdict is explicit, with the tailored resume and cover letter editable and copy-ready beneath it
- **Survives restarts** — a run interrupted by a deploy is closed out on next startup instead of polling forever

**Pipeline stage: Preparing**

Applications now start at `Preparing` — found, not yet applied to. That's the stage prep runs in, and it's what makes the Gmail loop close: when a confirmation email arrives for a company you were preparing, the application moves to `Applied` on its own, dated to the email. Every other transition still goes through review — confirming an application you already decided to submit isn't a judgement call, but inferring an interview or rejection is.

**After you apply (Phase 6)**

The pipeline used to go quiet the moment you hit send. A search spends one afternoon preparing and three months in everything after it, so that half now does real work too:

- **Interviews are real records, not a status label** — multiple rounds per application, each with a time, kind, and notes. A phone screen and the onsite two weeks later both exist, and past rounds stay visible after the status moves on
- **Your calendar, automatically** — a scheduled interview is mirrored to Google Calendar, and edits and cancellations follow it there rather than stranding a duplicate. There's an `.ics` download too, for any calendar app and no OAuth at all
- **Gmail reads the time out of the invite** — when a scan spots an interview email, a second pass opens that message and extracts the date. Accepting the suggestion books it in the same action, with the time shown editable first
- **An Agenda that answers "what today?"** — upcoming interviews, plus applications that have gone quiet, escalating from "worth a follow-up" at two weeks to "probably ghosted" at a month. This is where prep closes its loop: *prepped and never sent* is the first thing it tells you
- **Interview prep is kept** — generated once and stored, so it's still there the morning of the interview instead of costing another model call

## Roadmap

| Phase | What | Why the data model was already ready |
|---|---|---|
| 1 | Manual tracker | — |
| 2 | LLM-based resume ↔ job description match scoring | `JobDescriptionText` was captured on every application from day one |
| 3 | Job posting ingestion from a URL | Ingestion writes the same `Application` shape |
| 4 | Email-based status detection | `StatusChange` rows carried a `Source` enum from day one — detection just writes rows with `EmailSuggestion` / `EmailAutomatic` |
| 5 | Automated prep pipeline | Match results were already append-only and model-stamped, so a loop that scores repeatedly is history, not overwrites |
| 6 | Post-apply workflow + calendar sync | That same `Source` enum generalized to `ChangeSource` and covered interviews unchanged — "did I enter this or did email find it?" was already a question the schema answered |

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
| POST | `/api/applications/{id}/prep` | Queues an automated prep pass; 202 with the `Running` run |
| GET | `/api/applications/{id}/prep` | Latest prep run — poll this for progress |
| GET | `/api/applications/prep-runs` | Latest prep run per application, for the list view |
| PUT | `/api/applications/{id}/documents` | Saves edits to the tailored resume / cover letter |
| POST | `/api/gmail/suggestions/accept` | Applies a scan suggestion, stamping `EmailSuggestion` provenance; schedules the interview too when the email named a time |
| GET | `/api/gmail/pending-suggestions` | Updates waiting for review — reading doesn't clear them |
| POST | `/api/gmail/pending-suggestions/status-updates/dismiss` | Dismisses a suggested status change |
| POST | `/api/gmail/pending-suggestions/new-applications/dismiss` | Dismisses a suggested new application |
| GET | `/api/agenda` | Upcoming interviews and applications that have gone quiet |
| GET/POST | `/api/applications/{id}/interviews` | List / schedule interviews |
| PUT/DELETE | `/api/interviews/{id}` | Reschedule or cancel — the calendar copy follows |
| GET | `/api/interviews/{id}.ics` | The interview as an iCalendar download |
| GET/POST | `/api/applications/{id}/interview-prep` | Stored prep / generate it (`?regenerate=true` forces a fresh pass) |
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

`Prep:TargetScore` (default `80`) is the score a prep pass tries to clear before it declares you good to apply. Raising it makes the loop rewrite more often; each extra pass is two more model calls, and the loop stops early anyway once a pass stops improving.

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

Once connected, Gmail is scanned automatically in the background (not just when you click "Check for updates") — every 6 hours by default. Override with `Gmail:ScanIntervalHours` (set to `0` to disable). Findings from scheduled and manual scans land in the same place and stay there until you accept or dismiss each one — closing the review window never loses an update. That matters because each scan only looks at mail since the last one, so an email is only ever found once.

**What's applied without asking.** Clear-cut emails change the status on their own: an application confirmation for a job you were preparing, an outright rejection, or an explicit phone screen / interview invite (which also schedules the interview when the email names a time). Offers, anything that would move a job backwards, a job still marked Preparing skipping straight past Applied, and anything the model marks as merely *likely* all wait for review. Every automatic change — including applications marked Ghosted after 30 days of silence (`Automation:GhostAfterDays`, `0` to disable) — appears under "Done for you" with an Undo that restores the old status, date, and removes any interview it added.

**What actually gets read.** A scan sends Claude only each candidate email's subject, sender, and the short Gmail snippet — never full bodies. The one exception is interview detection: an email the first pass has already identified as an interview invitation gets opened and its body sent, because the scheduled time appears there and nowhere else. That's a handful of messages per scan, not everything the search matched. The OAuth scope is unchanged (`gmail.readonly` always permitted this), and nothing is persisted beyond the scan that requested it.

**Calendar sync** needs the `https://www.googleapis.com/auth/calendar.events` scope added to your OAuth consent screen in Google Cloud Console. Google won't widen a token that already exists, so an existing connection has to be disconnected and reconnected once — the app detects this and shows a "Reconnect for calendar sync" prompt rather than failing writes with a confusing 403. Career Connect only ever touches events it created; it never reads the rest of your calendar.

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
- **Prep runs are a persisted row, not an HTTP request** — a pass is several chained model calls, well past any sane request timeout, and the user should be able to close the tab. The row *is* the progress: each step is saved as it completes, so polling shows a live log and a restart can close out what it interrupted.
- **The prep loop stops on non-improvement, not just on a pass count** — a rewrite can only reframe experience the resume already has, so once a pass stops helping, more passes only drift further from the original. The 3-pass cap is a backstop, not the usual exit.
- **Tailored resumes live on the application, not in the resume library** — the library holds base versions the user maintains; one throwaway variant per posting would bury them.
- **Preparing → Applied is the one automatic transition.** Everything else Gmail infers stays a suggestion. The asymmetry is deliberate: confirming an application the user already chose to submit is fact-checking, while inferring an interview or rejection is a judgement worth reviewing.
- **Interviews are rows, not a date column on the application** — a search runs in rounds, and a phone screen and the onsite two weeks later are both real. Keeping them as history also means a past interview stays visible after the status has moved on.
- **A model-read interview time is shown editable before it's accepted.** The extractor is told to return null rather than guess when an email doesn't state a timezone — a wrong offset puts a real appointment on someone's calendar at the wrong hour, which is worse than no appointment. The user confirming the time is the last check on that.
- **Calendar sync never throws.** A Google outage must not look like a failure to schedule; the interview is saved either way and the sync is retried on the next edit. `CalendarEnabled` is recorded from the scope Google actually granted, not the one requested, since users can untick it on the consent screen.
- **The Agenda is arithmetic, not a model call** — thresholds over timestamps. It should be free, instant, and identical for identical input; `ICopilotService` remains the AI counterpart for judgement.
- **An imminent unprepped interview is flagged on its card, not as a separate nudge.** The urgency window is narrower than the upcoming window, so a nudge could only ever duplicate a card already on screen.
