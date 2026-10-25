# Roadmap

## ✅ Days 1–3 (done)

- .NET 10 solution (Domain / Infrastructure / Application / Api / Tests), React + Vite frontend, CORS, Git, README
- EF Core + SQL Server: `LoanApplications`, `RuleAssessments`, `InitialCreate` migration, 17 synthetic seed records
- `GET/POST /api/applications`, `GET /api/applications/{id}`, `POST /api/applications/{id}/assess`
- EMI and DTI calculation, configurable product rules, missing/inconsistent-data flags
- 60 xUnit tests

## Day 4: Verify against SQL Server and add integration tests
- Run `dotnet ef database update` against a live SQL Server and verify the seed data, constraints and FK.
- Add API integration tests (`WebApplicationFactory` + a real SQL Server test database or Testcontainers).
- Add `AuditLogs` (create/assess actions) and `GET /api/applications/{id}/assessments` history.

## Day 5: Dashboard UI
- Applications list (search, status filter, paging, flag badges), detail view, "Run assessment" action.
- Create-application form that shows server-side validation errors per field.

## Day 6: AI analysis (OpenRouter, OpenAI-compatible)
- `AIAnalyses` table, `POST /api/applications/{id}/analyze`, `GET /api/applications/{id}/analysis`.
- Typed `HttpClient`, API key from user secrets, timeouts, strict JSON schema, validation before storing.
- The prompt includes the deterministic assessment and asks for a summary, missing info, inconsistencies and reviewer questions. The AI never makes a decision.
- **No paid calls without explicit approval.** Develop against a stub/fake client and use a free model for manual checks.

## Day 7: Razorpay IFSC lookup
- `GET /api/bank/ifsc/{code}` via typed `HttpClient`: format validation, 404 handling, timeout, `503` when unavailable, response caching.
- Optional: add an IFSC field to applications and show bank/branch in the UI.

## Day 8: Usage and cost monitoring
- `ApiUsageLogs` (provider, operation, latency, tokens, provider-reported or labelled-estimate cost, status).
- `GET /api/operations/metrics`, `GET /api/operations/ai-usage`, plus a dashboard panel.
- Skip AI calls when rules already show the application can't be evaluated, and cache analyses by an input hash.

## Day 9: Hardening
- Rate limiting on AI and IFSC endpoints, optional authentication with backend-enforced authorization.
- Least-privilege SQL login, structured logging, more tests.

## Day 10: Polish and demo
- UI polish, README screenshots, demo script, final test run, and a short architecture walkthrough.
