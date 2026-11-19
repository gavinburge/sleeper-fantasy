# Proposal

## Why

Managing a fantasy football roster requires repeatedly combining league roster data, player availability, weekly results, bye weeks, and injury/news signals. Sleeper exposes the core league data through a read-only API, but it does not turn that data into a focused, explainable weekly waiver workflow for a manager.

## What Changes

- Add a small web application for one manager to configure a Sleeper NFL league and identify their roster.
- Expose all business operations through an API so the web UI is one client and a future mobile app can reuse the same contracts.
- Resolve a Sleeper user, list their leagues for a selected season, and let the manager select a league and team.
- Fetch and cache the selected league's settings, users, rosters, matchups, transactions, NFL state, and player metadata through a server-side Sleeper integration.
- Let the manager manually start a weekly analysis; the initial release SHALL NOT run scheduled analyses.
- Run analysis as a long-lived job and expose its queued, running, completed, failed, and stale states through the API.
- Produce a weekly roster review covering recent performance, upcoming bye weeks, injury status, practice participation, and missing or uncertain starters.
- Identify players currently available in the league and rank waiver targets against roster needs and league scoring/roster settings.
- Enrich analysis with football intelligence from an external data provider for injuries, news, depth charts, usage, and recent performance.
- Send normalized evidence to a configurable external LLM API for explanation and contextual analysis, while keeping source data, confidence, and limitations visible.
- Start with free sources where practical: Sleeper, nflverse, NFL.com injury reports, and a free-tier Gemini API; keep each source replaceable.
- Show an explainable recommendation for each target, including supporting signals, confidence, and relevant caveats; never submit waiver claims or modify Sleeper data.
- Protect the personal dashboard with an ASP.NET Identity account and secure HTTP-only session cookie; keep Sleeper/API calls on the server.
- Handle stale, unavailable, or incomplete data explicitly rather than presenting unsupported certainty.

## Capabilities

### New Capabilities

- `league-setup`: Securely configure and persist a manager's selected Sleeper user, league, season, and roster.
- `analysis-api`: Provide API-first, asynchronous, manually triggered analysis jobs reusable by web and future mobile clients.
- `weekly-roster-analysis`: Build an explainable weekly roster-health review from roster, schedule, performance, bye, injury, and availability data.
- `waiver-recommendations`: Rank available players as potential waiver targets using league-specific needs and show the evidence behind each recommendation.

### Modified Capabilities

<!-- None: this project has no existing capabilities. -->

## Impact

- New web UI, API routes, persistence, asynchronous job execution, and weekly analysis logic.
- Implement the application in .NET 10 with C# and ASP.NET Core, following the
  solution separation and PostgreSQL approach used by `brody-speak`.
- GCP hosting should follow the existing `brody-speak` pattern: Cloud Run, Artifact Registry, Secret Manager, and a separate small Neon PostgreSQL project.
- Sleeper's read-only API is the source for league/roster/player metadata. It requires no API token, but calls must be rate-limited and cached.
- Sleeper's player metadata can expose injury status and practice participation, while reliable current performance/news data requires an additional provider or a clearly marked unavailable-data state; the first implementation must not silently invent those signals.
- Football intelligence and LLM providers are external dependencies and must be replaceable behind application interfaces. The app must not depend on a personal GitHub Copilot IDE subscription as its production analysis API credential.
- Analysis rules SHALL consume normalized internal data rather than provider-specific response shapes, so data sources and LLM vendors can be swapped without changing the web or mobile API.
- No Sleeper write operations are in scope. Recommendations are advisory only.
- Personal configuration and session credentials must not be exposed to the browser, logs, or client-side source.
