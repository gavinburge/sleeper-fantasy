# Tasks

## 1. Application foundation

- [x] 1.1 Create the .NET 10 solution with Domain, Application, Infrastructure, API, Web, and Worker projects; verify `dotnet build` succeeds.
- [x] 1.2 Add PostgreSQL persistence and migrations for manager configuration, provider cache metadata, player identity mappings, analysis jobs, evidence, and results; verify migrations apply to a clean database.
- [x] 1.3 Add ASP.NET Core configuration validation for provider keys, cache policies, runtime URLs, and Identity settings; verify startup fails clearly for missing required secrets.
- [x] 1.4 Add the single-administrator ASP.NET Identity flow with HTTP-only secure cookies, lockout, route protection, antiforgery validation, and secret redaction; verify unauthenticated API requests and state-changing requests without antiforgery tokens are rejected.

## 2. Provider contracts and source adapters

- [x] 2.1 Define C# normalized domain types and provider interfaces for league data, performance data, injury data, and language-model analysis; verify adapters can be substituted in unit tests.
- [x] 2.2 Implement the Sleeper adapter for user, league, roster, settings, matchup, transaction, player, state, and trending endpoints with timeouts, rate limiting, and cache policies; verify fixtures cover successful, empty, rate-limited, and unavailable responses.
- [x] 2.3 Implement nflverse ingestion for schedules, weekly player statistics, rosters, and usage data; verify downloaded fixtures normalize into player and performance records with source timestamps.
- [x] 2.4 Implement the NFL injury-report adapter and parser for injury, practice, and game-status fields; verify fixtures cover status changes, missing fields, and malformed or unavailable pages.
- [x] 2.5 Implement player identity mapping across Sleeper and external identifiers with conservative matching and ambiguity warnings; verify known mappings resolve and ambiguous names are not guessed.
- [x] 2.6 Implement the Gemini language-model adapter with `HttpClientFactory`, server-side key handling, bounded prompts, structured response validation, timeouts, and error mapping; verify invalid or unavailable model responses are rejected without losing deterministic findings.
- [x] 2.7 Document provider configuration, source freshness expectations, and replacement adapter contracts; verify the documented local setup matches the implemented .NET configuration and environment variables.

## 3. League configuration

- [x] 3.1 Implement league discovery from Sleeper username and season, including league/team selection and ambiguous-name handling; verify API tests cover valid, empty, invalid, and ambiguous selections.
- [x] 3.2 Persist and retrieve the selected Sleeper user, league, roster, season, and configuration timestamp without storing Sleeper credentials; verify round-trip persistence and protected access.
- [x] 3.3 Add league configuration API routes and error responses; verify a same-origin Blazor client can configure a league through the API without calling provider endpoints directly.

## 4. Analysis domain and ranking

- [x] 4.1 Implement source refresh and normalized evidence aggregation with freshness, provenance, identity, and partial-failure metadata; verify stale and unavailable signals remain explicit.
- [x] 4.2 Implement deterministic roster-risk analysis using league scoring, roster positions, starters, bye weeks, injury status, practice status, and recent performance; verify focused cases for bye, injury, missing data, and offseason league state.
- [x] 4.3 Implement available-player derivation from all league rosters and position eligibility; verify rostered players, invalid records, and incomplete roster snapshots are handled correctly.
- [x] 4.4 Implement waiver ranking using roster need, replacement level, opportunity signals, matchup/context, and evidence quality; verify ranking changes with scoring settings and identifies evidence conflicts.
- [x] 4.5 Implement structured LLM evidence bundling and result merging so explanations reference evidence IDs, include confidence and caveats, and never replace deterministic facts; verify the deterministic result survives model failure.
- [x] 4.6 Persist completed reviews, evidence manifests, source timestamps, confidence, stale markers, and prior successful results; verify failed refreshes do not overwrite a valid result with an empty success.

## 5. Asynchronous analysis API and worker

- [ ] 5.1 Implement analysis-run state transitions, deduplication, heartbeats, retry policy, and recovery of abandoned executions; verify queued, running, completed, failed, retryable, and stale transitions.
- [ ] 5.2 Implement the .NET Worker entry point for Cloud Run Job execution; verify a fixture run completes, a process interruption is recoverable, and repeated execution is idempotent.
- [ ] 5.3 Implement `POST /api/v1/analysis/run` with authenticated `202 Accepted` responses and active-job reuse; verify repeated requests do not create duplicate work.
- [ ] 5.4 Implement `GET /api/v1/analysis/latest` with status-specific response contracts and completed-result delivery; verify running jobs do not expose incomplete recommendations and failed jobs preserve prior results.
- [ ] 5.5 Implement manual retry and Cloud Run Job execution triggering with the Google Cloud .NET client; verify retry attempts link to the original run and completed results are preserved.
- [ ] 5.6 Add structured execution logging, Cloud Run execution ID correlation, and status reconciliation; verify logs and API status expose stage, provider, attempt, and failure category without secrets.
- [ ] 5.7 Add API contract tests independent of the web UI; verify a second client can start, observe, retry, and read the same job/result representations.

## 6. Web client

- [ ] 6.1 Build the authenticated setup view for Sleeper username, season, league, and roster selection; verify validation and source errors are visible without exposing server credentials.
- [ ] 6.2 Build the analysis view with manual start, polling/status display, stale indicators, retry behavior, and completed review presentation; verify queued/running/failed/completed states render correctly.
- [ ] 6.3 Build roster-risk and waiver recommendation views showing rationale, evidence sources, timestamps, confidence, conflicts, and limitations; verify unavailable signals are not presented as facts.
- [ ] 6.4 Add web-client tests for the primary setup and analysis flows; verify the UI consumes API contracts rather than calling provider endpoints directly.

## 7. Integration and operational verification

- [ ] 7.1 Add Docker Compose PostgreSQL setup, Terraform for a separate Neon project, Artifact Registry, Cloud Run API/web service and Job execution, Secret Manager secrets, Cloud Logging, retry settings, and least-privilege runtime access; verify a deployment plan contains no plaintext credentials.
- [ ] 7.2 Add end-to-end tests using provider fixtures for setup, manual analysis, worker completion, failure fallback, and recommendation display; verify the full flow works without paid services.
- [ ] 7.3 Add rate-limit, timeout, malformed-response, and secret-redaction tests across provider boundaries; verify failures are explicit and retry behavior is bounded.
- [ ] 7.4 Add local and GCP run documentation for starting PostgreSQL, applying migrations, deploying the API, and executing the worker; verify a clean checkout can follow the documented steps.
- [ ] 7.5 Run the project formatter, linter, type-check, unit tests, API tests, and end-to-end tests; verify all required checks pass before implementation is considered complete.
- [ ] 7.6 Add a separate recommendation-quality evaluation suite using sanitized representative league snapshots and recorded model responses; verify risk labels, waiver ordering, evidence grounding, caveats, and deterministic-score authority without making pull-request tests depend on live providers or Gemini.
- [ ] 7.7 Add scheduled provider smoke checks for Sleeper, nflverse, NFL injury reports, and configured model endpoints; verify availability, content types, required fields, representative record counts, source timestamps, parser warnings, and stale/empty responses without logging raw payloads or secrets.

## Workflow follow-up

- Archive the change after implementation and review are complete.
- Verify the archived capabilities and requirements match the delivered behavior.
