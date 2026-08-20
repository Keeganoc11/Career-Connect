# Deployment Readiness Report

Audit only — no application code was changed. This document maps every place the
app currently assumes "one developer, one machine, SQLite, localhost" and what
has to move before it can run as a deployed, containerized service. File paths
are relative to the repo root.

---

## 1. Local-environment assumptions

### 1.1 Connection string / SQLite file location

- [`api/CareerConnect.Api/appsettings.json:9-11`](../api/CareerConnect.Api/appsettings.json) —
  `ConnectionStrings:Default` = `"Data Source=careerconnect.db"`, a relative path.
- [`api/CareerConnect.Api/Data/SqliteConnectionString.cs:17-38`](../api/CareerConnect.Api/Data/SqliteConnectionString.cs) —
  `Resolve()` anchors that relative path to `IHostEnvironment.ContentRootPath`
  (line 34) specifically so the DB file lands next to the project regardless of
  the working directory the process was launched from. This is a deliberate,
  documented local-dev fix (see the class's doc comment) — it's the right
  behavior for a single dev machine, but it also means the SQLite file is
  written inside the container's own filesystem by default. In a container that
  filesystem is ephemeral: every redeploy/restart loses the database unless a
  volume is mounted at the content root, and it cannot be shared across
  replicas. This whole code path goes away once the app talks to a networked
  Postgres instance (§2).
- [`api/CareerConnect.Api/Program.cs:21-22`](../api/CareerConnect.Api/Program.cs) — wires
  `SqliteConnectionString.Resolve(...)` into `UseSqlite(...)`. This is the one
  call site that has to change providers.
- The actual dev database file, `api/CareerConnect.Api/careerconnect.db`, exists
  on disk but is **not tracked in git** — `.gitignore:296-298` excludes
  `*.db`/`*.db-shm`/`*.db-wal`. Good; nothing to clean up there.

### 1.2 CORS origin hardcoded to the Vite dev server

- [`api/CareerConnect.Api/Program.cs:57-60`](../api/CareerConnect.Api/Program.cs) —
  the only CORS policy allows exactly `http://localhost:5173`. Any deployed
  client origin (a real domain, a different port, `https://`) will be rejected
  by the browser. This needs to become configuration-driven (e.g. an
  `AllowedOrigins` list read from config/env) rather than a literal.

### 1.3 Gmail OAuth redirect URI + post-auth client redirect hardcoded

- [`api/CareerConnect.Api/Controllers/GmailController.cs:16-19`](../api/CareerConnect.Api/Controllers/GmailController.cs) —
  ```csharp
  private const string RedirectUri = "http://localhost:5199/api/gmail/callback";
  private const string ClientOrigin = "http://localhost:5173";
  ```
  Both are `const string`, compiled in. `RedirectUri` is sent to Google on the
  authorize call (`Connect()`, line 49) and again on the token exchange
  (`Callback()`, line 80) — it **must** match, byte-for-byte, a URI registered
  in the Google Cloud OAuth client. `ClientOrigin` is where the browser gets
  redirected back to after consent (`RedirectToClient`, lines 90-93). Both need
  to come from configuration once the API and client aren't on localhost. This
  is the crux of §4 below.

### 1.4 Vite dev proxy vs. production build

- [`client/vite.config.ts:6-10`](../client/vite.config.ts) — proxies `/api/*` to
  `http://localhost:5199` **only in the Vite dev server**. This proxy does not
  exist in the built output (`client/dist/`).
- [`client/src/api/client.ts:80,101`](../client/src/api/client.ts) — the client
  calls `fetch(path, ...)` with a **relative** path (`/api/...`), not an
  absolute URL. That's actually deployment-friendly as-is: in production it
  works as long as the built client is served from the same origin as the API
  (e.g. the API serves the static files, or a reverse proxy fronts both under
  one host and maps `/api` to the backend). If client and API end up on
  different origins/domains in the deployed topology, this becomes an absolute
  base URL that has to be injected at build time (`import.meta.env.VITE_API_URL`
  or similar) — currently there is no such env var anywhere in `client/`.
  Confirm which topology is intended before assuming the relative-path
  approach still works.
- User-facing copy that leaks the local port: [`client/src/api/client.ts:85,103`](../client/src/api/client.ts)
  and [`client/src/pages/LoginPage.tsx:31`](../client/src/pages/LoginPage.tsx) all
  say "Is it running on port 5199?" — cosmetic, but wrong once deployed.

### 1.5 Secrets read from user secrets / appsettings

- **JWT signing key** — [`api/CareerConnect.Api/appsettings.Development.json:8-9`](../api/CareerConnect.Api/appsettings.Development.json)
  has a plaintext dev key (`dev-only-signing-key-change-me-...`). `appsettings.json`
  (the base config, loaded in every environment) has **no** `Jwt:Key` at all.
  [`Program.cs:50-52`](../api/CareerConnect.Api/Program.cs) and
  [`Services/TokenService.cs:18-19`](../api/CareerConnect.Api/Services/TokenService.cs)
  both throw `InvalidOperationException` if it's missing — so the app fails
  fast rather than silently signing with nothing, but a production `Jwt:Key`
  has to be supplied some other way (env var `Jwt__Key`, a secrets manager,
  etc.) — there is currently no non-Development config file or env-var wiring
  for it.
- **Seed user credentials** — [`appsettings.Development.json:11-15`](../api/CareerConnect.Api/appsettings.Development.json)
  (`Seed:Email` / `Seed:Password` = `dev@careerconnect.local` / `devpassword1`).
  [`Data/DbSeeder.cs:14-44`](../api/CareerConnect.Api/Data/DbSeeder.cs) runs on
  every startup (`Program.cs:106-111`) and only skips seeding if `Seed:Email`
  is unset or the user already exists (line 20, line 26). If `Seed:Email`/
  `Seed:Password` get carried into a deployed config by accident (e.g. copying
  `appsettings.Development.json` wholesale), a known dev login gets created in
  production. This needs an explicit decision: omit `Seed:*` entirely outside
  Development, or replace single-seeded-user auth with real registration
  before deploying (the README already flags this as a Phase-1 simplification
  — "single seeded user today").
- **Anthropic API key** — read from `Anthropic:ApiKey` config or
  `ANTHROPIC_API_KEY` env var; see §3.
- **Google OAuth client id/secret** — read from `Gmail:ClientId` /
  `Gmail:ClientSecret` config; see §3.
- None of these secrets are committed — `appsettings.Development.json` *is*
  tracked in git (`git ls-files` confirms it), which is fine since it only
  holds the dev-only JWT key and dev seed password, both explicitly called out
  as throwaway values in the README (line 103). Worth double-checking that
  stays true if anyone edits that file later — nothing currently enforces it.

### 1.6 ASP.NET Core Data Protection keys (found during the audit, not in your list — flagging because it affects Gmail specifically)

- [`Program.cs:32`](../api/CareerConnect.Api/Program.cs) — `builder.Services.AddDataProtection();`
  with no persistence configured. This is what encrypts the Gmail refresh
  token before it's stored (`GmailOAuthService.cs:19,123,131` — Data
  Protection purpose `"CareerConnect.GmailRefreshToken.v1"`) and what encrypts
  the OAuth `state` parameter (`GmailController.cs:24`). By default ASP.NET
  Core persists these keys to the local filesystem (or in-memory if it can't
  find a writable, stable location). In a container with no mounted volume for
  the key ring, keys are lost on every restart/redeploy — any row in
  `GmailConnections.EncryptedRefreshToken` written before that becomes
  permanently undecryptable (`GmailOAuthService.cs:184-193` already catches
  and logs that failure, but the user's Gmail connection is silently dead and
  they'd have to reconnect). With more than one replica, each instance
  generates its own key ring unless one is shared, so a token encrypted by
  replica A can't be decrypted by replica B. **I'm not certain what target
  platform this is heading to (single container? multiple replicas?), so I'm
  flagging this rather than prescribing a fix** — the standard options are
  `PersistKeysToFileSystem` on a mounted volume (single instance), or a shared
  store (`PersistKeysToStackExchangeRedis`, a blob-storage provider, etc.) for
  multiple replicas.

---

## 2. Postgres migration: what changes, and what it touches

### 2.1 Provider switch (code)

- [`api/CareerConnect.Api/CareerConnect.Api.csproj:15`](../api/CareerConnect.Api/CareerConnect.Api.csproj) —
  swap `Microsoft.EntityFrameworkCore.Sqlite` for `Npgsql.EntityFrameworkCore.PostgreSQL`
  (`Microsoft.EntityFrameworkCore.Design` stays, it's provider-agnostic).
- [`Program.cs:19-22`](../api/CareerConnect.Api/Program.cs) — replace
  `options.UseSqlite(SqliteConnectionString.Resolve(...))` with
  `options.UseNpgsql(connectionString)`. The comment right above this call
  (lines 19-20) already anticipates this: *"Swapping providers later means
  changing this call and regenerating Data/Migrations against the new
  provider."*
- [`Data/SqliteConnectionString.cs`](../api/CareerConnect.Api/Data/SqliteConnectionString.cs) —
  becomes dead code once Postgres is networked rather than file-based; delete
  it (or repurpose it if there's ever a reason to anchor a relative Postgres
  connection string component, which there isn't).

### 2.2 EF Core migrations (must be fully regenerated, not edited)

All three existing migrations were generated against the SQLite provider and
encode SQLite-specific column types throughout — every column, including ones
you wouldn't expect SQLite to have a native type for, is emitted as
`type: "TEXT"`:

- [`Data/Migrations/20260803171233_InitialCreate.cs:18-65`](../api/CareerConnect.Api/Data/Migrations/20260803171233_InitialCreate.cs) —
  e.g. `Id = table.Column<Guid>(type: "TEXT", ...)` and
  `DateApplied = table.Column<DateOnly>(type: "TEXT", ...)`. Postgres has
  native `uuid` and `date` types — Npgsql's migration generator would emit
  those instead, so these files are not just "run them and see" incompatible,
  they're structurally wrong for Postgres from the first `CREATE TABLE` on.
- [`Data/Migrations/20260805202708_AddResumeAndMatchScoring.cs`](../api/CareerConnect.Api/Data/Migrations/20260805202708_AddResumeAndMatchScoring.cs) and
  [`Data/Migrations/20260806190345_AddGmailConnection.cs`](../api/CareerConnect.Api/Data/Migrations/20260806190345_AddGmailConnection.cs) —
  same issue.
- [`Data/Migrations/AppDbContextModelSnapshot.cs`](../api/CareerConnect.Api/Data/Migrations/AppDbContextModelSnapshot.cs) —
  the snapshot EF uses to compute the *next* migration's diff. It has to be
  regenerated too, or EF will try to diff against a SQLite-shaped snapshot
  while the active provider is Npgsql.

  **The concrete step**: delete `Data/Migrations/` entirely and run
  `dotnet ef migrations add InitialCreate` fresh against `UseNpgsql`, once the
  provider switch above is in place. Don't try to hand-edit the existing
  migration files' `type:` strings — the designer files and snapshot encode
  provider-specific annotations too (they're generated in lockstep with the
  provider selected when `dotnet ef migrations add` ran), and a hybrid
  SQLite/Postgres migration is not a supported EF Core state.

  This also means **the existing SQLite dev database's data does not carry
  over automatically** — there's no SQLite→Postgres data migration path here.
  If any data in `careerconnect.db` needs to survive (unlikely for a dev DB,
  but flagging since I don't know your intent), that's a separate one-time
  export/import step, not something `dotnet ef database update` handles.

### 2.3 Provider-specific code in the DbContext / model (checked, mostly clean)

- [`Data/AppDbContext.cs`](../api/CareerConnect.Api/Data/AppDbContext.cs) — no
  SQLite-specific APIs (no `HasAnnotation("Sqlite:...")`, no raw SQL). The
  `OnModelCreating` configuration (string-valued enums, max lengths, cascade
  vs. restrict delete behavior) is all provider-neutral and needs no changes.
- [`Data/StringListConversion.cs`](../api/CareerConnect.Api/Data/StringListConversion.cs) and
  [`Data/SuggestedEditListConversion.cs`](../api/CareerConnect.Api/Data/SuggestedEditListConversion.cs) —
  both store JSON as a plain string column via `ValueConverter<T, string>`, not
  a SQLite JSON1-extension-specific type. This works unchanged under Postgres
  (stored as `text`); if you want real `jsonb` querying later that's an
  optional enhancement, not a blocker.
- No LINQ queries in `Services/*.cs` use SQLite-only translations (e.g.
  `EF.Functions.Like` with SQLite globbing, `strftime`) as far as this pass
  found — a full query-by-query behavioral diff against Postgres is worth
  doing once the port lands, but nothing jumped out as SQLite-specific syntax.

### 2.4 Tests

- [`api/CareerConnect.Api.Tests/TestDatabase.cs`](../api/CareerConnect.Api.Tests/TestDatabase.cs) —
  README states tests run "on SQLite in-memory (a real relational engine, so
  FK constraints and cascade deletes behave like production)". I did not open
  this file's contents in this pass, but based on that description the test
  suite likely uses `UseSqlite("Data Source=:memory:")` with an open
  connection kept alive for the test's duration — a different mechanism from
  the app's file-based SQLite path, and independent of the app's runtime
  provider. Whether to also switch this to a real (containerized) Postgres for
  integration-level fidelity, or leave it on SQLite in-memory as a fast unit
  test layer, is a judgment call I'd want your input on rather than assume —
  it's a real tradeoff (speed/CI-simplicity vs. provider-fidelity), not a
  correctness requirement, since Postgres and SQLite can diverge subtly (e.g.
  case sensitivity, `DateOnly` handling) in ways that a passing SQLite test
  wouldn't catch.

---

## 3. Where secrets come from today vs. in deployment

| Secret | Read from today | Code location | Deployed environment should use |
|---|---|---|---|
| Claude API key | `Anthropic:ApiKey` config, or `ANTHROPIC_API_KEY` env var (env var checked second) | [`Services/AnthropicSupport.cs:17-21`](../api/CareerConnect.Api/Services/AnthropicSupport.cs) | Same env var / config key works unchanged — just needs to be a real secret injected by the deploy platform (e.g. a secrets manager → env var), not a user-secrets file. No code change required here. |
| Google OAuth client id/secret | `Gmail:ClientId` / `Gmail:ClientSecret` config only (**no env var fallback** — unlike the Anthropic key) | [`Services/GmailOAuthService.cs:37-38`](../api/CareerConnect.Api/Services/GmailOAuthService.cs) | Needs either a config-provider that can inject nested keys as env vars (ASP.NET Core supports `Gmail__ClientId` / `Gmail__ClientSecret` via the standard env-var configuration provider, which is already active by default — this works with no code change), or an explicit env var fallback added to match the Anthropic pattern for consistency. Flagging since it's asymmetric with the Anthropic key today. |
| JWT signing key | `Jwt:Key` config, dev value only in `appsettings.Development.json` | [`Program.cs:50-52`](../api/CareerConnect.Api/Program.cs), [`Services/TokenService.cs:18-19`](../api/CareerConnect.Api/Services/TokenService.cs) | Must be supplied via `Jwt__Key` env var or secrets manager in every non-Development environment — currently nothing supplies it outside Development, so the app will fail to start (by design — it throws) until this is set. |
| Google OAuth *user* refresh tokens (per-user data, not an app secret, but sensitive) | Encrypted at rest with ASP.NET Data Protection, stored in `GmailConnections.EncryptedRefreshToken` | [`Services/GmailOAuthService.cs:123,131,187`](../api/CareerConnect.Api/Services/GmailOAuthService.cs) | Depends on the Data Protection key persistence decision in §1.6 — the encryption mechanism itself doesn't change, but the key ring needs durable/shared storage in the deployed topology. |

Both `IResumeMatchAnalyzer` (Claude) and `IGmailOAuthService`/`IEmailStatusClassifier`
(Google + Claude) already degrade to "feature disabled" (`IsConfigured == false`
→ 503, per `ClaudeResumeMatchAnalyzer.cs:69`, `GmailOAuthService.cs:48`,
`ClaudeEmailStatusClassifier.cs:63`) rather than crashing the app when a key is
absent — so a deploy that's missing one of these three env vars is a degraded
feature, not a broken app, except for `Jwt:Key`, which is load-bearing for
every authenticated request and intentionally hard-fails at startup.

---

## 4. The OAuth redirect URI problem

This is the sharpest edge in the whole audit. Two hardcoded constants in
[`Controllers/GmailController.cs:16-19`](../api/CareerConnect.Api/Controllers/GmailController.cs)
assume the app is always reached at `localhost:5199` (API) and `localhost:5173`
(client):

```csharp
private const string RedirectUri = "http://localhost:5199/api/gmail/callback";
private const string ClientOrigin = "http://localhost:5173";
```

**What breaks, concretely:**

1. **`Connect()` (line 35-51)** sends `RedirectUri` to Google as the
   `redirect_uri` parameter on the authorization URL (`BuildAuthorizationUrl`,
   `GmailOAuthService.cs:56-77`). Google's OAuth server only allows
   `redirect_uri` values that exactly match one of the URIs registered on the
   OAuth client in Google Cloud Console. Once the API is deployed anywhere
   other than `http://localhost:5199`, this call still asks Google to redirect
   to `localhost:5199` — a URI unreachable from the user's browser and almost
   certainly not registered for that OAuth client, so Google will reject the
   authorization request outright (`redirect_uri_mismatch`) before the user
   ever sees a consent screen.

2. **`Callback()` (line 54-88)** passes the *same* `RedirectUri` constant to
   `oauth.ConnectAsync(userId, code, RedirectUri, ...)` →
   `GmailOAuthService.ConnectAsync` → `flow.ExchangeCodeForTokenAsync(...,
   redirectUri, ...)` (`GmailOAuthService.cs:91`). The token exchange step
   requires this to match what was sent in step 1 *and* what's registered with
   Google — three-way consistency. If someone "fixes" only the authorize call
   or only registers a new URI in Google Cloud Console without updating this
   constant too, the two calls disagree and the exchange fails even though the
   authorize step succeeded.

3. **`RedirectToClient()` (line 90-93)** sends the user's browser back to
   `ClientOrigin` (`http://localhost:5173/?gmail=connected` or `/?gmail=error&...`)
   after Google's callback hits the API. Once the client is served from a real
   domain, this hardcoded origin sends the user's browser to `localhost`,
   which won't resolve/won't be running anything for them — the OAuth flow
   would appear to hang or 404 after a successful Google consent.

4. **CORS is a separate, smaller instance of the same problem** —
   [`Program.cs:57-60`](../api/CareerConnect.Api/Program.cs)'s
   `.WithOrigins("http://localhost:5173")` needs to allow the deployed
   client's real origin too, though this only affects normal API calls, not
   the OAuth redirect flow itself (browser redirects aren't subject to CORS).

**What has to change:** both constants need to become environment-driven
(e.g. `Gmail:RedirectUri` / `App:ClientOrigin` read from configuration, mirroring
how `Jwt:Issuer`/`Jwt:Audience` are already read from config rather than
hardcoded). Whatever URI ends up configured for `RedirectUri` **also has to be
separately, manually added** to the OAuth client's "Authorized redirect URIs"
list in Google Cloud Console — that registration lives outside this repo
entirely and nothing here can automate it. I'd also register both the
localhost and production redirect URIs on the same OAuth client (Google
supports multiple), so local dev keeps working after the production one is
added, rather than swapping one for the other.

**Unsure / needs your input:** I don't know the intended deployed topology —
same-origin (API serves the client, or a reverse proxy fronts one domain) vs.
two separate domains for API and client. That decision changes both what
`ClientOrigin` should default to and whether the CORS policy needs to allow a
cross-origin client at all. Flagging rather than guessing.

---

## Summary punch list

Roughly in the order I'd tackle them:

1. Make `Jwt:Key`, `Gmail:ClientId`/`Gmail:ClientSecret`, `Gmail:RedirectUri`,
   client origin, and allowed CORS origins all configuration/env-driven — no
   remaining `const string` localhost values (§1.2, §1.3, §3, §4).
2. Decide the Data Protection key persistence story before any real Gmail
   connections are made in the deployed environment (§1.6) — this one's easy
   to miss because nothing fails at startup; it only breaks quietly later.
3. Decide whether `Seed:*` config ships to non-Development environments at all
   (§1.5) — right now nothing stops it.
4. Switch the EF Core provider (`UseSqlite` → `UseNpgsql`,
   `Microsoft.EntityFrameworkCore.Sqlite` → `Npgsql.EntityFrameworkCore.PostgreSQL`
   package), delete `Data/Migrations/`, regenerate from scratch, delete
   `Data/SqliteConnectionString.cs` (§2.1, §2.2).
5. Register the production Gmail OAuth redirect URI in Google Cloud Console
   once §1 lands (external to the repo, can't be automated from here).
6. Confirm the client/API topology (same-origin vs. cross-origin) so §1.4's
   relative-fetch assumption and §4's CORS/redirect config target the right
   shape.
