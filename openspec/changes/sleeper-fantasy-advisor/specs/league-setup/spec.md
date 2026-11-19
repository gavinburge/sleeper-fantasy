# Spec Delta

## Purpose

Lets a manager securely connect the app to a Sleeper NFL league and select the
specific roster that weekly analysis will evaluate.

## ADDED Requirements

### Requirement: Manager can configure a Sleeper league

The system SHALL let the manager enter a Sleeper username and season, resolve
the user's leagues, and select one league and one team roster from the results.

#### Scenario: League selection succeeds

- **WHEN** a valid Sleeper username and season return one or more leagues
- **THEN** the system shows league names, seasons, statuses, and team names
  associated with those leagues and allows one team to be selected

#### Scenario: No matching league is found

- **WHEN** the username is valid but no league matches the requested season
- **THEN** the system explains that no selectable league was found and does not
  save an incomplete configuration

### Requirement: Configuration is scoped and protected

The system SHALL require an authenticated application session before exposing
the manager dashboard and SHALL store only the identifiers and settings needed
to refresh the selected league.

#### Scenario: Unauthenticated dashboard request

- **WHEN** a request without a valid app session accesses the dashboard or
  configuration data
- **THEN** the system rejects the request and does not disclose league or roster
  data

#### Scenario: Configuration is saved

- **WHEN** the manager selects a league and roster
- **THEN** the system persists the Sleeper user ID, league ID, roster ID, season,
  and configuration timestamp without storing a Sleeper password or API token

### Requirement: Manager can authenticate securely

The system SHALL provide a protected sign-in flow using a server-side identity
account and SHALL issue an HTTP-only secure session cookie after successful
authentication.

#### Scenario: Sign-in succeeds

- **WHEN** the manager submits valid credentials over HTTPS
- **THEN** the system creates an authenticated session and redirects to the
  protected dashboard

#### Scenario: Sign-in fails

- **WHEN** the manager submits invalid credentials
- **THEN** the system rejects the request without revealing whether the account
  or password was incorrect and applies the configured lockout policy

#### Scenario: State-changing browser request lacks antiforgery validation

- **WHEN** a browser submits a configuration, refresh, or retry request without
  a valid antiforgery token
- **THEN** the system rejects the request without changing application state

### Requirement: Setup handles Sleeper failures explicitly

The system SHALL distinguish invalid input, unavailable Sleeper responses, rate
limits, and stale or ambiguous team matches.

#### Scenario: Sleeper is unavailable

- **WHEN** a Sleeper lookup fails or times out
- **THEN** the system shows a retryable error and does not replace a previously
  valid configuration with partial data

#### Scenario: Team names are ambiguous

- **WHEN** multiple rosters match the entered team name
- **THEN** the system requires the manager to choose using additional displayed
  context rather than guessing
