# Provider configuration

The API and worker keep provider credentials and upstream calls server-side. The
web client never receives these values.

## Local configuration

Start PostgreSQL with the repository's local compose setup, then configure the
API and worker with the same `ConnectionStrings:DefaultConnection` value.
`src/SleeperFantasy.Api/appsettings.Development.json` contains the non-secret
local defaults. Override values with environment variables when needed:

```text
Providers__SleeperBaseUrl=https://api.sleeper.app
Providers__NflverseBaseUrl=https://github.com/nflverse/nflverse-data/releases/download
Providers__InjuryReportBaseUrl=https://www.nfl.com/injuries
Providers__LanguageModel=None
Providers__GeminiApiKey=<server-only-key>
Providers__GeminiModel=gemini-2.0-flash
Providers__GeminiBaseUrl=https://generativelanguage.googleapis.com/v1beta
Providers__GeminiResultTtlMinutes=30
Providers__LanguageModelTimeoutSeconds=30
Providers__LanguageModelMaxPromptCharacters=24000
Cache__DefaultTtlHours=24
Cache__SleeperTtlHours=24
Cache__NflverseTtlHours=24
Cache__InjuryReportTtlHours=24
Cache__MaxEntriesPerProvider=100
```

Set `Providers__LanguageModel=Gemini` only when
`Providers__GeminiApiKey` is present. Required URL, cache, database, runtime,
and Identity settings are validated at startup.

## Freshness and failure behavior

Successful provider responses are cached for the configured source-specific TTL,
subject to the per-provider entry limit. `Cache__DefaultTtlHours` remains the
backward-compatible fallback when a source-specific TTL is omitted. nflverse
publishes rolling release assets under stable tags such as
`schedules`, `stats_player`, and `weekly_rosters`; the GitHub release
publication date is not the data freshness timestamp. For a resource requested
under a release tag, the adapter requests the matching tag metadata, for
example:
`https://github.com/nflverse/nflverse-data/releases/download/stats_player/timestamp.json`
for `stats_player/stats_player_week_2026.csv` (and
`weekly_rosters/timestamp.json` for the weekly roster asset). Consumers should
use that release-specific `timestamp.json` when deciding whether the data is
current. Sleeper requests are serialized and spaced to avoid burst
traffic. A provider result reports `Available`, `Empty`,
`RateLimited`, `Unavailable`, or `Invalid`; callers must preserve that state
and must not turn an unavailable signal into a fact.

The initial freshness expectations are:

| Source | Default use | Freshness |
| --- | --- | --- |
| Sleeper | League, roster, settings, matchup, transaction, player, state, trending data | 24 hours |
| nflverse | Schedules, weekly stats, rosters, and usage | 24 hours; validate the source `timestamp.json` |
| NFL injury report | Current injury, practice, and game status | 24 hours |
| Gemini | Explanations and conflict summaries only | 30 minutes |

## Replacement adapter contracts

Implement `ILeagueDataProvider`, `IPerformanceDataProvider`,
`IInjuryDataProvider`, or `ILanguageModelProvider` from
`SleeperFantasy.Domain.Providers`. Adapters return normalized records wrapped
in `ProviderResult<T>`, including provider name, observation time, freshness,
availability, and an actionable error code/message. The analysis layer should
depend only on those interfaces and normalized records.

Use the existing typed `HttpClient` registrations as the replacement boundary.
Adapters must enforce bounded timeouts, validate upstream payloads, avoid
logging credentials or raw sensitive payloads, and report ambiguity rather than
guessing player identity. A language-model adapter must validate evidence IDs
and retain deterministic findings when model output is unavailable.
