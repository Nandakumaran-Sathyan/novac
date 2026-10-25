# FinSight AI — Intelligent Loan Operations Copilot

An independent, educational fintech prototype: a loan-operations dashboard that helps staff review
**synthetic** loan applications. It combines deterministic lending rules (EMI, debt-to-income,
product limits) with, in later phases, AI-assisted analysis that **never approves or rejects a loan**.

> All data is synthetic. This project is not affiliated with, and does not reproduce, any
> employer's internal systems or proprietary implementation.

**Status:** Days 1–3 complete (project setup, SQL Server data layer, deterministic assessment).
See [docs/ROADMAP.md](docs/ROADMAP.md) for Days 4–10 and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
for design decisions.

---

## Tech stack

| Layer     | Technology |
|-----------|------------|
| Backend   | C# / ASP.NET Core Web API on **.NET 10 (LTS)** |
| Data      | Microsoft **SQL Server 2022** via Entity Framework Core 10 |
| Frontend  | React 19 + Vite (JavaScript) |
| API docs  | OpenAPI document + Swagger UI |
| Tests     | xUnit |

## Prerequisites

| Tool | Version | Check |
|------|---------|-------|
| .NET SDK | 10.0.x | `dotnet --list-sdks` |
| Node.js | 20.19+ or 22.12+ | `node --version` |
| Docker-compatible runtime (Docker Desktop, Colima or OrbStack) | any recent | `docker --version` |

**Apple Silicon:** the SQL Server image is amd64-only and runs under Rosetta emulation.
In Docker Desktop, enable *Settings → General → Use Rosetta for x86_64/amd64 emulation*.
With Colima, start it with `colima start --vm-type vz --vz-rosetta --memory 4`.
SQL Server needs about 2 GB RAM and about 3 GB of disk for the image.

If you installed .NET with `dotnet-install.sh` (into `~/.dotnet`), add this to `~/.zshrc`:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
```

## First-time setup

### 1. Start SQL Server

```bash
cp .env.example .env            # then edit .env and set a strong MSSQL_SA_PASSWORD
docker compose up -d
docker compose ps               # wait until STATUS shows "healthy" (~30–60 s)
```

### 2. Give the API its connection string (kept out of source control)

```bash
cd backend
dotnet user-secrets set "ConnectionStrings:FinSightDb" \
  "Server=localhost,1433;Database=FinSightAI;User Id=sa;Password=<same as .env>;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5" \
  --project FinSight.Api > /dev/null
```

User secrets are stored in `~/.microsoft/usersecrets/`, outside the repository. Outside
Development, use the environment variable `ConnectionStrings__FinSightDb` instead.
`TrustServerCertificate=True` is acceptable only for this local container, which uses a self-signed certificate.

### 3. Create the database, apply migrations and seed synthetic data

```bash
cd backend
dotnet tool restore             # installs the pinned dotnet-ef version from dotnet-tools.json
dotnet ef database update --project FinSight.Infrastructure --startup-project FinSight.Api
```

This applies the `InitialCreate` migration, then runs the idempotent seeder (17 synthetic applications).

## Running

```bash
# Terminal 1: API on http://localhost:5080 (Swagger UI at /swagger)
cd backend && dotnet run --project FinSight.Api

# Terminal 2: frontend on http://localhost:5173
cd frontend && npm install && npm run dev
```

Quick checks:

```bash
curl http://localhost:5080/health                    # "Healthy" when SQL Server is reachable
curl "http://localhost:5080/api/applications?pageSize=3"
```

## Tests

```bash
cd backend && dotnet test
```

Unit tests cover the EMI formula (reference values computed independently), edge cases
(0% interest, 1-month tenure, rounding midpoints, extreme bounds, invalid input), every
assessment rule, DTO validation and flag serialization. They need no database.

## API (current)

| Method | Route | Purpose |
|--------|-------|---------|
| GET  | `/api/applications?search=&status=&page=1&pageSize=20` | Paged list, newest first, with latest review-flag counts |
| GET  | `/api/applications/{id}` | Detail plus latest assessment |
| POST | `/api/applications` | Create (server sets `Status = Submitted`) |
| POST | `/api/applications/{id}/assess` | Run deterministic rules and store a `RuleAssessment` |
| GET  | `/health` | Liveness plus SQL Server connectivity |

Errors use RFC 7807 ProblemDetails: `400` validation (with field errors), `404` not found,
`503` database unavailable, `500` unexpected (details only in server logs).
Sample requests: [backend/FinSight.Api/FinSight.Api.http](backend/FinSight.Api/FinSight.Api.http).

## Configuration

Product limits live in `backend/FinSight.Api/appsettings.json` under `LendingProductRules`
and are validated at startup (for example, min must not exceed max):

| Setting | Default | Meaning |
|---------|---------|---------|
| `MinLoanAmount` / `MaxLoanAmount` | 50,000 / 2,500,000 | INR |
| `MinTenureMonths` / `MaxTenureMonths` | 12 / 84 | months |
| `MinAnnualInterestRate` / `MaxAnnualInterestRate` | 9.5 / 24 | % p.a. |
| `MinMonthlyIncome` | 20,000 | INR |
| `MaxDebtToIncomeRatio` | 0.50 | (existing debt + new EMI) / income |

CORS allows only the origins in `Cors:AllowedOrigins` (default `http://localhost:5173`).

## Repository layout

```text
.
├── backend/
│   ├── FinSight.slnx
│   ├── dotnet-tools.json            # pins dotnet-ef
│   ├── FinSight.Domain/             # entities + pure lending rules (no dependencies)
│   ├── FinSight.Infrastructure/     # EF Core DbContext, configurations, migrations, seed data
│   ├── FinSight.Application/        # DTOs, validators, services (use-case orchestration)
│   ├── FinSight.Api/                # controllers, middleware, Program.cs, configuration
│   └── FinSight.Tests/              # xUnit tests
├── frontend/                        # React + Vite (src/api, src/pages)
├── docs/                            # architecture notes, roadmap, generated schema SQL
├── docker-compose.yml               # local SQL Server 2022
└── .env.example                     # template for local secrets (.env is git-ignored)
```

## Security notes

- No secrets in source control: the connection string is in user secrets or env vars, and the SA password is in a git-ignored `.env`.
- The SQL Server port is bound to `127.0.0.1` only.
- Server-side validation on every input. DB check constraints and foreign keys back it up.
- Error responses never include stack traces, SQL or internal type names.
- Local development uses the `sa` login for simplicity. A deployed environment should use a least-privilege login.

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `503 The database is currently unavailable` | SQL Server isn't running or reachable: `docker compose ps`, then check the password in `.env` matches the user secret. |
| API exits with *Connection string 'FinSightDb' is not configured* | Run setup step 2. |
| `dotnet: command not found` | Add `~/.dotnet` to `PATH` (see Prerequisites). |
| Vite: *Port 5173 is in use* | Stop the other process; the port is fixed because CORS allows exactly that origin. |
