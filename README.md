# Sleeper Fantasy Advisor

Local .NET 10 application for configuring a Sleeper fantasy football league
and preparing weekly roster analysis.

## Prerequisites

- .NET 10 SDK
- Docker Desktop (or another Docker Compose implementation)
- Network access to `api.sleeper.app`

## Start PostgreSQL

From the repository root:

```bash
docker compose up -d postgres
docker compose ps
```

The compose file creates PostgreSQL with these local development defaults:

```text
Host=localhost
Port=5432
Database=sleeper_fantasy
Username=sleeper
Password=sleeper
```

## Run the API

The API applies the checked-in Entity Framework migrations and creates the
development administrator account on startup.

```bash
dotnet run --project src/SleeperFantasy.Api --launch-profile http
```

The API is available at <http://localhost:5142>. Verify it is running:

```bash
curl http://localhost:5142/health
```

Open Scalar at <http://localhost:5142/scalar> to browse and try the generated
OpenAPI contract. The Scalar UI and OpenAPI document are enabled only in the
Development environment.

The default development administrator is:

```text
Email:    admin@example.invalid
Password: LocalDevelopmentPassword1!
```

Change these values before sharing the environment. The API reads them from
`src/SleeperFantasy.Api/appsettings.Development.json` or equivalent
environment variables:

```text
Identity__AdministratorEmail
Identity__AdministratorPassword
```

### Authenticate with Scalar

Scalar requests must be made in this order:

1. Open `GET /api/auth/antiforgery` and execute it. Keep the antiforgery
   cookie that the browser receives and copy the returned `token` value.
2. Open `POST /api/auth/login`, add the header
   `RequestVerificationToken: <token>`, and send:

```json
{
  "email": "admin@example.invalid",
  "password": "LocalDevelopmentPassword1!",
  "rememberMe": false
}
```

The login request uses `email`, not `username`. The antiforgery cookie and
header are required for every state-changing browser request.

### Authenticate with curl

```bash
rm -f cookies.txt

token=$(curl -sS -c cookies.txt \
  http://localhost:5142/api/auth/antiforgery \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')
```

The token and cookie must be used together:

```bash
curl -b cookies.txt -c cookies.txt \
  -H "Content-Type: application/json" \
  -H "RequestVerificationToken: <token>" \
  -d '{"email":"admin@example.invalid","password":"LocalDevelopmentPassword1!","rememberMe":false}' \
  http://localhost:5142/api/auth/login
```

Check that the login cookie is valid:

```bash
curl -b cookies.txt http://localhost:5142/api/auth/session
```

Expected response:

```json
{"authenticated":true}
```

## Configure a Sleeper league

Use the authenticated Scalar session or the same `cookies.txt` cookie jar.
After restarting the API, fetch a new antiforgery token before making any
state-changing request.

### 1. Discover leagues and rosters

In Scalar, execute:

```text
GET /api/v1/league/discovery?username=therealgavb&season=2026
```

The response lists each league and its rosters. Find the league and roster
belonging to the manager. For example, the current development data returns:

```text
League ID: 1389768622208991232
Roster ID: 8
```

With curl:

```bash
curl -b cookies.txt \
  'http://localhost:5142/api/v1/league/discovery?username=therealgavb&season=2026'
```

### 2. Save the selected configuration

In Scalar, first execute `GET /api/auth/antiforgery`, then add its returned
token as the `RequestVerificationToken` header on the configuration request.
Use the following JSON body:

```json
{
  "username": "therealgavb",
  "season": 2026,
  "leagueId": "1389768622208991232",
  "rosterId": "8",
  "teamName": null
}
```

With curl, fetch and use a fresh token in one sequence:

```bash
token=$(curl -sS -b cookies.txt -c cookies.txt \
  http://localhost:5142/api/auth/antiforgery \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')

curl -b cookies.txt -c cookies.txt \
  -H "Content-Type: application/json" \
  -H "RequestVerificationToken: $token" \
  -d '{"username":"therealgavb","season":2026,"leagueId":"1389768622208991232","rosterId":"8","teamName":null}' \
  http://localhost:5142/api/v1/league/configuration
```

### 3. Read the saved configuration

```bash
curl -b cookies.txt http://localhost:5142/api/v1/league
```

Make sure the saved `leagueId` and `rosterId` are the real values returned by
your discovery request. Values such as `league-1`, `roster-1`, or
`fixture-league` belong to automated tests and will produce Sleeper `404`
responses when used against the live API.

## Run an analysis locally

The local API does not start a background worker automatically. In
development, `POST /api/v1/analysis/run` queues one run and a separate Worker
process claims and executes it. Both processes use the same PostgreSQL
database. The default local trigger logs that the run is queued; it does not
contact Cloud Run.

### 1. Queue analysis for the saved team

You do **not** run the analysis twice. The API request below creates the
queued job and returns its ID. It does not execute the analysis itself.
The analysis is scoped to the authenticated application session: the API
looks up that user's saved league and roster configuration. You do not include
a Sleeper username, league ID, or roster ID in this request.

Fetch a fresh antiforgery token before the POST:

```bash
token=$(curl -sS -b cookies.txt -c cookies.txt \
  http://localhost:5142/api/auth/antiforgery \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')

curl -sS -b cookies.txt -c cookies.txt \
  -H "RequestVerificationToken: $token" \
  -X POST \
  http://localhost:5142/api/v1/analysis/run \
  | tee analysis-run.json

run_id=$(python3 -c \
  'import json,sys; print(json.load(open("analysis-run.json"))["id"])')
echo "$run_id"
```

The response is `202 Accepted`. `run_id` is the ID of the queued analysis
created for your saved team.

### 2. Run the Worker for that queued ID

Keep the API running, then use a second terminal. Start the Worker once with
the ID returned by the previous step:

```bash
dotnet run --project src/SleeperFantasy.Worker -- \
  --analysis-run-id "$run_id"
```

The Worker refreshes Sleeper, nflverse, and injury data, computes deterministic
roster risks and waiver rankings, optionally requests Gemini explanations, and
stores the completed review in PostgreSQL. A completed run is not executed
again if the same command is repeated.

### 3. Read the analysis result

```bash
curl -sS -b cookies.txt \
  http://localhost:5142/api/v1/analysis/latest \
  | python3 -m json.tool
```

Poll the same endpoint while the Worker is running. A queued or running
response has `"result": null`; a completed response contains the review under
`result`, including roster risks, waiver recommendations, evidence timestamps,
confidence, stale markers, and limitations. A failed or stale run reports its
error metadata and preserves the prior successful result when one exists.

If you omit `--analysis-run-id`, the Worker claims the oldest queued run and
then remains idle:

```bash
dotnet run --project src/SleeperFantasy.Worker
```

This is useful for repeatedly testing manual runs, but the explicit ID form is
easier to correlate with one request.

If the Worker reports that a run is already claimed, inspect its current state:

```bash
curl -sS -b cookies.txt \
  http://localhost:5142/api/v1/analysis/latest \
  | python3 -m json.tool
```

If the status is `running` and `lastHeartbeatAt` is recent, another Worker is
still executing it; do not start a second copy. If the previous Worker
crashed, the API and Worker recover the run after the 10-minute heartbeat
timeout. Requesting `latest` after that timeout changes it to `retryable` or
`stale`, after which a new run or retry can be started.

### Optional Gemini explanations

Analysis works without Gemini and retains deterministic findings. To enable
server-side contextual explanations, set the key for the Worker process
without committing it:

```bash
export Providers__LanguageModel=Gemini
export Providers__GeminiApiKey='your-server-only-key'
dotnet run --project src/SleeperFantasy.Worker -- \
  --analysis-run-id "$run_id"
```

The API and Worker are separate processes. Export the variables in the same
terminal that starts the Worker; exporting them only for the API terminal does
not configure the Worker. Verify the key is present without printing it:

```bash
test -n "$Providers__GeminiApiKey" && echo "Gemini key is configured"
```

The analysis response reports a provider error code in its caveats if Gemini
cannot run, such as `MISSING_API_KEY`, `MODEL_UNAVAILABLE`, or `TIMEOUT`.

Use the same provider configuration as the API and keep the key out of the
browser, source control, and logs. See [`docs/providers.md`](docs/providers.md)
for freshness and failure behavior.

The API key is associated with a Google AI Studio project. That project must
have Gemini API free-tier access or billing enabled. A key can successfully
list models while still receiving `403 PERMISSION_DENIED` from
`generateContent` when the project is not eligible for the free tier. Verify
generation access with the selected model:

```bash
model="${Providers__GeminiModel#models/}"

curl -sS -w '\nHTTP_STATUS:%{http_code}\n' \
  -H "x-goog-api-key: $Providers__GeminiApiKey" \
  -H "Content-Type: application/json" \
  -d '{"contents":[{"parts":[{"text":"Reply with OK"}]}]}' \
  "https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent"
```

If the response says that the free tier is unavailable for the project,
enable billing at <https://aistudio.google.com/billing> or create the key
under a project with free-tier access. Otherwise, deterministic analysis still
runs and the response identifies that Gemini contextual analysis is
unavailable.

### Retry a failed run

Retry only after inspecting `errorCode`, `errorMessage`, and the affected
stage/provider:

```bash
token=$(curl -sS -b cookies.txt -c cookies.txt \
  http://localhost:5142/api/auth/antiforgery \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')

curl -sS -b cookies.txt -c cookies.txt \
  -H "RequestVerificationToken: $token" \
  -X POST \
  "http://localhost:5142/api/v1/analysis/$run_id/retry" \
  | tee retry-run.json

retry_id=$(python3 -c \
  'import json; print(json.load(open("retry-run.json"))["id"])')
dotnet run --project src/SleeperFantasy.Worker -- \
  --analysis-run-id "$retry_id"
```

## Run the web project

In a second terminal:

```bash
dotnet run --project src/SleeperFantasy.Web --urls http://localhost:5187
```

The league setup UI is available at <http://localhost:5187/setup>. The web
project uses `ApiBaseUrl` from
`src/SleeperFantasy.Web/appsettings.Development.json` to call the local API;
keep the API running on port 5142.

After signing in with the development administrator account, enter a Sleeper
username and season, select the league and manager-owned roster, and save the
configuration. Provider credentials remain server-side and are never entered
in the web UI.

## API endpoint reference

Open <http://localhost:5142/scalar> for the interactive OpenAPI reference.
The main endpoints are:

```text
GET  /health                              API health check
GET  /api/auth/antiforgery                 Create antiforgery cookie/token pair
POST /api/auth/login                       Authenticate and issue session cookie
GET  /api/auth/session                     Check authentication status
POST /api/auth/logout                      Clear the session cookie
GET  /api/v1/league/discovery               Discover Sleeper leagues and rosters
GET  /api/v1/league                        Read saved league configuration
POST /api/v1/league/configuration           Save selected league and roster
POST /api/v1/analysis/run                   Queue an analysis for the saved team
GET  /api/v1/analysis/latest               Read analysis status and latest result
POST /api/v1/analysis/{runId}/retry         Retry a failed or stale analysis
```

All `/api/v1/league` and `/api/v1/analysis` routes require authentication. POST
requests require the antiforgery cookie and `RequestVerificationToken` header.
Provider calls remain server-side; clients never call Sleeper directly.

## Run tests and build

The test suite includes an API end-to-end journey backed by a disposable
PostgreSQL 17 Testcontainers instance, so Docker must be running.

```bash
dotnet test tests/SleeperFantasy.ProviderTests/SleeperFantasy.ProviderTests.csproj
dotnet build SleeperFantasy.slnx
```

The API end-to-end tests use a disposable PostgreSQL 17 Testcontainers
database. To exercise the real local pipeline, use the analysis steps above
with a configured Sleeper username, season, league, and roster. The default
`LanguageModel=None` setting keeps that run offline from Gemini while still
producing deterministic roster-risk and waiver output.

## Optional providers

The local configuration disables Gemini by default:

```json
"Providers": {
  "LanguageModel": "None"
}
```

To enable it, set `Providers__LanguageModel=Gemini` and provide the
server-side `Providers__GeminiApiKey`. Never expose provider keys to the web
client or commit them to the repository. See
[`docs/providers.md`](docs/providers.md) for provider URLs, freshness, and
adapter configuration.

## Stop local services

```bash
docker compose down
```

Add `-v` only when you intentionally want to delete the local PostgreSQL
volume and all stored development data.
