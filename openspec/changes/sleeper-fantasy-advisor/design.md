# Design

## Context

See `proposal.md` for the motivation and scope. The project is starting empty, with one personal manager, a read-only Sleeper
integration, free external data sources where practical, GCP as the hosting
target, and a future requirement for mobile clients to reuse the business API.

The analysis combines data with different freshness, identity systems, and
reliability characteristics. It also includes an asynchronous job and external
LLM call, so the web request must not own the analysis lifecycle.

## Goals / Non-Goals

**Goals:**

- Keep domain logic independent of provider SDKs, response formats, and the web
  UI.
- Provide a persisted analysis-job lifecycle that survives a page refresh and
  can later be invoked by a scheduler or mobile client.
- Make source freshness, provenance, uncertainty, and partial failure visible.
- Support free initial adapters for Sleeper, nflverse, NFL.com injury reports,
  and Gemini.
- Keep all credentials and upstream calls server-side.

**Non-Goals:**

- Automatic scheduling, notifications, lineup changes, waiver submission, or
  other Sleeper write operations.
- A multi-user account system or paid data-provider integration in the first
  release.
- Treating an LLM as the source of player facts or as a guaranteed projection
  engine.

## Decisions

### API-first application boundary

Use a .NET 10/C# solution with ASP.NET Core versioned API routes and a thin
Blazor web client. Separate Domain, Application, Infrastructure, API, Web, and
Worker projects follow the separation used by `brody-speak`. Domain services
return serializable DTOs that do not contain provider-specific objects. The
initial API surface is:

```text
POST /api/v1/analysis/run
GET  /api/v1/analysis/latest
GET  /api/v1/league
POST /api/v1/league/configuration
```

The run endpoint returns `202 Accepted` and a job identifier. The latest
endpoint returns the job state and only exposes a final result when the job is
complete. This keeps future mobile clients independent of the web rendering
implementation.

The Blazor web client and API are initially served from the same origin. The
browser authenticates with an ASP.NET Core Identity cookie, so protected API
requests do not require a separate browser token or CORS configuration. The
Blazor client calls the API contracts rather than invoking provider adapters or
application handlers directly.

### Persisted PostgreSQL state

Use PostgreSQL for local and hosted environments. Persist configuration,
normalized player identity mappings, source snapshots/cache metadata, analysis
jobs, evidence records, and completed results. PostgreSQL matches the existing
`brody-speak` operational experience, supports concurrent API/worker access,
and avoids maintaining separate SQLite behavior for GCP.

Run PostgreSQL 17 in Docker Compose locally. Use a separate Neon project for
this application rather than sharing the `brody-speak` database or schema.
Separate credentials, migrations, backups, and lifecycle make the applications
independently deployable while the small personal workload remains suitable for
Neon's free plan.

Use Entity Framework Core 10 with the Npgsql provider and keep migrations in
source control. Use explicit freshness and source timestamps rather than
treating cache presence as freshness. Keep raw provider payloads out of normal
API responses; retain only what is needed for reproducibility and debugging.

### Database-backed Cloud Run Job execution

`POST /analysis/run` creates or reuses a deduplicated job in PostgreSQL, then
starts a Cloud Run Job execution with the analysis ID as an argument. The
execution runs the .NET Worker entry point, transitions the record through
`queued -> running -> completed|failed`, and records retryable versus terminal
failures. The API never waits for upstream calls or for the job to complete.

For local development, the worker runs as a second .NET process against the
same Docker PostgreSQL database. In GCP, deploy the API and worker entry point
from the same container image, and execute the worker through a Cloud Run Job.
The job has one task initially, a bounded retry count, and a timeout suitable
for the analysis. No service is kept running between manual analyses.

The database record is the application source of truth for API status. Cloud
Run execution IDs are stored with each attempt. The API can reconcile an
interrupted execution from its last heartbeat and Cloud Run execution metadata.
Manual retry creates a new attempt linked to the prior run and never overwrites
a completed successful result.

### Execution observability

Emit structured logs containing the analysis ID, Cloud Run execution ID, stage,
provider, attempt, duration, and error category. Cloud Run sends these logs to
Cloud Logging and exposes execution history, task failures, retries, and
container exit status in the Cloud Run Jobs console. Persist the same
high-level state and failure category in PostgreSQL so the web UI can explain
what happened without requiring console access.

Use bounded automatic retries for transient failures. Permanent validation,
authentication, or data-quality failures should stop without retrying. A
manual retry endpoint is available for the manager after correcting
configuration or when a source becomes available.

### GCP deployment boundary

Follow the proven `brody-speak` infrastructure shape: Artifact Registry stores
the container images, Cloud Run hosts the API and Blazor web service, Secret
Manager stores the Neon connection string and provider keys, and dedicated
runtime service accounts access those secrets. Terraform owns the resources
and least-privilege bindings. The API service account can execute the
Cloud Run Job; the job service account can read secrets and write logs. The
worker execution receives only a job identifier and reads the remaining
configuration from Secret Manager and PostgreSQL.

### Provider adapter boundaries

Define C# interfaces for:

- `LeagueDataProvider`: Sleeper users, leagues, rosters, settings, matchups,
  transactions, player metadata, and trending data.
- `PerformanceDataProvider`: nflverse schedules, weekly stats, play-by-play
  aggregates, rosters, and usage data.
- `InjuryDataProvider`: NFL injury/practice reports and normalized player
  statuses.
- `LanguageModelProvider`: structured contextual analysis and explanation.

Initial adapters are Sleeper, nflverse, NFL.com injury reports, and Gemini.
Each adapter maps into internal records containing canonical player identity,
observation time, source, confidence, and fields relevant to analysis. The
analysis engine never imports an adapter's response types.

### Player identity normalization

Maintain a mapping between Sleeper player IDs and external identifiers such as
nflverse/GSIS IDs. Resolve using stable IDs first, then conservative
name/team/position matching. Unresolved or ambiguous matches are retained as
data-quality warnings and excluded from claims that require identity certainty.

### Analysis pipeline

The worker executes these stages:

1. Load the saved league configuration and current NFL state.
2. Refresh eligible source data using provider-specific cache policies.
3. Normalize player identities and produce a source/evidence manifest.
4. Build the roster-risk view using league scoring, roster positions, starters,
   bye weeks, injury status, and recent usage/performance.
5. Build the available-player pool from all league rosters.
6. Rank waiver candidates deterministically using roster need, replacement
   level, opportunity signals, matchup/context, and evidence quality.
7. Send a compact, normalized evidence bundle to the language-model provider
   for explanation and conflict summarization.
8. Validate the LLM response against a structured schema, attach it to the
   deterministic result, and persist the final review.

If the LLM fails, the deterministic result remains available with contextual
explanation marked unavailable. If a required source fails, the job reports
partial or failed status according to whether the affected result can be
truthfully produced.

### Evidence and LLM contract

The LLM receives facts, not an instruction to browse arbitrarily. Each fact
includes player ID, value, source, observed/published timestamp, and freshness.
The response schema contains explanations, confidence adjustments, conflicts,
and caveats keyed to evidence IDs. Unknown or unsupported claims are rejected
or marked unavailable before persistence.

The Gemini adapter reads its API key from server-side environment configuration.
The interface allows a later provider to implement the same structured request
and response contract.

### Authentication, authorization, and failure handling

Use ASP.NET Core Identity with a single administrator account for the initial
personal deployment. Protect API routes with authorization and issue an
HTTP-only, secure-in-production cookie with an appropriate SameSite policy.
Apply ASP.NET Core antiforgery validation to browser state-changing requests,
including configuration, analysis start, and retry operations. Configure
password hashing, lockout, session expiry, and HTTPS redirects using the same
patterns as `brody-speak`.

Do not add bearer-token authentication for the first release. A future mobile
client can introduce a separate token flow without weakening the browser
cookie boundary. Keep provider keys server-side, redact secrets and raw news
content from logs, validate all external responses, and apply timeouts, bounded
retries, and upstream rate limits.

Analysis results include source timestamps and stale indicators. A failed
refresh never replaces the last successful result with an empty success-shaped
response.

## Risks / Trade-offs

- **[Free sources can be incomplete or change format]** -> Isolate adapters,
  validate payloads, record freshness, and show unavailable signals explicitly.
- **[NFL.com parsing may break]** -> Keep the injury adapter narrow, add contract
  fixtures, and allow the analysis to continue with Sleeper data plus warnings.
- **[nflverse data is not always real-time]** -> Use it primarily for completed
  game usage and performance; keep current injury/practice status in a separate
  source with independent freshness.
- **[LLM output can hallucinate or be unavailable]** -> Use deterministic
  ranking as authoritative, require structured output, validate evidence
  references, and retain results when the LLM fails.
- **[Neon scale-to-zero or network interruptions delay requests]** -> Use pooled
  connections, bounded timeouts, retry transient connection failures, and
  expose job state rather than keeping API requests open.
- **[A Cloud Run Job can be interrupted]** -> Persist stage/status updates and
  heartbeats, configure bounded job retries, make stages idempotent, and mark
  abandoned executions recoverable.
- **[Provider identifiers may not match]** -> Store explicit mappings and
  surface ambiguous matches instead of guessing.

## Migration Plan

1. Create the PostgreSQL schema, migrations, and API/domain modules.
2. Implement the Sleeper, nflverse, injury-report, and Gemini adapters behind
   the provider ports.
3. Run the API and worker locally with Docker PostgreSQL and the single-user
   session configuration.
4. Backfill no external historical state; source caches and results are created
   on demand.
5. Provision a separate Neon project and GCP secrets, deploy the API and Blazor
   web service to Cloud Run, and execute the .NET Worker through a Cloud Run
   Job with Cloud Logging and bounded retries.
6. To roll back, stop worker executions and disable analysis routes; existing
   configuration and prior results remain read-only.
7. A later scheduler or mobile client calls the existing analysis API and does
   not require changes to the analysis engine.
