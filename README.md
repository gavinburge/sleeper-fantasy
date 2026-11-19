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

## Run the web project

In a second terminal:

```bash
dotnet run --project src/SleeperFantasy.Web --urls http://localhost:5187
```

The web project currently contains the Blazor shell and starter home page. The
league setup UI is not wired yet; use the API directly or continue with the
remaining web tasks.

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
```

All `/api/v1/league` routes require authentication. POST requests require the
antiforgery cookie and `RequestVerificationToken` header. Provider calls remain
server-side; clients never call Sleeper directly.

## Run tests and build

The test suite includes an API end-to-end journey backed by a disposable
PostgreSQL 17 Testcontainers instance, so Docker must be running.

```bash
dotnet test tests/SleeperFantasy.ProviderTests/SleeperFantasy.ProviderTests.csproj
dotnet build SleeperFantasy.slnx
```

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
