# Spec Delta

## Purpose

Ranks players available in the selected league as advisory waiver targets and
makes the reasoning, uncertainty, and roster fit visible to the manager.

## ADDED Requirements

### Requirement: Available players are limited to the league pool

The system SHALL derive available players from the selected league's current
rosters and SHALL exclude players already rostered, invalid player records, and
players who are not eligible for the league's supported positions.

#### Scenario: Available player pool is built

- **WHEN** league rosters and player metadata are successfully refreshed
- **THEN** the system lists only players not present on any league roster and
  records when the availability snapshot was taken

#### Scenario: Availability cannot be verified

- **WHEN** one or more rosters cannot be refreshed
- **THEN** the system does not present a complete waiver ranking and clearly
  labels the results as unavailable or incomplete

### Requirement: Waiver targets are ranked by explainable need

The system SHALL rank available players using league settings, roster gaps,
bye-week coverage, injury or role risk, recent performance when available, and
relevant transaction context.

#### Scenario: Target addresses a roster weakness

- **WHEN** an available player fills a position affected by a starter's bye or
  injury risk
- **THEN** the recommendation explains the roster gap, the player's fit, and the
  evidence contributing to the rank

#### Scenario: Evidence is incomplete

- **WHEN** performance, injury, or role data is missing or stale
- **THEN** the recommendation lowers or omits confidence, identifies the missing
  evidence, and does not fabricate a projection

### Requirement: Recommendations are advisory only

The system SHALL show a ranked recommendation, rationale, confidence, and
limitations without submitting waiver claims, trades, lineup changes, or any
other write operation to Sleeper.

#### Scenario: Manager reviews a recommendation

- **WHEN** the manager opens a waiver target
- **THEN** the system shows the suggested priority and supporting signals and
  provides no action that silently changes the Sleeper league

#### Scenario: Manager has a stale recommendation

- **WHEN** the underlying roster, injury, or availability data is older than the
  configured freshness window
- **THEN** the system prominently labels the recommendation stale and offers a
  refresh before treating it as current

### Requirement: Increased-opportunity candidates are evidence-based

The system SHALL identify potential replacement players from team role,
depth-chart, usage, injury, and news evidence and SHALL describe them as
opportunity candidates rather than guaranteed outcomes.

#### Scenario: Starter injury creates a candidate

- **WHEN** a relevant player is expected to miss time and an available teammate
  has supporting role or usage evidence
- **THEN** the recommendation explains the expected opportunity, lists the
  supporting evidence, and assigns a confidence level

#### Scenario: Opportunity evidence conflicts

- **WHEN** depth-chart, usage, injury, or news signals disagree
- **THEN** the recommendation surfaces the conflict, lowers confidence, and
  avoids presenting one candidate as certain
