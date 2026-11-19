# Spec Delta

## Purpose

Gives the manager a weekly, league-aware view of roster risks and recent
performance so lineup and waiver decisions are based on current evidence.

## ADDED Requirements

### Requirement: Weekly review identifies roster risks

The system SHALL evaluate the selected roster against the current NFL week and
league settings, including player availability, bye weeks, injury status,
practice participation, and recent fantasy performance when available.

#### Scenario: Current roster has actionable risks

- **WHEN** a refresh completes for an in-season league
- **THEN** the review identifies affected rostered players, labels each risk,
  and indicates the data timestamp and source

#### Scenario: No risk signal is available

- **WHEN** a player has no current injury, bye, or performance data from the
  configured sources
- **THEN** the review marks that signal as unavailable instead of treating the
  player as healthy or projecting a score

### Requirement: Review is tailored to the league

The system SHALL use the selected league's scoring settings, roster positions,
starting lineup rules, and current week when evaluating roster needs.

#### Scenario: Scoring settings change the review

- **WHEN** two leagues have different scoring or starting-position settings
- **THEN** the same player data can produce different need labels or priorities
  for each league

#### Scenario: League is not in season

- **WHEN** the selected league is pre-draft or complete
- **THEN** the system explains that weekly in-season analysis is unavailable and
  shows the league state rather than presenting a current-week review

### Requirement: Review refreshes safely

The system SHALL cache successful source responses for a bounded period, limit
upstream request frequency, and retain the last successful review when a
refresh cannot complete.

#### Scenario: Refresh succeeds

- **WHEN** the manager requests a refresh and source data is current enough
- **THEN** the system stores and displays a new review with a visible generated
  timestamp

#### Scenario: Refresh fails after a prior success

- **WHEN** one or more upstream sources fail during refresh
- **THEN** the system keeps the prior review, marks it stale, identifies the
  failed source, and offers a retry

### Requirement: Football intelligence is sourced explicitly

The system SHALL combine Sleeper data with an external football-data provider
for current injury news, depth-chart or role information, and usage or
performance evidence when those signals are not available from Sleeper.

#### Scenario: External evidence is available

- **WHEN** an external provider returns current evidence for a rostered player
- **THEN** the review includes the evidence source, publication or observation
  time, and the player identity mapping used

#### Scenario: External evidence is unavailable

- **WHEN** the provider is unavailable, has no matching player, or returns stale
  evidence
- **THEN** the review identifies the missing signal and does not infer a fact
  from an absent response

### Requirement: LLM analysis is bounded by evidence

The system SHALL send normalized roster, league, player, and external evidence
to a configured external LLM API only for contextual analysis or explanation;
the LLM MUST NOT be treated as the authoritative source of facts.

#### Scenario: LLM returns an analysis

- **WHEN** the LLM receives a complete evidence bundle
- **THEN** the review stores its explanation alongside the underlying evidence,
  confidence, model metadata, and generation timestamp

#### Scenario: LLM provider fails

- **WHEN** the LLM API times out, rejects the request, or returns invalid output
- **THEN** the system reports that contextual analysis is unavailable while
  retaining deterministic source-based findings where possible

### Requirement: External providers are replaceable

The system SHALL isolate Sleeper, nflverse, injury-report, and LLM integrations
behind replaceable provider boundaries and SHALL pass normalized data into the
analysis engine.

#### Scenario: Provider is replaced

- **WHEN** an equivalent provider is configured for player statistics, injury
  information, or LLM explanation
- **THEN** the analysis engine and client-facing API continue to use the same
  normalized fields and result semantics

#### Scenario: Provider response shape changes

- **WHEN** an upstream provider changes its response format
- **THEN** only the affected provider adapter requires change, provided it can
  still produce the normalized contract
