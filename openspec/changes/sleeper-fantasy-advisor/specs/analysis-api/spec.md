# Spec Delta

## Purpose

Provides a stable API for starting and observing weekly analysis jobs so the
web interface and future mobile clients can share the same business behavior.

## ADDED Requirements

### Requirement: Analysis can be started manually

The system SHALL expose an authenticated API operation that starts analysis for
the configured league and roster without requiring a scheduled trigger.

#### Scenario: Analysis starts

- **WHEN** an authenticated client requests a weekly analysis and no equivalent
  job is already active
- **THEN** the API returns an analysis job identifier and an accepted status
  without waiting for the analysis to finish

#### Scenario: Analysis is already active

- **WHEN** an authenticated client requests analysis while an equivalent job is
  queued or running
- **THEN** the API returns the existing active job identifier and does not
  create duplicate work

### Requirement: Analysis status is observable

The system SHALL expose the latest analysis job state as queued, running,
completed, failed, or stale, with timestamps and an actionable error when
available.

#### Scenario: Analysis is still running

- **WHEN** a client requests the latest analysis before the job completes
- **THEN** the API reports that analysis is in progress and does not return
  incomplete recommendations as final results

#### Scenario: Analysis has completed

- **WHEN** a client requests the latest analysis after successful completion
- **THEN** the API returns the completed review, recommendations, evidence
  timestamps, and job completion metadata

#### Scenario: Analysis fails

- **WHEN** a job cannot complete because a required source or analysis provider
  fails
- **THEN** the API reports failure, identifies the affected dependency, and
  preserves the previous successful result when one exists

### Requirement: API contracts are client-independent

The system SHALL keep analysis state and result representations independent of
the web UI so another authenticated client can start jobs and retrieve results.

#### Scenario: Future client uses the API

- **WHEN** a supported non-web client calls the documented analysis endpoints
- **THEN** it receives the same job states, result fields, and error semantics as
  the web client

### Requirement: Scheduling is out of scope initially

The initial system SHALL NOT start analysis automatically on a timer, webhook,
or background weekly schedule.

#### Scenario: No manual trigger occurs

- **WHEN** a new NFL week begins without an authenticated analysis request
- **THEN** the system does not create an analysis job

### Requirement: Failed analysis can be retried

The system SHALL retry transient analysis failures automatically within a
bounded policy and SHALL allow an authenticated client to manually retry a
failed analysis.

#### Scenario: Transient execution failure

- **WHEN** the analysis execution exits with a retryable failure
- **THEN** the job execution is retried up to the configured limit and the API
  exposes the current attempt count

#### Scenario: Manual retry

- **WHEN** an authenticated client requests a retry for a failed analysis
- **THEN** the system creates or starts a new execution linked to the prior
  result, without modifying a completed successful result

### Requirement: Analysis execution is observable

The system SHALL record enough execution metadata to determine whether an
analysis is queued, running, completed, failed, or retryable without inspecting
process memory.

#### Scenario: Analysis fails before producing a result

- **WHEN** the worker exits before completing the analysis
- **THEN** the API reports the failure reason category, attempt count, last
  update time, and whether retry is available

#### Scenario: Manager investigates execution

- **WHEN** the manager opens the analysis status or operational logs
- **THEN** the system correlates the analysis ID, execution ID, stage, provider,
  and error details without exposing provider secrets
